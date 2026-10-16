using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MuSync.Models;
using MuSync.Players.Interfaces;
using MuSync.Utils;
using Windows.Media.Control;

namespace MuSync.Players;

/// <summary>
/// 千千静听（TTPlayer 社区重制版）读取器：走 Windows 系统媒体控件（SMTC）。
/// <para>
/// 不走内存逆向的理由：该重制版主动把曲目信息、播放状态与进度发布到 SMTC，
/// 读系统接口比读进程内存稳（不怕对方改内存布局、不需要管理员权限、没有版本适配负担）。
/// </para>
/// <para>
/// 识别口径：按会话的 SourceAppUserModelId **前缀**匹配（实测形如
/// `Sonic853.TTPlayerRebuild.EDB4BA0433B1AC19`，后缀是包标识哈希，会随构建变化）。
/// 只认这一家，绝不"随便拿第一个会话"，因此不会串到 QQ音乐 / 网易云等其它播放器。
/// </para>
/// <para>
/// ⚠️ 本类**必须全程 async**，不得出现 `.GetAwaiter().GetResult()` / `.Result` / `.Wait()` 这类同步等待。
/// 把 SMTC 的异步调用同步化会阻塞线程池线程，而主循环节拍依赖线程池
/// → 线程池被占住后主循环停止推进，表现为「日志长时间空白、切歌完全不认」。
/// </para>
/// </summary>
internal sealed class TTPlayerSmtc : IMusicPlayer
{
    /// <summary>重制版的 AppUserModelId 前缀（实测值，见其 docs/SMTC.md 对应实现）。</summary>
    private const string AppIdPrefix = "Sonic853.TTPlayerRebuild";

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    /// <summary>上次取会话管理器失败的时刻；失败后按 <see cref="ManagerRetryDelay"/> 退避重试，而不是永久放弃。</summary>
    private DateTime _managerFailedAtUtc = DateTime.MinValue;
    private static readonly TimeSpan ManagerRetryDelay = TimeSpan.FromSeconds(30);
    /// <summary>会话管理器重建的最小间隔（见 FindSessionAsync 的失效说明）。</summary>
    private static readonly TimeSpan RebuildMinInterval = TimeSpan.FromSeconds(5);
    private DateTime _lastRebuildAttemptUtc = DateTime.MinValue;

    public async Task<PlayerInfo?> GetPlayerInfoAsync()
    {
        var session = await FindSessionAsync().ConfigureAwait(false);
        if (session == null) return null;
        return await ReadSessionAsync(session).ConfigureAwait(false);
    }

