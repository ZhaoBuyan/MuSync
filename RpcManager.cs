using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
    /// <summary>单个播放器的轮询状态：最近信息、时间戳、错误码与降频控制。</summary>
    private class PlayerState
    {
        public IMusicPlayer? Player { get; set; }
        public PlayerInfo? LastPolledInfo { get; set; }
        public DateTime LastPollTime { get; set; } = DateTime.MinValue;
        /// <summary>最近一次「进程探测 + 轮询」的时间（用于非活跃播放器降频）。</summary>
        public DateTime LastCheckTime { get; set; } = DateTime.MinValue;
        public PlayerInfo? PendingUpdateInfo { get; set; }
        public DateTime LastChangeDetectedTime { get; set; } = DateTime.MinValue;
        /// <summary>
        /// 「<see cref="LastPolledInfo"/> 里那个播放位置」的采样时刻（仅当位置读数真的变了才更新）。
        /// <para>
        /// 为什么不能用 <see cref="LastPollTime"/>：那个每次轮询都会被刷新，而轮询每 233ms 一次，
        /// 于是「距上次轮询过了多久」永远是 0 → 进度推算等于没推算，只能等源自己上报新位置才跳一下
        /// （SMTC 类源表现为约 5 秒跳一次）。
        /// </para>
        /// </summary>
        public DateTime PositionSampleTime { get; set; } = DateTime.MinValue;
        public ErrorCode LastError { get; set; } = ErrorCode.None;
        /// <summary>DLL 未就绪（播放器启动竞态）的起始时间，宽限期内不报错。</summary>
        public DateTime DllNotFoundSinceUtc { get; set; } = DateTime.MinValue;
    }

    /// <summary>播放器读取错误码（无 / 权限不足 / DLL 未找到 / 版本不支持）。</summary>
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
    /// <summary>千千静听（TTPlayer 社区重制版）：走 SMTC，不读进程内存。</summary>
    private readonly PlayerState _ttPlayerState = new();
    /// <summary>浏览器媒体（Edge / Chrome 等网页播放）：走 SMTC，需在设置里开启（默认关）。</summary>
    private readonly PlayerState _browserState = new();
    private DateTime _lastMemoryLogTime = DateTime.MinValue;
    private volatile bool _stateRefreshRequested;
    private bool _realGameActivePreviously;
    private PlayerState? _lastActiveState;
    private bool _lastActiveInfoNull = true;
    private string? _lastPushedAppDisplay;
    private AppRule? _activeAppRule;
    private AppRule? _pendingAppRule;
    private string? _activeAppDisplay;
    private string? _activeAppIconPath;
    private DateTime _lastAppCheckTime = DateTime.MinValue;
    private List<string>? _cachedPlayerOrder;
    private string _cachedPlayerOrderSignature = "";
    /// <summary>
    /// 内置播放器优先级顺序（**不含**浏览器媒体）。
    /// internal 供测试核对「每个播放器在数据层、默认配置、界面键表里都不缺角色」。
    /// 浏览器媒体刻意不在这里：它在 ResolveActiveState 里固定排最后，也不出现在设置页的优先级列表中
    /// （放进来会与 SettingsForm.PlayerOrderKeys 的口径冲突）。
    /// </summary>
    internal static readonly string[] DefaultPlayerOrder = ["NetEase", "Tencent", "LxMusic", "KuGou", "TTPlayer"];
    private const double JumpToleranceSeconds = 0.4;
    private const double DebounceWindowSeconds = 1.5;
    // 进度条推送间隔：跟随用户档位（快速 0.25s / 标准 0.5s / 省流 1s）
    private static double ProgressUpdateIntervalSeconds => Configurations.Instance.Settings.SyncSpeed switch
    {
        SyncSpeedLevel.Fast => 0.25,
        SyncSpeedLevel.Economic => 1.0,
        _ => 0.5
    };
    // 有播放器在「播放」时高频轮询；空闲时降低频率省电
    private static readonly TimeSpan ActivePollInterval = TimeSpan.FromMilliseconds(233);
    /// <summary>
    /// 只检测到播放器进程、但它是暂停态的轮询间隔。
    /// <para>
    /// 暂停时播放位置不会变，没必要按 233ms 去读内存（既费 CPU，也平白多读播放器内存）。
    /// 降到 1 秒：恢复播放最多晚 ~1 秒被察觉（那之后立刻回全速）。
    /// </para>
    /// <para>
    /// ⚠️ 判定必须用「本轮有没有读到正在播放的源」，不能用「有没有检测到播放器进程」：
    /// 后者在暂停时同样为真，等于没降频。
    /// </para>
    /// </summary>
    private static readonly TimeSpan PausedPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromMilliseconds(1200);
    // 深度空闲（没音乐、也没程序来源，且已持续 ≥5 分钟）：进一步降到 3s 一轮省电
    private static readonly TimeSpan DeepIdlePollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DeepIdleAfter = TimeSpan.FromMinutes(5);
    private DateTime _idleSinceUtc = DateTime.MinValue;
    // 非活跃源的探测/轮询间隔（秒）：只有当前活跃源保持全速，节省内存读取与进程枚举
    private const double InactiveCheckIntervalSeconds = 1.0;
    // 进度推算判定"同一首歌内位置跳变"所需的最小间隔（秒）：间隔太短时位置样本噪声大
    // （SMTC 的位置是 5 秒级离散值），容易把正常播放误判成跳变
    private const double MinElapsedForProgressCheckSeconds = 3.0;
    private DateTime _lastProgressUpdateTime = DateTime.MinValue;
    private DateTime _lastTrayStatusUpdateTime = DateTime.MinValue;

    // ---- 稳定性兜底 ----
    // 主循环曾经整体停摆：进程与界面都健康，但循环卡在 `Task.Delay` 的回调上再也不动（回调从未被调用）。
    // 对策：节拍改成内核等待（不经过 .NET 定时器队列），另加心跳 + 独立看门狗线程，停摆即重启循环。
    /// <summary>节拍器：内核等待，可被提前唤醒（设置变化 / 重连 / 看门狗复位）。</summary>
    private readonly TickPacer _ticker = new();
    /// <summary>最近一次心跳的单调时刻（Environment.TickCount64 毫秒，不受系统时间调整影响）。</summary>
    private long _lastTickMs = Environment.TickCount64;
    /// <summary>累计心跳次数（诊断与日志用）。</summary>
    private long _tickCount;
    /// <summary>看门狗已重启主循环的次数。</summary>
    private int _loopRestartCount;
    /// <summary>心跳日志的节流时刻。</summary>
    private long _lastHeartbeatLogMs = Environment.TickCount64;
    /// <summary>当前这一份主循环的取消源（看门狗重启时会换新的一份）。</summary>
    private CancellationTokenSource? _loopCts;
    private bool _loopStarted;

    /// <summary>请求立即重推状态（设置变化 / 重连 / 系统唤醒），并唤醒节拍等待、不必等下一拍。</summary>
    public void RequestStateRefresh()
    {
        _stateRefreshRequested = true;
        _ticker.Wake();
    }

    /// <summary>
    /// 系统从睡眠 / 休眠恢复：先复位心跳，避免睡眠期间的「心跳空档」被看门狗判成停摆
    /// （Windows 的 TickCount64 是否合计睡眠时长并无保证，这里显式复位，两种口径都安全）。
    /// </summary>
    public void NotifySystemResumed() => MarkTick();

    /// <summary>
    /// 取某播放器「已按时间对表」的当前位置。**所有对外展示与推送都必须走这里**，
    /// 不要直接用 <see cref="PlayerState.LastPolledInfo"/> —— 那是源的原始读数，
    /// 可能 5 秒才更新一次（SMTC 类源），直接用会表现成一跳一跳。
    /// </summary>
    private static PlayerInfo? InterpolatedInfoOf(PlayerState state)
    {
        var info = state.LastPolledInfo;
        return info.HasValue
            ? InterpolatePosition(info.Value, state.PositionSampleTime, DateTime.UtcNow)
            : null;
    }

    public (PlayerInfo? PlayerInfo, string PlayerName) GetCurrentPlayerInfo()
    {
        var (state, name) = ResolveActiveState();
        return (state == null ? null : InterpolatedInfoOf(state), name);
    }

    /// <summary>当前生效的程序同步显示名（无则 null）。</summary>
    public string? GetActiveAppDisplay() => _activeAppDisplay;

    /// <summary>当前生效程序对应的规则（无则 null；供主界面判定状态点颜色）。</summary>
    public AppRule? GetActiveAppRule() => _activeAppRule;

    /// <summary>
    /// 前台命中、但按规则不允许显示的程序（例如新发现待用户确认的程序）。
    /// 只作状态展示用：它不参与推送，<see cref="_activeAppDisplay"/> 仍为 null。
    /// </summary>
    public AppRule? GetPendingAppRule() => _pendingAppRule;

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
            SnapshotOf(_lxMusicState, "洛雪音乐", active),
            SnapshotOf(_kuGouState, "酷狗音乐", active),
            SnapshotOf(_ttPlayerState, "千千静听", active),
            SnapshotOf(_browserState, "浏览器媒体", active)
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
            // 浏览器媒体固定排在最后，且不参与设置页的优先级列表
            _cachedPlayerOrder = priority.Concat(DefaultPlayerOrder).Distinct().Append("Browser").ToList();
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
        "LxMusic" => (_lxMusicState, "洛雪音乐"),
        "KuGou" => (_kuGouState, "酷狗音乐"),
        "TTPlayer" => (_ttPlayerState, "千千静听"),
        // 浏览器媒体不进用户优先级列表：它排最后，且只有设置里开启时才参与
        "Browser" => (_browserState, "浏览器媒体"),
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
        var song = state == null ? null : InterpolatedInfoOf(state);
        if (song != null || appDisplay != null)
        {
            Logger.Info($"[MuSync] 状态合成: 程序={appDisplay ?? "(无)"} 音乐={song?.Title ?? "(无)"}");
            await steamManager.UpdateStatusAsync(song, name, appDisplay);
        }
        else
        {
            Logger.Info("[MuSync] 无活跃源，按常驻状态设置处理");
            await steamManager.UpdateStatusAsync(null, name, null);
        }
    }

    /// <summary>
    /// 启动主循环与看门狗（立即返回，实际工作在后台）。
    /// 主循环负责轮询与状态同步；看门狗是独立线程（只用 Thread.Sleep），
    /// 发现循环停摆即写日志并重启循环（自愈）。
    /// </summary>
    public void Start(CancellationToken token = default)
    {
        _loopCts = new CancellationTokenSource();
        if (token.CanBeCanceled)
        {
            var loopCts = _loopCts;
            token.Register(() =>
            {
                try
                {
                    loopCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // 退出竞态：忽略
                }
            });
        }
        _loopStarted = true;
        MarkTick();
        LaunchLoop(_loopCts.Token);
        new Thread(WatchdogLoop)
        {
            IsBackground = true,
            Name = "MuSync.Watchdog"
        }.Start();
#if MUSYNC_NO_WATCHDOG_RESTART
        Logger.Warn("[Watchdog] 【诊断构建】自愈已禁用：停摆时只记录、不重启，仅供卡死取证，请勿日常使用");
#endif
        Logger.Info("[MuSync] 主循环已启动（节拍走内核等待，看门狗已就位）");
    }

    /// <summary>把主循环放到后台任务上跑（看门狗每次重启都会新起一份）。</summary>
    private void LaunchLoop(CancellationToken token) => _ = Task.Run(() => RunLoopAsync(token), token);

    /// <summary>主循环本体：轮询 → 合成 → 节拍等待。停摆由看门狗兜底，本方法只负责正常跑完一圈又一圈。</summary>
    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var currentTime = DateTime.UtcNow;
            // anyPlayerActive：本轮检测到播放器进程（决定"是否算空闲"）
            // anyPlayerPlaying：本轮读到**正在播放**的源（决定节拍快慢 —— 暂停时降到 1 秒）
            var anyPlayerActive = false;
            var anyPlayerPlaying = false;
            try
            {
                // 本 tick 的活跃源（活跃源全速轮询；其余降频，节省内存读取与进程枚举）
#if DEBUG || MUSYNC_STALL_TEST
                MaybeInjectTestStall();
#endif
                var activeSnapshot = ResolveActiveState().State;

                if (ShouldCheckPlayer(_netEaseState, activeSnapshot, currentTime))
                {
                    _netEaseState.LastCheckTime = currentTime;
                    var detected = await PollNetEaseAsync(currentTime);
                    anyPlayerActive |= detected;
                    anyPlayerPlaying |= detected && IsPlaying(_netEaseState);
                }

                if (ShouldCheckPlayer(_tencentState, activeSnapshot, currentTime))
                {
                    _tencentState.LastCheckTime = currentTime;
                    var detected = await PollTencentAsync(currentTime);
                    anyPlayerActive |= detected;
                    anyPlayerPlaying |= detected && IsPlaying(_tencentState);
                }

                if (ShouldCheckPlayer(_lxMusicState, activeSnapshot, currentTime))
                {
                    _lxMusicState.LastCheckTime = currentTime;
                    var detected = await PollLxMusicAsync(currentTime);
                    anyPlayerActive |= detected;
                    anyPlayerPlaying |= detected && IsPlaying(_lxMusicState);
                }

                if (ShouldCheckPlayer(_kuGouState, activeSnapshot, currentTime))
                {
                    _kuGouState.LastCheckTime = currentTime;
                    var detected = await PollKuGouAsync(currentTime);
                    anyPlayerActive |= detected;
                    anyPlayerPlaying |= detected && IsPlaying(_kuGouState);
                }

                if (ShouldCheckPlayer(_ttPlayerState, activeSnapshot, currentTime))
                {
                    _ttPlayerState.LastCheckTime = currentTime;
                    var detected = await PollTTPlayerAsync(currentTime);
                    anyPlayerActive |= detected;
                    anyPlayerPlaying |= detected && IsPlaying(_ttPlayerState);
                }

                if (ShouldCheckPlayer(_browserState, activeSnapshot, currentTime))
                {
                    _browserState.LastCheckTime = currentTime;
                    var detected = await PollBrowserAsync(currentTime);
                    anyPlayerActive |= detected;
                    anyPlayerPlaying |= detected && IsPlaying(_browserState);
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
                // 深度空闲判定：没音乐、没程序来源且连续空闲 ≥5 分钟 → 3s 一轮；任一项回来立刻回全速
                var idleNow = !anyPlayerActive && _activeAppDisplay == null;
                if (!idleNow)
                {
                    _idleSinceUtc = DateTime.MinValue;
                }
                else if (_idleSinceUtc == DateTime.MinValue)
                {
                    _idleSinceUtc = DateTime.UtcNow;
                }
                var idleFor = _idleSinceUtc == DateTime.MinValue ? TimeSpan.Zero : DateTime.UtcNow - _idleSinceUtc;
                MarkTick();
                var pausedOnly = anyPlayerActive && !anyPlayerPlaying;
                LogPollTier(anyPlayerPlaying ? 0
                    : pausedOnly ? 1
                    : _activeAppDisplay != null ? 2
                    : idleFor >= DeepIdleAfter ? 3 : 2);
                WaitForNextTick(
                    ComputePollDelay(
                        anyPlayerPlaying,
                        pausedOnly,
                        hasAppSource: _activeAppDisplay != null,
                        idleFor),
                    token);
            }
        }
        Logger.Info("[MuSync] 主循环已停止");
    }

    /// <summary>心跳：记录「本拍到此为止」的单调时刻，供看门狗判定停摆与写心跳日志。</summary>
    private void MarkTick()
    {
        Interlocked.Exchange(ref _lastTickMs, Environment.TickCount64);
        Interlocked.Increment(ref _tickCount);
    }

    /// <summary>距上一拍的时长（单调时钟，不受系统时间调整影响）。</summary>
    private TimeSpan SinceLastTick() =>
        TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _lastTickMs));

    /// <summary>
    /// 节拍等待：内核等待（TickPacer → WaitHandle），不依赖 .NET 定时器队列。
    /// 主循环曾经卡在 `Task.Delay` 的定时器回调上（回调整个丢失），换成内核等待后不再走那条路；
    /// </summary>
    private void WaitForNextTick(TimeSpan delay, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        try
        {
            _ticker.Wait(delay, token);
        }
        catch (OperationCanceledException)
        {
            // 程序退出或看门狗重启：结束本份循环
        }
    }

    /// <summary>
    /// 看门狗线程：只用 Thread.Sleep（内核等待），既不依赖 .NET 定时器，也不依赖线程池。
    /// 距上一拍超过阈值 → 判定停摆，记日志并重启主循环；平时每 5 分钟写一条心跳日志。
    /// 诊断构建（-p:MuSyncNoWatchdog=true）下改为「只记录、不重启」，保留卡死现场供取证。
    /// </summary>
    private void WatchdogLoop()
    {
        while (true)
        {
            try
            {
                Thread.Sleep(StabilityConfig.WatchdogPollInterval);
                var loopCts = _loopCts;
                if (!_loopStarted || loopCts == null || loopCts.IsCancellationRequested) return;
                var sinceLastTick = SinceLastTick();
                if (ShouldRestartLoop(_loopStarted, sinceLastTick, StabilityConfig.LoopStallThreshold))
                {
#if MUSYNC_NO_WATCHDOG_RESTART
                    LogStallWithoutRestart(sinceLastTick);
#else
                    RestartLoop(sinceLastTick);
#endif
                    continue;
                }
                LogHeartbeatIfDue(sinceLastTick);
            }
            catch (Exception ex)
            {
                // 看门狗自身绝不允许退出
                Logger.Error($"[Watchdog] 看门狗异常: {ex.Message}");
            }
        }
    }

    /// <summary>重启主循环（自愈）：复位心跳 → 取消旧循环 → 放掉它的节拍等待 → 新起一份。</summary>
    private void RestartLoop(TimeSpan sinceLastTick)
    {
        var attempt = Interlocked.Increment(ref _loopRestartCount);
        Logger.Error(
            $"[Watchdog] 主循环停摆 {sinceLastTick.TotalSeconds:F0} 秒（阈值 {StabilityConfig.LoopStallThreshold.TotalSeconds:F0} 秒），正在重启（第 {attempt} 次）");
        MarkTick();
        var oldCts = _loopCts;
        _loopCts = new CancellationTokenSource();
        try
        {
            oldCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 忽略
        }
        _ticker.Wake();
        LaunchLoop(_loopCts.Token);
        Logger.Info("[Watchdog] 主循环已重启");
    }

