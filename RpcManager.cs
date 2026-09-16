using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MuSync.Models;
using MuSync.Players;
using MuSync.Players.Interfaces;
using MuSync.Utils;
namespace MuSync;
/// <summary>
/// 轮询各播放器并将状态同步到 Steam。
/// 多播放器仲裁规则：同一时刻只有一个"活跃源"拥有 Steam 状态发言权——
/// 正在播放（未暂停）的优先；其次是有信息（暂停中）的；都不满足则清除状态。
/// 非活跃源的状态变化只刷新界面，不会覆盖 Steam 状态。
/// </summary>
internal class RpcManager(SteamStatusManager steamManager)
{
    private class PlayerState
    {
        public IMusicPlayer? Player { get; set; }
        public PlayerInfo? LastPolledInfo { get; set; }
        public DateTime LastPollTime { get; set; } = DateTime.MinValue;
        /// <summary>最近一次「进程探测 + 轮询」的时间（用于非活跃播放器降频）。</summary>
        public DateTime LastCheckTime { get; set; } = DateTime.MinValue;
        public PlayerInfo? PendingUpdateInfo { get; set; }
        public DateTime LastChangeDetectedTime { get; set; } = DateTime.MinValue;
        public ErrorCode LastError { get; set; } = ErrorCode.None;
        /// <summary>DLL 未就绪（播放器启动竞态）的起始时间，宽限期内不报错。</summary>
        public DateTime DllNotFoundSinceUtc { get; set; } = DateTime.MinValue;
    }

    public enum ErrorCode
    {
        None,
        PermissionDenied,
        DllNotFound,
        VersionNotSupported
    }

    // DLL 持续缺失超过该时长才提示（避免播放器启动瞬间误报）
    private static readonly TimeSpan DllMissingGracePeriod = TimeSpan.FromSeconds(5);

    private readonly PlayerState _netEaseState = new();
    private readonly PlayerState _tencentState = new();
    private readonly PlayerState _lxMusicState = new();
    private readonly PlayerState _kuGouState = new();
    private DateTime _lastMemoryLogTime = DateTime.MinValue;
    private volatile bool _stateRefreshRequested;
    private bool _realGameActivePreviously;
    private PlayerState? _lastActiveState;
    private bool _lastActiveInfoNull = true;
    private string? _lastPushedAppDisplay;
    private AppRule? _activeAppRule;
    private string? _activeAppDisplay;
    private string? _activeAppIconPath;
    private DateTime _lastAppCheckTime = DateTime.MinValue;
    private List<string>? _cachedPlayerOrder;
    private string _cachedPlayerOrderSignature = "";
    private static readonly string[] DefaultPlayerOrder = ["NetEase", "Tencent", "LxMusic", "KuGou"];
    private const double JumpToleranceSeconds = 0.4;
    private const double DebounceWindowSeconds = 1.5;
    // 进度条推送间隔：跟随用户档位（快速 0.25s / 标准 0.5s / 省流 1s）
    private static double ProgressUpdateIntervalSeconds => Configurations.Instance.Settings.SyncSpeed switch
    {
        SyncSpeedLevel.Fast => 0.25,
        SyncSpeedLevel.Economic => 1.0,
        _ => 0.5
    };
    // 有播放器在跑时高频轮询；空闲时降低频率省电
    private static readonly TimeSpan ActivePollInterval = TimeSpan.FromMilliseconds(233);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromMilliseconds(1200);
    // 非活跃源的探测/轮询间隔（秒）：只有当前活跃源保持全速，节省内存读取与进程枚举
    private const double InactiveCheckIntervalSeconds = 1.0;
    private DateTime _lastProgressUpdateTime = DateTime.MinValue;
    private DateTime _lastTrayStatusUpdateTime = DateTime.MinValue;

    public void RequestStateRefresh() => _stateRefreshRequested = true;

