using System;
using System.Collections.Generic;
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
    /// <summary>
    /// 最近一次成功读到的信息与时刻。
    /// <para>
    /// 用途：系统媒体控件的元数据读取会**偶发失败**（实测同一会话连读多次，中间某次拿不到标题）。
    /// 若失败就直接返回 null，活跃源会在两三拍内掉回上一个播放器、下一拍又切回来 ——
    /// 表现为界面与 Steam 状态来回跳。因此失败时短暂沿用最近一次成功读数，
    /// 超过 <see cref="StaleFallbackWindow"/> 才真正视为没有媒体在放。
    /// </para>
    /// </summary>
    private PlayerInfo? _lastGoodInfo;
    private DateTime _lastGoodUtc = DateTime.MinValue;
    private static readonly TimeSpan StaleFallbackWindow = TimeSpan.FromSeconds(8);
    /// <summary>会话管理器重建的最小间隔（见 DescribeWithRecoveryAsync 的限频说明）。</summary>
    private static readonly TimeSpan RebuildMinInterval = TimeSpan.FromSeconds(5);
    private DateTime _lastRebuildAttemptUtc = DateTime.MinValue;

    /// <summary>
    /// 取会话快照；为空时**重建会话管理器再取一次**。
    /// <para>
    /// 为什么需要它（实测）：会话管理器对象在被反复快速调用后会失效 ——
    /// 同一个管理器第一次 <c>GetSessions()</c> 正常返回 2 个会话，
    /// 0.3 秒后再调就持续返回 0 个（而重新 RequestAsync 拿到的管理器又是好的）。
    /// 主循环每 233ms 读一次，正好踩中这个失效。
    /// </para>
    /// </summary>
    private async Task<List<Smtc.SessionSnapshot>> DescribeWithRecoveryAsync()
    {
        var sessions = await Smtc.DescribeAsync(_manager!).ConfigureAwait(false);
        if (sessions.Count > 0) return sessions;

        // 会话列表为空：可能确实是没人在放，也可能是管理器失效了 —— 重建一次再判定。
        // ⚠️ 重建必须限频：全速档下本方法每 233ms 走一次，若"确实没人在放"时不限频，
        // 就会变成每秒重建约 4 次会话管理器（白白开销）。这里 5 秒最多重建一次。
        var now = DateTime.UtcNow;
        if (now - _lastRebuildAttemptUtc < RebuildMinInterval) return sessions;
        _lastRebuildAttemptUtc = now;

        _manager = await Smtc.RequestManagerAsync().ConfigureAwait(false);
        if (_manager == null)
        {
            _managerFailedAtUtc = DateTime.UtcNow;
            return sessions;
        }
        _managerFailedAtUtc = DateTime.MinValue;
        return await Smtc.DescribeAsync(_manager).ConfigureAwait(false);
    }

    public async Task<PlayerInfo?> GetPlayerInfoAsync()
    {
        // 管理器失败后先退避，别每轮都撞同一个失败点
        if (_manager == null && _managerFailedAtUtc != DateTime.MinValue &&
            DateTime.UtcNow - _managerFailedAtUtc < ManagerRetryDelay)
        {
            return StaleFallback();
        }
        try
        {
            _manager ??= await Smtc.RequestManagerAsync().ConfigureAwait(false);
            if (_manager == null)
            {
                _managerFailedAtUtc = DateTime.UtcNow;
                return StaleFallback();
            }
            var sessions = await DescribeWithRecoveryAsync().ConfigureAwait(false);
            var index = PickBrowserSession(sessions);
            if (index < 0) return StaleFallback();

            var snapshot = sessions[index];
            var identity = $"{snapshot.AppId}|{snapshot.Title}";

            // 封面字节取自快照里**已经拿到**的缩略图引用：
            // 绝不能再拿会话去 TryGetMediaPropertiesAsync() —— 那是导致"来源时有时无"的重复读取坑
            var cover = await Smtc.TryReadThumbnailAsync(snapshot.Thumbnail).ConfigureAwait(false);

            var info = BuildInfo(snapshot, identity, cover);
            _lastGoodInfo = info;
            _lastGoodUtc = DateTime.UtcNow;
            return info;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Browser] 读取会话失败: {ex.Message}");
            _manager = null;
            _managerFailedAtUtc = DateTime.UtcNow;
            return StaleFallback();
        }
    }

    /// <summary>
    /// 读取失败时的兜底：沿用最近一次成功读数（短期），避免活跃源在两三拍内来回切换。
    /// 超过窗口则返回 null（真的没有媒体在放了）。
    /// </summary>
    private PlayerInfo? StaleFallback()
    {
        if (_lastGoodInfo is { } info && DateTime.UtcNow - _lastGoodUtc <= StaleFallbackWindow)
        {
            return info;
        }
        return null;
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
    /// <para>
    /// ⚠️ 进度必须按"采样时刻"推算到当下：网页播放器可能十几分钟才向系统上报一次位置
    /// （实测 B 站，<c>LastUpdatedTime</c> 停了 9 分钟），直接把上报值当当前位置用，
    /// 显示的进度会比实际播放位置小一大截。真实位置 = 上报位置 + (现在 − 采样时刻)。
    /// </para>
    /// </summary>
    /// <param name="now">当前时刻；默认取系统时间，单测可传入固定值。</param>
    internal static PlayerInfo BuildInfo(
        Smtc.SessionSnapshot session, string identity, byte[]? cover, DateTimeOffset? now = null)
    {
        var duration = session.Duration > 0 ? session.Duration : 0;
        var position = session.Position > 0 ? session.Position : 0;

        // 把位置从"采样时刻"推算到当下。采样时刻缺失时（部分源不给）退回原始读数，
        // 由主循环的通用推算逻辑接管，不会比原来更差。
        if (session.IsPlaying && session.LastUpdatedTime != default)
        {
            var elapsed = (now ?? DateTimeOffset.Now) - session.LastUpdatedTime;
            if (elapsed > TimeSpan.Zero) position += elapsed.TotalSeconds;
        }

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
