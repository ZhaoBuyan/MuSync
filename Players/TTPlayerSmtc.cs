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
            return index < 0 ? null : sessions[index];
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
            list.Add(new SmtcSessionSnapshot(session.SourceAppUserModelId, playing));
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
            // 封面：SMTC 给的是图片流（实测专辑封面 300×300 JPEG）。读出字节直接交给界面解码，
            // 不走 URL 下载链路 —— 切换更快，也不需要临时文件。
            var cover = await Smtc.TryReadThumbnailAsync(session).ConfigureAwait(false);
            return BuildInfo(
                title,
                props?.Artist,
                props?.AlbumTitle,
                pause: status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                position: timeline?.Position.TotalSeconds ?? 0,
                duration: timeline?.EndTime.TotalSeconds ?? 0,
                coverImage: cover);
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
        byte[]? coverImage = null)
    {
        var safeDuration = duration > 0 ? duration : 0;
        var safePosition = position > 0 ? position : 0;
        if (safeDuration > 0 && safePosition > safeDuration) safePosition = safeDuration;   // 进度不越过总时长
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