    public (PlayerInfo? PlayerInfo, string PlayerName) GetCurrentPlayerInfo()
    {
        var (state, name) = ResolveActiveState();
        return (state?.LastPolledInfo, name);
    }

    /// <summary>当前生效的程序同步显示名（无则 null）。</summary>
    public string? GetActiveAppDisplay() => _activeAppDisplay;

    /// <summary>当前生效程序的可执行文件路径（用于显示图标，可能为空）。</summary>
    public string? GetActiveAppIconPath() => _activeAppIconPath;

    /// <summary>当前生效程序的分类中文名（无则空串）。</summary>
    public string GetActiveAppCategoryText() => _activeAppRule?.Category switch
    {
        AppCategory.Game => Loc.L("游戏", "Game"),
        AppCategory.Work => Loc.L("工作", "Work"),
        AppCategory.Media => Loc.L("媒体", "Media"),
        AppCategory.Social => Loc.L("社交", "Social"),
        AppCategory.Other => Loc.L("其他", "Other"),
        AppCategory.Ignore => Loc.L("忽略", "Ignore"),
        _ => ""
    };

    /// <summary>各播放器的诊断快照（供「复制诊断信息」使用）。</summary>
    public List<PlayerStatus> GetPlayerStatusSnapshot()
    {
        var active = ResolveActiveState().State;
        return
        [
            SnapshotOf(_netEaseState, "网易云音乐", active),
            SnapshotOf(_tencentState, "QQ音乐", active),
            SnapshotOf(_lxMusicState, "LX Music", active),
            SnapshotOf(_kuGouState, "酷狗音乐", active)
        ];

        static PlayerStatus SnapshotOf(PlayerState state, string name, PlayerState? activeState)
        {
            var info = state.LastPolledInfo;
            return new PlayerStatus(
                name,
                state.Player != null,
                info?.Title ?? "",
                info?.Artists ?? "",
                info?.Pause ?? false,
                state.LastError,
                state == activeState);
        }
    }

    /// <summary>
    /// 按优先级挑选当前应展示到 Steam 的播放器：
    /// 第一优先：正在播放的；第二优先：有信息但暂停的。
    /// </summary>
    private (PlayerState? State, string Name) ResolveActiveState()
    {
        // 顺序来自设置（缺项自动补全），正在播放的始终优先于暂停的
        var order = GetPlayerOrder();
        var playing = ResolveActiveStatePass(order, playingOnly: true);
        return playing.State != null ? playing : ResolveActiveStatePass(order, playingOnly: false);
    }

    /// <summary>播放器优先顺序（按设置缓存，设置变化时自动失效；避免每 tick 重复分配）。</summary>
    private List<string> GetPlayerOrder()
    {
        var priority = Configurations.Instance.Settings.PlayerPriority ?? [];
        var signature = string.Join('|', priority);
        if (_cachedPlayerOrder is null || signature != _cachedPlayerOrderSignature)
        {
            _cachedPlayerOrderSignature = signature;
            _cachedPlayerOrder = priority.Concat(DefaultPlayerOrder).Distinct().ToList();
        }
        return _cachedPlayerOrder;
    }

    private (PlayerState? State, string Name) ResolveActiveStatePass(List<string> order, bool playingOnly)
    {
        foreach (var key in order)
        {
            var entry = GetPlayerEntry(key);
            if (entry is not { } pair) continue;
            if (pair.State is not { Player: not null, LastPolledInfo: not null }) continue;
            if (playingOnly && pair.State.LastPolledInfo is { Pause: true }) continue;
            return pair;
        }
        return (null, "");
    }

    private (PlayerState State, string Name)? GetPlayerEntry(string key) => key switch
    {
        "NetEase" => (_netEaseState, "网易云音乐"),
        "Tencent" => (_tencentState, "QQ音乐"),
        "LxMusic" => (_lxMusicState, "LX Music"),
        "KuGou" => (_kuGouState, "酷狗音乐"),
        _ => null
    };