#if MUSYNC_NO_WATCHDOG_RESTART
    /// <summary>诊断构建专用：是否已经报告过停摆（避免每 5 秒重复刷同一条日志）。</summary>
    private bool _stallReported;

    /// <summary>
    /// 诊断构建（-p:MuSyncNoWatchdog=true）专用：检测到停摆时**只记录、不重启**，
    /// 把「卡住的现场」原样保留下来，便于从容取证。
    /// 取证步骤：任务管理器 → 详细信息 → MuSync.exe → 右键「创建转储文件」→ 再结束进程；
    /// 随后把 .DMP 交给 dotnet-dump analyze（dumpasync / clrthreads / threadpool）定位卡点。
    /// 常规构建不编入本方法，走 RestartLoop 自愈。
    /// </summary>
    private void LogStallWithoutRestart(TimeSpan sinceLastTick)
    {
        if (_stallReported) return;
        _stallReported = true;
        Logger.Error(
            $"[Watchdog] 主循环停摆 {sinceLastTick.TotalSeconds:F0} 秒（阈值 {StabilityConfig.LoopStallThreshold.TotalSeconds:F0} 秒）" +
            "——【诊断构建】不重启、保留现场；请现在用任务管理器对 MuSync.exe 执行「创建转储文件」，再结束进程");
    }
