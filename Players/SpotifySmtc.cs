using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using MuSync.Models;
using MuSync.Players.Interfaces;
using MuSync.Utils;
using Windows.Media.Control;

namespace MuSync.Players;

/// <summary>
/// Spotify（Windows 桌面版）读取器：走 Windows 系统媒体控件（SMTC）。
/// <para>
/// 为什么不做内存读取：Spotify 是 Electron 外壳，内存布局随每次更新变化，靠特征码匹配既脆又危险；
/// 而桌面版本来就把曲目、播放状态和进度发布到 SMTC（媒体键与音量浮层里能看到的那张卡片就是它），
/// 读系统接口稳、不需要管理员权限、也不用跟着版本改偏移。
/// </para>
/// <para>
/// 识别口径（两种安装形态都要认）：
/// <list type="bullet">
/// <item>Microsoft Store 版：AUMID 形如 <c>SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify</c>，按前缀匹配</item>
/// <item>官网安装包版：Win32 程序，AppId 通常是 <c>Spotify.exe</c> 或完整路径，按包含 <c>Spotify.exe</c> 匹配</item>
/// </list>
/// 只认这两类，绝不"随便取第一个会话"，因此不会串到 QQ音乐 / 网易云 / 千千静听。
/// </para>
/// <para>
/// ⚠️ 与 <see cref="TTPlayerSmtc"/> 一样，本类**必须全程 async**，
/// 不得出现 <c>.GetAwaiter().GetResult()</c> / <c>.Result</c> / <c>.Wait()</c>：
/// 把 SMTC 的异步调用同步化会占住线程池，而主循环节拍依赖线程池，表现为「切歌完全不认」。
/// </para>
/// <para>
/// 📌 与 <see cref="TTPlayerSmtc"/> 的会话管理逻辑是同构的（有意复制）：两条链路互不影响，
/// 一边出问题不会连累另一边；两边共用同一个 <see cref="Smtc"/> 缓存管理器，不会多造 WinRT 对象。
/// </para>
/// </summary>
internal sealed class SpotifySmtc : IMusicPlayer
{
    /// <summary>Microsoft Store 版的 AppUserModelId 前缀。</summary>
    private const string StoreAppIdPrefix = "SpotifyAB.SpotifyMusic";

    /// <summary>官网安装包版的可执行文件名（AppId 可能是它本身，也可能是完整路径）。</summary>
    private const string DesktopAppIdMarker = "Spotify.exe";

    /// <summary>上次取会话管理器失败的时刻；失败后按 <see cref="ManagerRetryDelay"/> 退避重试，而不是永久放弃。</summary>
    private DateTime _managerFailedAtUtc = DateTime.MinValue;
    private static readonly TimeSpan ManagerRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>上一轮是否看到过 Spotify 的会话（区分"从来没在播"和"播过又丢了"）。</summary>
    private bool _sawSession;
    private int _lostPolls;

    /// <summary>连续多少轮"本来有、现在没有"才判定管理器假死。</summary>
    private const int LostPollsBeforeRebuild = 2;

    /// <summary>下一轮是否要求重建管理器（由假死判定置位）。</summary>
    private bool _forceRefresh;

    public async Task<PlayerInfo?> GetPlayerInfoAsync()
    {
        var session = await FindSessionAsync().ConfigureAwait(false);
        if (session == null) return null;
        return await ReadSessionAsync(session).ConfigureAwait(false);
    }