    /// <summary>活跃源（音乐/程序）变化时同步一次 Steam 状态；force 则无条件推送当前组合。</summary>
    private async Task SynchronizeActiveSourceAsync(bool force = false)
    {
        var (state, name) = ResolveActiveState();
        var infoNull = state?.LastPolledInfo is null;
        var appDisplay = _activeAppDisplay;
        if (!force && state == _lastActiveState && infoNull == _lastActiveInfoNull &&
            appDisplay == _lastPushedAppDisplay)
        {
            return;
        }
        _lastActiveState = state;
        _lastActiveInfoNull = infoNull;
        _lastPushedAppDisplay = appDisplay;
        var song = state?.LastPolledInfo;
        if (song != null || appDisplay != null)
        {
            Logger.Info($"[MuSync] 状态合成: 程序={appDisplay ?? "(无)"} 音乐={song?.Title ?? "(无)"}");
            await steamManager.UpdateStatusAsync(song, name, appDisplay);
        }
        else
        {
            Logger.Info("[MuSync] 无活跃源，清除 Steam 状态");
            steamManager.ClearStatus();
        }
    }

    public async Task Start()
    {
        while (true)
        {
            var currentTime = DateTime.UtcNow;
            var anyPlayerActive = false;
            try
            {
                // 本 tick 的活跃源（活跃源全速轮询；其余降频，节省内存读取与进程枚举）
                var activeSnapshot = ResolveActiveState().State;

                if (ShouldCheckPlayer(_netEaseState, activeSnapshot, currentTime))
                {
                    _netEaseState.LastCheckTime = currentTime;
                    anyPlayerActive |= await PollNetEaseAsync(currentTime);
                }

                if (ShouldCheckPlayer(_tencentState, activeSnapshot, currentTime))
                {
                    _tencentState.LastCheckTime = currentTime;
                    anyPlayerActive |= await PollTencentAsync(currentTime);
                }

                if (ShouldCheckPlayer(_lxMusicState, activeSnapshot, currentTime))
                {
                    _lxMusicState.LastCheckTime = currentTime;
                    anyPlayerActive |= await PollLxMusicAsync(currentTime);
                }

                if (ShouldCheckPlayer(_kuGouState, activeSnapshot, currentTime))
                {
                    _kuGouState.LastCheckTime = currentTime;
                    anyPlayerActive |= await PollKuGouAsync(currentTime);
                }

                // 内存快照：每 10 分钟记一次（用于排查内存增长趋势）
                if ((currentTime - _lastMemoryLogTime).TotalMinutes >= 10)
                {
                    _lastMemoryLogTime = currentTime;
                    Logger.MemorySnapshot();
                }

                // 程序同步：每秒检测前台程序（含自动发现），变化时立即刷新状态
                if ((currentTime - _lastAppCheckTime).TotalSeconds >= 1)
                {
                    _lastAppCheckTime = currentTime;
                    try
                    {
                        if (UpdateActiveApp(currentTime))
                        {
                            await SynchronizeActiveSourceAsync(force: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        // 程序检测异常不得影响音乐轮询
                        Debug.WriteLine($"[MuSync] 程序检测异常: {ex.Message}");
                        Logger.Error($"[MuSync] 程序检测异常: {ex.Message}");
                    }
                }
                // 活跃源仲裁：变化时自动切换 Steam 状态
                await SynchronizeActiveSourceAsync();

                var realGamePause = steamManager.IsRealGameActive &&
                                    Configurations.Instance.Settings.PauseWhenPlayingGame;
                if (realGamePause != _realGameActivePreviously)
                {
                    _realGameActivePreviously = realGamePause;
                    if (realGamePause)
                    {
                        Debug.WriteLine("[MuSync] 真实游戏进行中，暂停 Steam 状态同步");
                        Logger.Info("[MuSync] 真实游戏进行中，暂停 Steam 状态同步");
                        steamManager.ClearStatus();
                    }
                    else
                    {
                        Debug.WriteLine("[MuSync] 真实游戏已结束，恢复 Steam 状态同步");
                        Logger.Info("[MuSync] 真实游戏已结束，恢复 Steam 状态同步");
                        await SynchronizeActiveSourceAsync(force: true);
                    }
                }
                if (_stateRefreshRequested)
                {
                    _stateRefreshRequested = false;
                    Debug.WriteLine("Settings changed. Refreshing current Steam status.");
                    await SynchronizeActiveSourceAsync(force: true);
                }
                if ((currentTime - _lastProgressUpdateTime).TotalSeconds >= ProgressUpdateIntervalSeconds)
                {
                    await UpdateProgressForActivePlayers(currentTime);
                    _lastProgressUpdateTime = currentTime;
                }
                if ((currentTime - _lastTrayStatusUpdateTime).TotalSeconds >= 5)
                {
                    Program.UpdateTrayStatus();
                    _lastTrayStatusUpdateTime = currentTime;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FATAL ERROR] An exception occurred in the main poll loop: {ex.Message}");
                Logger.Error($"[FATAL ERROR] An exception occurred in the main poll loop: {ex.Message}");
                ClearAllPlayers();
            }
            finally
            {
                await Task.Delay(anyPlayerActive ? ActivePollInterval : IdlePollInterval);
            }
        }
    }

    /// <summary>非活跃源降频：活跃源每 tick 检查；其余每 1 秒检查一次。</summary>
    private static bool ShouldCheckPlayer(PlayerState state, PlayerState? activeState, DateTime currentTime)
    {
        if (state == activeState) return true;
        return (currentTime - state.LastCheckTime).TotalSeconds >= InactiveCheckIntervalSeconds;
    }

    /// <summary>网易云：探测进程 + 轮询。返回是否检测到进程（决定主循环频率）。</summary>
    private async Task<bool> PollNetEaseAsync(DateTime currentTime)
    {
        var hwnd = Win32Api.User32.FindWindow("OrpheusBrowserHost", null);
        if (hwnd != IntPtr.Zero &&
            Win32Api.User32.GetWindowThreadProcessId(hwnd, out var pid) != 0)
        {
            try
            {
                await PollAndUpdatePlayer(_netEaseState, "NetEase CloudMusic", pid,
                    p => new NetEase(p), currentTime);
                RecordPollSuccess(_netEaseState);
            }
            catch (UnauthorizedAccessException)
            {
                if (_netEaseState.LastError != ErrorCode.PermissionDenied)
                {
                    Logger.Error("[NetEase] 无权限读取进程内存，可能需要以管理员身份运行。");
                }
                _netEaseState.LastError = ErrorCode.PermissionDenied;
            }
            catch (DllNotFoundException)
            {
                // cloudmusic.dll 通常比主窗口晚加载：宽限期后仍失败才提示
                RecordDllMissing(_netEaseState);
                Debug.WriteLine("[NetEase] 等待 cloudmusic.dll 加载 (DllNotFound).");
            }
            catch (EntryPointNotFoundException)
            {
                if (_netEaseState.LastError != ErrorCode.VersionNotSupported)
                {
                    Logger.Error("[NetEase] 内存特征码未命中，播放器版本可能不兼容。");
                }
                _netEaseState.LastError = ErrorCode.VersionNotSupported;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NetEase] Error: {ex.Message}");
                _netEaseState.LastError = ErrorCode.None;
            }
            return true;
        }
        CleanupPlayerState(_netEaseState, "NetEase CloudMusic");
        RecordPollSuccess(_netEaseState);
        return false;
    }

    /// <summary>QQ 音乐：探测进程 + 轮询。返回是否检测到进程。</summary>
    private async Task<bool> PollTencentAsync(DateTime currentTime)
    {
        var hwnd = Win32Api.User32.FindWindow("QQMusic_Daemon_Wnd", null);
        if (hwnd != IntPtr.Zero &&
            Win32Api.User32.GetWindowThreadProcessId(hwnd, out var pid) != 0)
        {
            try
            {
                await PollAndUpdatePlayer(_tencentState, "Tencent QQMusic", pid,
                    p => new Tencent(p), currentTime);
                RecordPollSuccess(_tencentState);
            }
            catch (UnauthorizedAccessException)
            {
                if (_tencentState.LastError != ErrorCode.PermissionDenied)
                {
                    Logger.Error("[Tencent] 无权限读取进程内存，可能需要以管理员身份运行。");
                }
                _tencentState.LastError = ErrorCode.PermissionDenied;
            }
            catch (DllNotFoundException)
            {
                RecordDllMissing(_tencentState);
                Debug.WriteLine("[Tencent] 等待 QQMusic.dll 加载 (DllNotFound).");
            }
            catch (EntryPointNotFoundException)
            {
                if (_tencentState.LastError != ErrorCode.VersionNotSupported)
                {
                    Logger.Error("[Tencent] 内存特征码未命中，播放器版本可能不兼容。");
                }
                _tencentState.LastError = ErrorCode.VersionNotSupported;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Tencent] Error: {ex.Message}");
                _tencentState.LastError = ErrorCode.None;
            }
            return true;
        }
        CleanupPlayerState(_tencentState, "Tencent QQMusic");
        RecordPollSuccess(_tencentState);
        return false;
    }