    private async Task<GlobalSystemMediaTransportControlsSession?> FindSessionAsync()
    {
        // 上次失败后先退避一会儿，避免每轮都去撞同一个失败点
        if (_manager == null && _managerFailedAtUtc != DateTime.MinValue &&
            DateTime.UtcNow - _managerFailedAtUtc < ManagerRetryDelay)
        {
            return null;
        }
        try
        {
            // 会话管理器只建一次
            if (_manager == null)
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask()
                    .ConfigureAwait(false);
                if (_manager == null)
                {
                    _managerFailedAtUtc = DateTime.UtcNow;
                    return null;
                }
            }
            var sessions = _manager.GetSessions();
            var index = PickSessionIndex(Describe(sessions), AppIdPrefix);
            if (index >= 0) return sessions[index];

            // ⚠️ 没命中时不能直接放弃：会话管理器在被反复快速调用后会**持续返回 0 个会话**
            // （这是实测确认过的失效，浏览器媒体那边同样踩过），而本方法每 233ms 就走一次。
            // 不重建的话，一旦失效就会永久报"没在播放"，只能等千千静听进程退出才自愈。
            // 重建限频 5 秒，避免"确实没在播放"时每拍都重建管理器。
            var now = DateTime.UtcNow;
            if (now - _lastRebuildAttemptUtc < RebuildMinInterval) return null;
            _lastRebuildAttemptUtc = now;
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask()
                .ConfigureAwait(false);
            if (_manager == null)
            {
                _managerFailedAtUtc = DateTime.UtcNow;
                return null;
            }
            _managerFailedAtUtc = DateTime.MinValue;
            var retrySessions = _manager.GetSessions();
            var retryIndex = PickSessionIndex(Describe(retrySessions), AppIdPrefix);
            return retryIndex < 0 ? null : retrySessions[retryIndex];
        }
        catch (Exception ex)
        {
            // SMTC 不可用不该影响主循环：记一次诊断，退避后再说
            Debug.WriteLine($"[TTPlayer] 取 SMTC 会话失败: {ex.Message}");
            _manager = null;
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
            // AppId 可能为 null（同仓库 Smtc.cs 对同一字段就写了 ?? ""）：
            // 直接传下去会让 PickSessionIndex 里的 StartsWith 抛 NRE，被上层 catch 抓走后退避 30 秒，
            // 而且只要那个会话还在就会反复如此
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
            // 封面字节取自**这一次**读取里的缩略图引用：
            // 不要再调一次 TryGetMediaPropertiesAsync（连续快速调用会返回空，导致来源时有时无）
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
            Debug.WriteLine($"[TTPlayer] 读取 SMTC 会话失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 把 SMTC 的原始字段映射成 MuSync 的统一模型（纯函数，便于单测）。
    /// 进度/时长单位是秒，与其它播放器一致（它们把毫秒乘 0.001）。
    /// </summary>
    internal static PlayerInfo BuildInfo(
        string title, string? artist, string? album, bool pause, double position, double duration,
        byte[]? coverImage = null, DateTimeOffset lastUpdated = default, DateTimeOffset? now = null)
    {
        var safeDuration = duration > 0 ? duration : 0;
        var safePosition = position > 0 ? position : 0;
        // 同浏览器媒体：位置读数可能很久才更新一次，按系统的采样时刻推算到当下，
        // 否则显示的进度会比实际播放位置小一大截（暂停时不推算）
        if (!pause && lastUpdated != default)
        {
            var elapsed = (now ?? DateTimeOffset.Now) - lastUpdated;
            if (elapsed > TimeSpan.Zero) safePosition += elapsed.TotalSeconds;
        }
        // ⚠️ 位置已超过总长时说明**总长不可信**（播放器换了媒体但没刷新时长），
        // 应把总长当"未知"（置 0），而不是用不可信的总长去砍可信的位置（否则进度永远停在满格）
        if (safeDuration > 0 && safePosition > safeDuration) safeDuration = 0;
        var artistText = string.IsNullOrWhiteSpace(artist) ? "" : artist.Trim();
        return new PlayerInfo
        {
            Identity = "TTPlayer",
            Title = title.Trim(),
            Artists = artistText,
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
    /// 从候选会话里挑出目标播放器的会话（纯函数，便于单测）。
    /// 同一个程序可能开出多个会话（实测出现过两个 TTPlayerRebuild 进程），
    /// 优先取「正在播放」的那个，否则取最靠前的；都不匹配返回 -1。
    /// </summary>
    internal static int PickSessionIndex(IReadOnlyList<SmtcSessionSnapshot> sessions, string appIdPrefix)
    {
        var best = -1;
        for (var i = 0; i < sessions.Count; i++)
        {
            var snapshot = sessions[i];
            if (!snapshot.AppId.StartsWith(appIdPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (snapshot.IsPlaying) return i;          // 正在播放的优先
            if (best < 0) best = i;                    // 否则记住第一个匹配的当兜底
        }
        return best;
    }

    /// <summary>会话快照：只把挑选需要的两件事抽出来，便于脱离 WinRT 单测。</summary>
    internal readonly record struct SmtcSessionSnapshot(string AppId, bool IsPlaying);

    /// <summary>释放会话管理器引用（SMTC 是系统共享对象，不需要我们主动销毁）。</summary>
    public void Dispose() => _manager = null;
}