#endif

    /// <summary>心跳日志（默认每 5 分钟一条）：日志里能直接看到「最后一拍」是什么时候。</summary>
    private void LogHeartbeatIfDue(TimeSpan sinceLastTick)
    {
        var nowMs = Environment.TickCount64;
        if (nowMs - Interlocked.Read(ref _lastHeartbeatLogMs) <
            (long)StabilityConfig.HeartbeatLogInterval.TotalMilliseconds) return;
        Interlocked.Exchange(ref _lastHeartbeatLogMs, nowMs);
        Logger.Info(
            $"[Watchdog] 心跳正常：最后一拍 {sinceLastTick.TotalSeconds:F0} 秒前 | 累计 {Interlocked.Read(ref _tickCount)} 拍 | 循环重启 {_loopRestartCount} 次");
    }

    /// <summary>停摆判定（纯函数，便于单测）：循环已启动且距上一拍超过阈值 → 需要重启自愈。</summary>
    internal static bool ShouldRestartLoop(bool loopStarted, TimeSpan sinceLastTick, TimeSpan stallThreshold)
        => loopStarted && sinceLastTick > stallThreshold;
#if DEBUG || MUSYNC_STALL_TEST
    /// <summary>测试钩子是否已触发（Debug 构建，或显式 -p:MuSyncStallTest=true 的验证构建）。</summary>
    private bool _stallInjected;

    /// <summary>
    /// 看门狗自愈验证专用：环境变量 MUSYNC_TEST_STALL_MS 设为正整数时，主循环会故意卡死一次。
    /// 用法：MUSYNC_TEST_STALL_MS=60000 启动 Debug 构建，则约 30 秒后日志应出现看门狗重启记录。
    /// 常规构建（含全部发布产物）不编入本钩子；仅当显式传 -p:MuSyncStallTest=true 时才编入，
    /// 用于在 Release 配置下验证「检测停摆 → 重启循环 → 新循环恢复推送」这条完整路径。
    /// </summary>
    private void MaybeInjectTestStall()
    {
        if (_stallInjected) return;
        if (!int.TryParse(Environment.GetEnvironmentVariable("MUSYNC_TEST_STALL_MS"), out var stallMs) || stallMs <= 0)
        {
            return;
        }
        _stallInjected = true;
        Logger.Warn($"[MuSync] 测试钩子：主循环故意卡死 {stallMs} 毫秒（用于验证看门狗自愈）");
        Thread.Sleep(stallMs);
    }