    private async Task<GlobalSystemMediaTransportControlsSession?> FindSessionAsync()
    {
        if (_managerFailedAtUtc != DateTime.MinValue &&
            DateTime.UtcNow - _managerFailedAtUtc < ManagerRetryDelay)
        {
            return null;
        }
        try
        {
            var manager = await Smtc.RequestManagerAsync(forceRefresh: _forceRefresh).ConfigureAwait(false);
            _forceRefresh = false;
            if (manager == null)
            {
                _managerFailedAtUtc = DateTime.UtcNow;
                return null;
            }

            var sessions = manager.GetSessions();
            var index = PickSessionIndex(Describe(sessions));
            if (index >= 0)
            {
                _sawSession = true;
                _lostPolls = 0;
                _managerFailedAtUtc = DateTime.MinValue;
                return sessions[index];
            }

            if (_sawSession && ++_lostPolls >= LostPollsBeforeRebuild)
            {
                _lostPolls = 0;
                _sawSession = false;
                _forceRefresh = true;
                Debug.WriteLine("[Spotify] 会话从有到无，判定会话管理器假死，下一轮重建");
            }
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Spotify] 取 SMTC 会话失败: {ex.Message}");
            Smtc.InvalidateManager();
            _forceRefresh = false;
            _managerFailedAtUtc = DateTime.UtcNow;
            return null;
        }
    }

    /// <summary>把 WinRT 会话对象压成纯数据快照，交给可单测的挑选函数。</summary>
    private static List<SmtcSessionSnapshot> Describe(
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
    {
        var list = new List<SmtcSessionSnapshot>(sessions.Count);
        foreach (var session in sessions)
        {
            var playing = false;
            try
            {
                playing = session.GetPlaybackInfo()?.PlaybackStatus ==
                          GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch
            {
                // 单个会话读取失败按"未播放"处理，不影响其它候选
            }
            // AppId 可能为 null：直接传给 Contains/StartsWith 会抛 NRE，
            // 被上层 catch 抓走后退避 30 秒，而且只要那个会话还在就会反复如此
            list.Add(new SmtcSessionSnapshot(session.SourceAppUserModelId ?? "", playing));
        }
        return list;
    }

    private static async Task<PlayerInfo?> ReadSessionAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var props = await session.TryGetMediaPropertiesAsync().AsTask().ConfigureAwait(false);
            var title = props?.Title ?? "";
            if (string.IsNullOrWhiteSpace(title)) return null;   // 没有曲目信息视为未播放
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var status = playback?.PlaybackStatus;
            // 封面字节取自**这一次**读取里的缩略图引用（连续快速调用会返回空）
            var cover = await Smtc.TryReadThumbnailAsync(props?.Thumbnail).ConfigureAwait(false);
            return BuildInfo(
                title,
                props?.Artist,
                props?.AlbumTitle,
                pause: status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                position: timeline?.Position.TotalSeconds ?? 0,
                duration: timeline?.EndTime.TotalSeconds ?? 0,
                coverImage: cover,
                lastUpdated: timeline?.LastUpdatedTime ?? default);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Spotify] 读取 SMTC 会话失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 把 SMTC 的原始字段映射成统一模型（纯函数，便于单测）。
    /// 进度/时长单位是秒；位置读数可能很久才更新一次，按采样时刻推算到当下（暂停时不推算）。
    /// </summary>
    internal static PlayerInfo BuildInfo(
        string title, string? artist, string? album, bool pause, double position, double duration,
        byte[]? coverImage = null, DateTimeOffset lastUpdated = default, DateTimeOffset? now = null)
    {
        var safeDuration = duration > 0 ? duration : 0;
        var safePosition = position > 0 ? position : 0;
        if (!pause && lastUpdated != default)
        {
            var elapsed = (now ?? DateTimeOffset.Now) - lastUpdated;
            if (elapsed > TimeSpan.Zero) safePosition += elapsed.TotalSeconds;
        }
        // 位置超过总长说明总长不可信（换了媒体但没刷新时长），把总长当未知
        if (safeDuration > 0 && safePosition > safeDuration) safeDuration = 0;
        return new PlayerInfo
        {
            Identity = "Spotify",
            Title = title.Trim(),
            Artists = string.IsNullOrWhiteSpace(artist) ? "" : artist.Trim(),
            Album = string.IsNullOrWhiteSpace(album) ? "" : album.Trim(),
            Cover = "",
            CoverImage = coverImage,
            Schedule = safePosition,
            Duration = safeDuration,
            Url = "",
            Pause = pause
        };
    }

    /// <summary>
    /// 这个 AppId 是不是 Spotify（纯函数，便于单测）。
    /// <para>
    /// 商店版按前缀匹配（后缀是包标识哈希，会随构建变化）；安装版按包含 <c>Spotify.exe</c> 匹配
    /// （AppId 可能是裸文件名，也可能是完整路径）。反过来，浏览器会话的 AppId 是浏览器自己的，
    /// 千千静听是 <c>Sonic853.TTPlayerRebuild…</c>，都不会命中这两个条件。
    /// </para>
    /// </summary>
    internal static bool IsSpotifyAppId(string appId) =>
        appId.StartsWith(StoreAppIdPrefix, StringComparison.OrdinalIgnoreCase) ||
        appId.Contains(DesktopAppIdMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 从候选会话里挑出 Spotify 的会话（纯函数，便于单测）。
    /// 同时装了商店版和安装版时，优先取「正在播放」的那个，否则取最靠前的；都不匹配返回 -1。
    /// </summary>
    internal static int PickSessionIndex(IReadOnlyList<SmtcSessionSnapshot> sessions)
    {
        var best = -1;
        for (var i = 0; i < sessions.Count; i++)
        {
            var snapshot = sessions[i];
            if (!IsSpotifyAppId(snapshot.AppId)) continue;
            if (snapshot.IsPlaying) return i;
            if (best < 0) best = i;
        }
        return best;
    }

    /// <summary>会话快照：只抽挑选需要的两件事，便于脱离 WinRT 单测。</summary>
    internal readonly record struct SmtcSessionSnapshot(string AppId, bool IsPlaying);

    /// <summary>释放会话管理器引用（SMTC 是系统共享对象，不需要主动销毁）。</summary>
    public void Dispose()
    {
        _sawSession = false;
        _lostPolls = 0;
        _forceRefresh = true;
    }
}
