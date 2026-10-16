using System;
using System.Threading.Tasks;
using MuSync.Models;
using MuSync.Players.Interfaces;
using MuSync.Utils;

namespace MuSync.Players;

/// <summary>
/// 浏览器媒体读取器：读取浏览器里正在播放的网页媒体（视频 / 音频），走 Windows 系统媒体控件（SMTC）。
/// <para>
/// ⚠️ 识别口径的两条硬约束（都是实测得出的）：
/// ① 浏览器发布的 AppId **与进程名不一致** —— Edge 给的是 <c>MSEdge</c>（既不是 msedge.exe，也不带 .exe），
///    Chrome 是 <c>chrome</c>。所以按 AppId 的**首段前缀**匹配，不按可执行文件名。
/// ② 不能按"播放类型"挑视频：实测一个 45 分钟的比赛回放，SMTC 的 PlaybackType 依然是 <c>Music</c>
///    —— 这个字段由网页自己填，与内容是视频还是音频无关。因此只要浏览器在放，就按媒体同步
///    （是否启用由设置里的总开关决定）。
/// </para>
/// <para>
/// ⚠️ 必须全程 async：把 SMTC 的异步调用同步化会阻塞线程池线程，而主循环节拍依赖线程池。
/// </para>
/// </summary>
internal sealed class BrowserMedia : IMusicPlayer
{
    /// <summary>
    /// AppId 前缀白名单（取 AppId 的第一段，大小写不敏感）。
    /// 刻意精确到首段，避免 "." 前缀匹配到 EdgeDriver / EdgeUpdate 之类非浏览器的 Edge 组件。
    /// </summary>
    private static readonly string[] BrowserAppIdPrefixes =
    [
        "MSEdge",     // Microsoft Edge（实测值）
        "chrome",     // Google Chrome
        "brave",      // Brave
        "vivaldi",    // Vivaldi
        "opera",      // Opera / Opera GX
        "firefox",    // Mozilla Firefox
        "librewolf",
        "waterfox"
    ];

    private static readonly TimeSpan ManagerRetryDelay = TimeSpan.FromSeconds(30);

    private Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager? _manager;
    private DateTime _managerFailedAtUtc = DateTime.MinValue;
    private string? _lastIdentity;

    public async Task<PlayerInfo?> GetPlayerInfoAsync()
    {
        // 管理器失败后先退避，别每轮都撞同一个失败点
        if (_manager == null && _managerFailedAtUtc != DateTime.MinValue &&
            DateTime.UtcNow - _managerFailedAtUtc < ManagerRetryDelay)
        {
            return null;
        }
        try
        {
            _manager ??= await Smtc.RequestManagerAsync().ConfigureAwait(false);
            if (_manager == null)
            {
                _managerFailedAtUtc = DateTime.UtcNow;
                return null;
            }
            var sessions = await Smtc.DescribeAsync(_manager).ConfigureAwait(false);
            var index = PickBrowserSession(sessions);
            if (index < 0) return null;

            var snapshot = sessions[index];
            var identity = $"{snapshot.AppId}|{snapshot.Title}";
            if (_lastIdentity != identity) _lastIdentity = identity;

            byte[]? cover = null;
            try
            {
                var raw = _manager.GetSessions();
                if (index < raw.Count) cover = await Smtc.TryReadThumbnailAsync(raw[index]).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Browser] 读取封面失败: {ex.Message}");
            }

            return BuildInfo(snapshot, identity, cover);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Browser] 读取会话失败: {ex.Message}");
            _manager = null;
            _managerFailedAtUtc = DateTime.UtcNow;
            return null;
        }
    }

    /// <summary>
    /// 从全部会话里挑出"浏览器在放的那个"（纯函数，便于单测）。
    /// 只认白名单里的浏览器；多个浏览器同时放时取第一个正在播放的，都在暂停则取第一个有标题的。
    /// </summary>
    internal static int PickBrowserSession(System.Collections.Generic.IReadOnlyList<Smtc.SessionSnapshot> sessions)
    {
        var paused = -1;
        for (var i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            if (!IsBrowserAppId(session.AppId)) continue;
            if (string.IsNullOrWhiteSpace(session.Title)) continue;   // 没有曲目信息视为未播放
            if (session.IsPlaying) return i;
            if (paused < 0) paused = i;
        }
        return paused;
    }

    /// <summary>AppId 首段是否命中浏览器白名单（大小写不敏感）。</summary>
    internal static bool IsBrowserAppId(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return false;
        var head = appId;
        var dot = head.IndexOf('.');
        if (dot > 0) head = head[..dot];
        foreach (var prefix in BrowserAppIdPrefixes)
        {
            if (head.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// 把会话快照映射成统一模型（纯函数，便于单测）。
    /// 标题**原样使用**、不做清洗：网页给的标题就是它的标题（可能带站名后缀或 #标签）。
    /// 进度为 0 时（网页未上报时间线）不影响同步，进度条会按"可见但不动"显示。
    /// </summary>
    internal static PlayerInfo BuildInfo(Smtc.SessionSnapshot session, string identity, byte[]? cover)
    {
        var duration = session.Duration > 0 ? session.Duration : 0;
        var position = session.Position > 0 ? session.Position : 0;
        if (duration > 0 && position > duration) position = duration;
        return new PlayerInfo
        {
            Identity = identity,
            Title = session.Title.Trim(),
            // 浏览器会话不提供"歌手"：留空，由格式模板决定怎么显示
            Artists = string.IsNullOrWhiteSpace(session.Artist) ? "" : session.Artist!.Trim(),
            Album = string.IsNullOrWhiteSpace(session.Album) ? "" : session.Album!.Trim(),
            Cover = "",
            CoverImage = cover,
            Schedule = position,
            Duration = duration,
            Url = "",
            Pause = !session.IsPlaying
        };
    }

    public void Dispose() => _manager = null;
}
