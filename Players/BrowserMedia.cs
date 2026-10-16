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
    /// <summary>上次真正读取会话的时刻（用于按配置间隔节流，其余节拍直接用上次读数）。</summary>
    private DateTime _lastReadUtc = DateTime.MinValue;
    /// <summary>封面字节缓存：身份（来源+标题）不变时复用，避免每拍重复读取与分配。</summary>
    private string? _coverIdentity;
    private byte[]? _cachedCover;

    /// <summary>
    /// 最近一次成功读取所属浏览器的可执行文件路径（供"封面缺失时用浏览器自身图标占位"）。
    /// <para>
    /// ⚠️ 必须记"**正在播放的那个**浏览器"，而不是"第一个在运行的浏览器"：
    /// 浏览器常年挂在后台，按白名单顺序取会张冠李戴（Firefox 在放歌却显示 Edge 图标）。
    /// 静态字段：占位图标在 UI 侧取用，那里拿不到读取器实例。
    /// </para>
    /// </summary>
    internal static string? LastActiveBrowserPath { get; private set; }

    /// <summary>
    /// 把会话的 AppId 映射成该浏览器可执行文件的路径；取不到返回 null。
    /// <para>
    /// ⚠️ 整段包在 try/catch 里：这只是"为了拿封面占位图标"的锦上添花，
    /// **绝不允许**它把媒体会话的读取判成失败。
    /// 实测踩过：取路径时触发 <c>AppResource</c> 的类型初始化异常，
    /// 异常一路冒到 <see cref="GetPlayerInfoAsync"/> 的 catch，于是整个浏览器来源被判成"读不到"，
    /// 状态被其它播放器顶掉 —— 为一个图标搞挂了主功能。
    /// </para>
    /// </summary>
    private static string? ResolveBrowserExecutablePath(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return null;
        try
        {
            // AppId 形如 "MSEdge" / "chrome"，与进程名并不总是一致（Edge 的进程名是 msedge），
            // 所以按白名单逐项比对前缀，命中后取该进程的可执行文件路径
            foreach (var name in BrowserProcessNamesForPath)
            {
                if (appId.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                {
                    var path = AppResource.TryGetProcessPath(name);
                    if (path != null) return path;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"[浏览器媒体] 取占位图标路径失败（不影响媒体读取）: {ex.GetType().Name}: {ex.Message}");
        }
        return null;
    }

    /// <summary>AppId 前缀 → 进程名（用于取可执行文件路径）。</summary>
    private static readonly string[] BrowserProcessNamesForPath =
        ["MSEdge", "msedge", "chrome", "brave", "vivaldi", "opera", "firefox", "librewolf", "waterfox"];

    /// <summary>
    /// 取会话快照。**每次都重新获取会话管理器**，不再长期缓存它。
    /// <para>
    /// ⚠️ 实测教训（两次踩同一个坑）：缓存的会话管理器在被反复调用后会失效 ——
    /// 同一个管理器先返回 2 个会话，之后**持续返回 0 个**，而重新 RequestAsync 拿到的又是好的。
    /// 主循环的节奏（233ms）正好踩中它。
    /// 第一版靠"列表为空就重建 + 限频 5 秒"补救，但**两次重建之间仍会返回空**：
    /// 实测 60 拍里有 27 拍"系统明明是 Playing、读取器却是 null"，
    /// 表现为状态被其它播放器反复顶掉又切回来。
    /// 现在改为每次重新获取管理器，从根上消除失效窗口；配合按配置间隔才真正读取（默认 1 秒），
    /// 开销与原来同一量级（原来每拍都在读会话）。
    /// </para>
    /// </summary>
    private async Task<List<Smtc.SessionSnapshot>> DescribeAsync()
    {
        var manager = await Smtc.RequestManagerAsync().ConfigureAwait(false);
        if (manager == null)
        {
            _managerFailedAtUtc = DateTime.UtcNow;
            return [];
        }
        _managerFailedAtUtc = DateTime.MinValue;
        return await Smtc.DescribeAsync(manager, IsBrowserAppId).ConfigureAwait(false);
    }

    public async Task<PlayerInfo?> GetPlayerInfoAsync()
    {
        // 管理器获取失败后先退避，别每轮都撞同一个失败点
        if (_managerFailedAtUtc != DateTime.MinValue &&
            DateTime.UtcNow - _managerFailedAtUtc < ManagerRetryDelay)
        {
            return StaleFallback();
        }
        try
        {
            // 按配置的刷新间隔才真正读取（默认 1 秒）：与"数据源"页里那条心率源同一节奏，
            // 其余节拍直接用上次读数，避免每 233ms 都做一轮跨进程 SMTC 调用
            var interval = Configurations.Instance.Settings.BrowserMediaRefreshSeconds;
            var now = DateTime.UtcNow;
            if (_lastReadUtc != DateTime.MinValue && now - _lastReadUtc < TimeSpan.FromSeconds(Math.Max(1, interval)))
            {
                return _lastGoodInfo ?? StaleFallback();
            }
            _lastReadUtc = now;

            var sessions = await DescribeAsync().ConfigureAwait(false);
            var index = PickBrowserSession(sessions);
            if (index < 0) return StaleFallback();

            var snapshot = sessions[index];
            var identity = $"{snapshot.AppId}|{snapshot.Title}";

            // 封面字节按"来源+标题"缓存：同一张封面不必每拍都重新 OpenReadAsync + 拷贝一份新数组
            // （全速档下那是每秒 4 次跨进程读取与分配，而界面侧本来就按"标题+字节长度"认为封面没变）
            byte[]? cover;
            if (_coverIdentity == identity)
            {
                cover = _cachedCover;
            }
            else
            {
                cover = await Smtc.TryReadThumbnailAsync(snapshot.Thumbnail).ConfigureAwait(false);
                // ⚠️ 只在**真的读到**时才记缓存：读封面失败的原因很多（元数据偶发读空、短读、超上限），
                // 若把 null 也缓存下来，这一段媒体的封面就再也不重试了 —— 一次瞬时失败 = 整首没封面。
                // 不缓存失败则保持"每拍重试"的自愈能力，成功后才享受零重复读取。
                if (cover != null)
                {
                    _coverIdentity = identity;
                    _cachedCover = cover;
                }
            }

            var info = BuildInfo(snapshot, identity, cover);
            _lastGoodInfo = info;
            _lastGoodUtc = DateTime.UtcNow;
            // 记下"正在播放的这个浏览器"的可执行文件路径：主界面在拿不到封面时
            // 用浏览器自身图标占位，不能按白名单取"第一个在运行的"（会张冠李戴）
            if (LastActiveBrowserPath is null && snapshot.AppId.Length > 0)
            {
                LastActiveBrowserPath = ResolveBrowserExecutablePath(snapshot.AppId);
            }
            return info;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Browser] 读取会话失败: {ex.Message}");
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
        // 兼容"全路径形态"的 AUMID（某些浏览器发布的是可执行文件全路径）：
        // 先取文件名，再取第一个 '.' 之前的部分 —— 否则首个 '.' 落在 xxx.exe 里，
        // 首段会变成整条路径，白名单永远命中不了。
        var slash = head.LastIndexOfAny(['\\', '/']);
        if (slash >= 0 && slash < head.Length - 1) head = head[(slash + 1)..];
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

        // ⚠️ 位置已经超过总长时，说明**总长不可信**（实测：B 站换到长视频后仍报上一个视频的 8:20，
        // 而位置已经 47:22），此时应当把总长当作"未知"（置 0 → 不显示进度条），
        // 绝不能用不可信的总长去砍可信的位置 —— 那会让进度永远停在满格。
        if (duration > 0 && position > duration) duration = 0;
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

    public void Dispose() { }
}