#endif

    /// <summary>
    /// 本 tick 结束后要等多久。
    /// <para>有源**正在播放** → 全速；只检测到播放器进程但为暂停态 → 暂停档（1s）；</para>
    /// <para>有程序来源或空闲不足 5 分钟 → 普通空闲；深度空闲 → 3s。</para>
    /// <para>
    /// <paramref name="pausedOnly"/> 表示"有播放器进程、但本轮没读到正在播放的源"。
    /// 它与 <paramref name="anyPlayerPlaying"/> 同时为真是不可能的，调用方按实际状态传即可。
    /// </para>
    /// </summary>
    internal static TimeSpan ComputePollDelay(bool anyPlayerPlaying, bool pausedOnly, bool hasAppSource, TimeSpan idleFor)
    {
        if (anyPlayerPlaying) return ActivePollInterval;
        if (pausedOnly) return PausedPollInterval;
        if (hasAppSource) return IdlePollInterval;
        return idleFor >= DeepIdleAfter ? DeepIdlePollInterval : IdlePollInterval;
    }

    /// <summary>
    /// 该播放器是否**正在播放**（有信息且未暂停）。
    /// 暂停与"没读到信息"都算否 —— 节拍放慢的前提是位置不会变。
    /// </summary>
    private static bool IsPlaying(PlayerState state) => state.LastPolledInfo is { Pause: false };

    /// <summary>
    /// 节拍档位变了就写一条日志（只记变化，正常听歌下一小时也就几条）。
    /// 用途：暂停降频这类"看不见的行为"必须能从日志核对，否则只能靠猜。
    /// </summary>
    private void LogPollTier(int tier)
    {
        if (tier == _lastLoggedPollTier) return;
        _lastLoggedPollTier = tier;
        var text = tier switch
        {
            0 => "全速 233ms（有源正在播放）",
            1 => "暂停档 1s（播放器在，但已暂停）",
            2 => "空闲 1.2s",
            _ => $"深空闲 {DeepIdlePollInterval.TotalSeconds:F0}s"
        };
        Logger.Info($"[MuSync] 节拍切换: {text}");
    }
    private int _lastLoggedPollTier = -1;

    /// <summary>非活跃源降频：活跃源每 tick 检查；其余每 1 秒检查一次。</summary>
    private static bool ShouldCheckPlayer(PlayerState state, PlayerState? activeState, DateTime currentTime)    {
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

    /// <summary>
    /// 千千静听（TTPlayer 重制版）：探测进程 + 读 SMTC 会话。
    /// 与其它播放器的区别：不读进程内存、不需要 PID、不需要管理员权限，
    /// 因此没有 PermissionDenied / DllNotFound 这两类错误（SMTC 拿不到就是"没在播放"）。
    /// </summary>
    private async Task<bool> PollTTPlayerAsync(DateTime currentTime)
    {
        if (!IsTTPlayerRunning())
        {
            CleanupPlayerState(_ttPlayerState, "TTPlayer");
            RecordPollSuccess(_ttPlayerState);
            return false;
        }
        try
        {
            // SMTC 与进程无关，PID 传 0 占位（PollAndUpdatePlayer 只把它转给工厂）
            await PollAndUpdatePlayer(_ttPlayerState, "TTPlayer", 0,
                _ => new TTPlayerSmtc(), currentTime);
            RecordPollSuccess(_ttPlayerState);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TTPlayer] Error: {ex.Message}");
            _ttPlayerState.LastError = ErrorCode.None;
        }
        return true;
    }

    /// <summary>
    /// 浏览器媒体：读 SMTC 会话里浏览器正在播放的网页媒体。
    /// 与千千静听的区别：浏览器进程基本一直开着，所以"有没有在放"完全由 SMTC 会话决定；
    /// 另外它受设置开关控制（默认关），关掉时连 SMTC 都不查。
    /// </summary>
    private async Task<bool> PollBrowserAsync(DateTime currentTime)
    {
        if (!Configurations.Instance.Settings.BrowserMediaSyncEnabled || !IsAnyBrowserRunning())
        {
            CleanupPlayerState(_browserState, "Browser");
            RecordPollSuccess(_browserState);
            return false;
        }
        try
        {
            // SMTC 与进程无关，PID 传 0 占位（PollAndUpdatePlayer 只把它转给工厂）
            await PollAndUpdatePlayer(_browserState, "Browser", 0,
                _ => new BrowserMedia(), currentTime);
            RecordPollSuccess(_browserState);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Browser] Error: {ex.Message}");
            _browserState.LastError = ErrorCode.None;
        }
        return true;
    }

    /// <summary>
    /// 是否有任一常见浏览器在运行（无则不必去查 SMTC）。
    /// <para>
    /// ⚠️ 结果按 <see cref="BrowserCheckInterval"/> 缓存：本方法要枚举系统里全部进程，
    /// 而轮询在全速档下每 233ms 走一次 —— 不缓存就等于每秒枚举 4 次进程表。
    /// 浏览器是否运行不需要秒级精度（真启动了，下一轮 SMTC 读取自然会看到会话）。
    /// </para>
    /// </summary>
    private static bool IsAnyBrowserRunning()
    {
        var now = DateTime.UtcNow;
        if (now - _lastBrowserCheckUtc < BrowserCheckInterval) return _lastBrowserRunning;
        _lastBrowserCheckUtc = now;
        _lastBrowserRunning = IsAnyBrowserRunningCore();
        return _lastBrowserRunning;
    }

    private static DateTime _lastBrowserCheckUtc = DateTime.MinValue;
    private static bool _lastBrowserRunning;
    private static readonly TimeSpan BrowserCheckInterval = TimeSpan.FromSeconds(5);

    private static bool IsAnyBrowserRunningCore()
    {
        try
        {
            foreach (var process in System.Diagnostics.Process.GetProcesses())
            {
                using (process)
                {
                    if (BrowserProcessNames.Contains(process.ProcessName)) return true;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Browser] 枚举进程失败: {ex.Message}");
        }
        return false;
    }

    /// <summary>浏览器进程名（不含 .exe，与 Process.ProcessName 口径一致）。</summary>
    private static readonly System.Collections.Generic.HashSet<string> BrowserProcessNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "msedge", "chrome", "brave", "vivaldi", "opera", "firefox", "librewolf", "waterfox"
        };

    /// <summary>千千静听（重制版）主进程是否在运行。</summary>
    private static bool IsTTPlayerRunning()
    {
        try
        {
            return System.Diagnostics.Process.GetProcessesByName("TTPlayerRebuild").Length > 0;
        }
        catch
        {
            return false;
        }
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
        // 单次读取加超时：某个播放器卡住时本轮跳过、下一轮继续（LX Music 走 HTTP，其余为内存读取）
        var currentInfo = await state.Player.GetPlayerInfoAsync().WaitAsync(StabilityConfig.PlayerPollTimeout);
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
        // 只有位置读数"真的变了"才更新采样时刻：源的读数在两次上报之间是冻住的，
        // 这个时刻就是该位置的有效起点，进度推算以它为基准
        if (state.LastPolledInfo is not { } previousInfo ||
            !currentInfo.HasValue ||
            !previousInfo.Schedule.Equals(currentInfo.Value.Schedule))
        {
            state.PositionSampleTime = currentTime;
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
            _pendingAppRule = null;
            _activeAppDisplay = null;
            _activeAppIconPath = null;
        }
        else
        {
            var applied = false;
            ForegroundAppInfo? pendingForeground = null;
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
                else if (rule != null)
                {
                    // 命中规则但不允许显示（典型：新发现、等用户在设置里确认分类）
                    // → 记下来供主界面显示「待确认」状态点；不参与推送
                    pendingForeground = foreground;
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
            // 待确认程序（命中规则但不参与推送）：仅当当前没有生效程序时才作状态展示用
            _pendingAppRule = !applied && pendingForeground != null && _activeAppDisplay == null
                ? config.Apps.FirstOrDefault(r =>
                    r.ExeName.Equals(pendingForeground.ExeName, StringComparison.OrdinalIgnoreCase))
                : null;
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
        CleanupPlayerState(_ttPlayerState, "TTPlayer");
        _lastActiveState = null;
        _lastActiveInfoNull = true;
        _lastPushedAppDisplay = null;
        // 兜底：统一走状态合成（常驻状态开启时回到常驻文案，否则清除）
        _ = steamManager.UpdateStatusAsync(null, "", null);
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

    /// <summary>
    /// 判断本轮读取相对上一轮是否发生变化（换歌 / 暂停态变化 / 封面变化 / 同一首歌内进度跳变）。
    /// internal 便于单测（见 RpcManagerChangeDetectionTests）。
    /// </summary>
    internal static bool DetectStateChange(PlayerInfo? current, PlayerInfo? last, DateTime currentTime,
        DateTime lastTime, double tolerance)
    {
        if ((current is null && last is not null) || (current is not null && last is null)) return true;
        if (current is not { } c || last is not { } l) return false;
        if (c.Identity != l.Identity || c.Pause != l.Pause) return true;
        // 换歌判定：直接比较曲目信息。
        // ⚠️ 不能只靠下面的"进度推算"：SMTC 这类源的位置每 ~5 秒才更新一次，
        // 推算出的偏差常常小于容差，换歌会被误判成"没变化" → 状态迟迟不更新。
        if (IsDifferentTrack(c, l)) return true;
        // 封面变化也算状态变化：同曲目换封面时标题/歌手/专辑全都相同，
        // 早先会判成"没变化" → 界面封面不会刷新（实测复现：仅封面不同时本函数返回 False）。
        // 封面抖动风险已实测排除：QQ音乐连读 10 次封面 URL 稳定，网易云更是只在播放列表
        // JSON 变化时才重读 —— 因此这不会反复重置防抖窗口。
        if (!string.Equals(c.Cover, l.Cover, StringComparison.Ordinal)) return true;
        if (c.Pause) return false;
        // 进度推算只用于"同一首歌内位置是否跳变"（拖动进度条等）；
        // 轮询间隔太短时样本噪声大（SMTC 位置是 5 秒级的离散值），跳过以免误判
        var elapsed = (currentTime - lastTime).TotalSeconds;
        if (elapsed < MinElapsedForProgressCheckSeconds) return false;
        var progressDelta = c.Schedule - l.Schedule;
        return Math.Abs(progressDelta - elapsed) > tolerance;
    }

    /// <summary>曲目信息是否不同（换歌 / 切集 / 同曲换版本）。</summary>
    private static bool IsDifferentTrack(PlayerInfo current, PlayerInfo last) =>
        !string.Equals(current.Title, last.Title, StringComparison.Ordinal) ||
        !string.Equals(current.Artists, last.Artists, StringComparison.Ordinal) ||
        !string.Equals(current.Album, last.Album, StringComparison.Ordinal);

    private async Task UpdateOrClearSteamStatusAsync(PlayerInfo? info, string playerName,
        PlayerState? state = null, bool onlyIfChanged = true)
    {
        var appDisplay = _activeAppDisplay;
        if (info is not { } playerInfo)
        {
            // 无音乐：统一走状态合成（有程序显示程序，否则按常驻状态设置处理）
            await steamManager.UpdateStatusAsync(null, playerName, appDisplay);
            return;
        }
        // 有状态时把播放位置对表到此刻：源的位置读数可能已经放了好一会儿（约 5 秒一报的源），
        // 直接用旧读数会看着卡住；这里按「读数采样时刻 → 现在」补上流逝的时间。
        var toPush = info.Value;
        if (state != null)
        {
            toPush = InterpolatePosition(playerInfo, state.PositionSampleTime, DateTime.UtcNow);
            // onlyIfChanged：仅用于"内容没变就不发"的省流场景（拖拽防抖）。
            // 进度刷新必须传 false —— 时间在走、内容一直在变，用省流判断会把推送整个挡掉。
            if (onlyIfChanged)
            {
                var pauseChanged = state.LastPolledInfo is { } last && last.Pause != playerInfo.Pause;
                if (!pauseChanged) return;
            }
        }
        Debug.WriteLine(
            $"pause: {toPush.Pause}, progress: {toPush.Schedule}, duration: {toPush.Duration}");
        Debug.WriteLine(
            $"id: {toPush.Identity}, name: {toPush.Title}, singer: {toPush.Artists}, album: {toPush.Album}");
        await steamManager.UpdateStatusAsync(toPush, playerName, appDisplay);
        // 高频诊断：只写调试输出，不进日志文件（需要时可临时改成 Logger.Info 观察）
        System.Diagnostics.Debug.WriteLine(
            $"[推送] 位置 {toPush.Schedule:F1}s / 总 {toPush.Duration:F1}s（{playerName}）" +
            $"｜Steam 端最近记录：{steamManager.LastPushedStatus}");
    }

    /// <summary>
    /// 把播放位置推算到 <paramref name="now"/>：读数位置 + （now − 该读数的采样时刻），并按总时长封顶。
    /// 纯函数，便于单测（见 RpcManagerProgressInterpolationTests）。
    /// </summary>
    internal static PlayerInfo InterpolatePosition(PlayerInfo info, DateTime sampleTime, DateTime now)
    {
        if (info.Pause) return info;                      // 暂停时不推进
        if (sampleTime == DateTime.MinValue) return info; // 没有采样时刻（例如刚开始采集）时原样返回
        if (info.Duration <= 0) return info;              // 时长未知，无法判断边界
        var elapsed = (now - sampleTime).TotalSeconds;
        if (elapsed <= 0) return info;
        var schedule = Math.Min(info.Schedule + elapsed, info.Duration);
        return info with { Schedule = schedule };
    }

    private async Task UpdateProgressForActivePlayers(DateTime currentTime)
    {
        var (state, name) = ResolveActiveState();
        if (state?.LastPolledInfo is { Pause: false } info)
        {
            // 走统一的「对表 + 推送」路径；进度推进必须绕过"内容没变就不发"的省流守卫
            // （位置每 0.5 秒都在变，绕过守卫才能持续推）
            await UpdateOrClearSteamStatusAsync(info, name, state, onlyIfChanged: false);
            // 高频诊断：只写调试输出，不进日志文件
            System.Diagnostics.Debug.WriteLine(
                $"[进度] 读数 {info.Schedule:F1}s（采样于 {(currentTime - state.PositionSampleTime).TotalSeconds:F1}s 前）" +
                $"→ 推算 {InterpolatePosition(info, state.PositionSampleTime, currentTime).Schedule:F1}s / 总 {info.Duration:F1}s（{name}）");
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