    /// <summary>LX Music：探测进程 + 轮询。返回是否检测到进程。</summary>
    private async Task<bool> PollLxMusicAsync(DateTime currentTime)
    {
        var lxProcess = Process.GetProcessesByName("lx-music-desktop").FirstOrDefault();
        if (lxProcess != null)
        {
            try
            {
                await PollAndUpdatePlayer(_lxMusicState, "LX Music", lxProcess.Id,
                    p => new LxMusic(p), currentTime);
                RecordPollSuccess(_lxMusicState);
            }
            catch (UnauthorizedAccessException)
            {
                if (_lxMusicState.LastError != ErrorCode.PermissionDenied)
                {
                    Logger.Error("[LX Music] 无权限读取进程信息。");
                }
                _lxMusicState.LastError = ErrorCode.PermissionDenied;
            }
            catch (DllNotFoundException)
            {
                RecordDllMissing(_lxMusicState);
                Debug.WriteLine("[LX Music] 等待组件加载 (DllNotFound).");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LX Music] Error: {ex.Message}");
                _lxMusicState.LastError = ErrorCode.None;
            }
            return true;
        }
        CleanupPlayerState(_lxMusicState, "LX Music");
        RecordPollSuccess(_lxMusicState);
        return false;
    }

    /// <summary>酷狗：探测主进程 + 轮询（进程更替时重建实例）。返回是否检测到进程。</summary>
    private async Task<bool> PollKuGouAsync(DateTime currentTime)
    {
        var kugouProcess = KuGou.FindMainProcess();
        if (kugouProcess != null)
        {
            // 酷狗重启（进程更替）后重建读取实例
            if (_kuGouState.Player is KuGou kuGouPlayer && kuGouPlayer.Pid != kugouProcess.Id)
            {
                Debug.WriteLine("[KuGou] Player process changed. Recreating instance.");
                CleanupPlayerState(_kuGouState, "KuGou");
            }
            try
            {
                await PollAndUpdatePlayer(_kuGouState, "KuGou", kugouProcess.Id,
                    p => new KuGou(p), currentTime);
                RecordPollSuccess(_kuGouState);
            }
            catch (UnauthorizedAccessException)
            {
                if (_kuGouState.LastError != ErrorCode.PermissionDenied)
                {
                    Logger.Error("[KuGou] 无权限读取进程内存，可能需要以管理员身份运行。");
                }
                _kuGouState.LastError = ErrorCode.PermissionDenied;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KuGou] Error: {ex.Message}");
                _kuGouState.LastError = ErrorCode.None;
            }
            return true;
        }
        CleanupPlayerState(_kuGouState, "KuGou");
        RecordPollSuccess(_kuGouState);
        return false;
    }

    private async Task PollAndUpdatePlayer(PlayerState state, string playerName,
        int pid,
        Func<int, IMusicPlayer> playerFactory, DateTime currentTime)
    {
        if (state.Player is null)
        {
            Debug.WriteLine($"[{playerName}] Player process detected. Creating instance.");
            state.Player = playerFactory(pid);
        }
        var currentInfo = await state.Player.GetPlayerInfoAsync();
        var isStateChanged = DetectStateChange(currentInfo, state.LastPolledInfo, currentTime, state.LastPollTime,
            JumpToleranceSeconds);
        if (isStateChanged)
        {
            Debug.WriteLine(
                $"[{playerName}] State change detected. Resetting debounce timer for: {currentInfo?.Title ?? "None (Clear)"}");
            state.PendingUpdateInfo = currentInfo;
            state.LastChangeDetectedTime = currentTime;
        }
        if (state.PendingUpdateInfo is not null &&
            (currentTime - state.LastChangeDetectedTime).TotalSeconds > DebounceWindowSeconds)
        {
            // 只有活跃源才允许把变化推送到 Steam，其余播放器变化只影响界面
            if (state == ResolveActiveState().State)
            {
                Debug.WriteLine($"[{playerName}] Debounce window passed. Sending Steam status update.");
                await UpdateOrClearSteamStatusAsync(state.PendingUpdateInfo, playerName);
            }
            state.PendingUpdateInfo = null;
        }
        state.LastPolledInfo = currentInfo;
        state.LastPollTime = currentTime;
    }

    /// <summary>更新当前生效的程序（前台匹配 / 运行即显示），返回显示名是否变化。</summary>
    private bool UpdateActiveApp(DateTime currentTime)
    {
        var config = Configurations.Instance.Settings;
        var previous = _activeAppDisplay;
        if (!config.AppSyncEnabled)
        {
            _activeAppRule = null;
            _activeAppDisplay = null;
            _activeAppIconPath = null;
        }
        else
        {
            var applied = false;
            var foreground = ForegroundWatcher.GetCurrent();
            if (foreground != null)
            {
                var (rule, isIgnored) = AppRuleManager.Match(foreground, config);
                if (rule != null && AppRuleManager.IsAllowedToDisplay(rule, config))
                {
                    _activeAppRule = rule;
                    _activeAppDisplay = AppRuleManager.DisplayNameOf(rule);
                    _activeAppIconPath = foreground.ExePath;
                    applied = true;
                }
                else if (isIgnored)
                {
                    // 忽略类程序在前台：保持现状不动（不清除、不切换）
                    applied = true;
                }
            }
            if (!applied)
            {
                var alwaysRule = AppRuleManager.FindRunningAlwaysRule(config, currentTime);
                _activeAppRule = alwaysRule;
                _activeAppDisplay = alwaysRule == null ? null : AppRuleManager.DisplayNameOf(alwaysRule);
                _activeAppIconPath = alwaysRule == null
                    ? null
                    : AppRuleManager.GetRunningProcessPath(alwaysRule.ExeName);
            }
        }
        if (previous == _activeAppDisplay) return false;
        Logger.Info($"[MuSync] 程序同步: {previous ?? "(无)"} -> {_activeAppDisplay ?? "(无)"}");
        return true;
    }

    private void CleanupPlayerState(PlayerState state, string playerName)
    {
        if (state.Player is null) return;
        Debug.WriteLine($"[{playerName}] Player process lost. Clearing local state (active source re-evaluated).");
        // 释放播放器持有的进程句柄等资源（如网易云/QQ音乐的 ProcessMemory）
        state.Player.Dispose();
        state.Player = null;
        state.LastPolledInfo = null;
        state.PendingUpdateInfo = null;
    }

    private void ClearAllPlayers()
    {
        CleanupPlayerState(_netEaseState, "NetEase CloudMusic");
        CleanupPlayerState(_tencentState, "Tencent QQMusic");
        CleanupPlayerState(_lxMusicState, "LX Music");
        CleanupPlayerState(_kuGouState, "KuGou");
        _lastActiveState = null;
        _lastActiveInfoNull = true;
        _lastPushedAppDisplay = null;
        steamManager.ClearStatus();
    }

    private static void RecordPollSuccess(PlayerState state)
    {
        state.DllNotFoundSinceUtc = DateTime.MinValue;
    }

    private static void RecordDllMissing(PlayerState state)
    {
        if (state.DllNotFoundSinceUtc == DateTime.MinValue)
        {
            state.DllNotFoundSinceUtc = DateTime.UtcNow;
        }
        var newError = (DateTime.UtcNow - state.DllNotFoundSinceUtc) >= DllMissingGracePeriod
            ? ErrorCode.DllNotFound
            : ErrorCode.None;
        if (newError == ErrorCode.DllNotFound && state.LastError != ErrorCode.DllNotFound)
        {
            Logger.Warn("播放器核心组件持续未就绪 (DllNotFound)");
        }
        state.LastError = newError;
    }

    private static bool DetectStateChange(PlayerInfo? current, PlayerInfo? last, DateTime currentTime,
        DateTime lastTime, double tolerance)
    {
        if ((current is null && last is not null) || (current is not null && last is null)) return true;
        if (current is not { } c || last is not { } l) return false;
        if (c.Identity != l.Identity || c.Pause != l.Pause) return true;
        if (c.Pause) return false;
        var elapsed = (currentTime - lastTime).TotalSeconds;
        var progressDelta = c.Schedule - l.Schedule;
        return Math.Abs(progressDelta - elapsed) > tolerance;
    }

    private async Task UpdateOrClearSteamStatusAsync(PlayerInfo? info, string playerName)
    {
        var appDisplay = _activeAppDisplay;
        if (info is not { } playerInfo)
        {
            if (appDisplay == null)
            {
                steamManager.ClearStatus();
            }
            else
            {
                await steamManager.UpdateStatusAsync(null, playerName, appDisplay);
            }
            return;
        }
        Debug.WriteLine(
            $"pause: {playerInfo.Pause}, progress: {playerInfo.Schedule}, duration: {playerInfo.Duration}");
        Debug.WriteLine(
            $"id: {playerInfo.Identity}, name: {playerInfo.Title}, singer: {playerInfo.Artists}, album: {playerInfo.Album}");
        await steamManager.UpdateStatusAsync(info, playerName, appDisplay);
    }

    private async Task UpdateProgressForActivePlayers(DateTime currentTime)
    {
        var (state, name) = ResolveActiveState();
        if (state?.LastPolledInfo is { Pause: false } info)
        {
            var elapsedSincePoll = (currentTime - state.LastPollTime).TotalSeconds;
            var interpolatedSchedule = info.Schedule + elapsedSincePoll;
            var clampedSchedule = Math.Min(interpolatedSchedule, info.Duration);
            var updatedInfo = info with { Schedule = clampedSchedule };
            await steamManager.UpdateStatusAsync(updatedInfo, name, _activeAppDisplay);
        }
    }
}

/// <summary>单个播放器的诊断快照（供「复制诊断信息」使用）。</summary>
internal readonly record struct PlayerStatus(
    string Name,
    bool Running,
    string Title,
    string Artists,
    bool Pause,
    RpcManager.ErrorCode LastError,
    bool IsActive);
