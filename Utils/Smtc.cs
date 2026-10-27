using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MuSync.Utils;

/// <summary>
/// Windows 系统媒体控件（SMTC）访问的共用部分。
/// <para>
/// 使用者：千千静听（TTPlayer 社区重制版）与浏览器媒体（Edge / Chrome 等网页播放的媒体）。
/// 两者都靠系统接口读取会话，区别只在"认哪个 AppId"，所以会话挑选、缩略图读取这些共性放在这里，
/// 各自只保留自己的识别口径（避免两份实现慢慢漂移）。
/// </para>
/// </summary>
internal static class Smtc
{
    /// <summary>
    /// 会话快照：只抽出挑选与展示需要的字段，便于脱离 WinRT 单测。
    /// <paramref name="Thumbnail"/> 是封面缩略图的**引用**（不是字节），
    /// 由调用方在需要时再用 <see cref="TryReadThumbnailAsync"/> 读成字节。
    /// <paramref name="LastUpdatedTime"/> 是该位置读数的**采样时刻**（系统提供）：
    /// 网页播放器可能十几分钟才上报一次位置（实测 B 站），必须靠它把位置推算到"现在"，
    /// 否则显示的进度会比实际播放位置小一大截。
    /// </summary>
    internal readonly record struct SessionSnapshot(
        string AppId, bool IsPlaying, string Title, string? Artist, string? Album, double Position, double Duration,
        IRandomAccessStreamReference? Thumbnail = null, DateTimeOffset LastUpdatedTime = default);

    /// <summary>
    /// 缓存的会话管理器；<see cref="ManagerMaxAge"/> 或显式 <see cref="InvalidateManager"/> 后才会重建。
    /// <para>
    /// ⚠️ 这里曾经是"每次轮询都重新 RequestAsync"，理由是实测发现缓存的会话管理器会假死
    /// （先返回 N 个会话，之后一直返回 0 个，重新请求又正常）。但那个写法有真实代价：
    /// 每次 RequestAsync 都会新建一整套 WinRT/COM 包装（只能等终结器回收）。
    /// 实测一晚上（10.9 小时）堆积 31,995 个 <c>WinRT.ObjectReference</c>，终结器线程常年满载，
    /// 线程池被拖到饥饿并不断注入线程 —— 线程数涨到 1500+，峰值吃 3.8 个核。
    /// </para>
    /// <para>
    /// 现在的折中：**默认复用**，只在两种情况下重建 ——
    /// <list type="number">
    /// <item>调用方给出确凿的"假死"信号（见 <c>BrowserMedia.DescribeAsync</c>：本来有浏览器会话、
    /// 突然连续几轮变成 0 个，且确实有浏览器进程在跑）；</item>
    /// <item>超过 <see cref="ManagerMaxAge"/>（10 分钟）兜底换新，避免长期复用同一个对象。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="forceRefresh">true = 丢弃缓存重建（调用方已判定管理器可能假死）。</param>
    public static async Task<GlobalSystemMediaTransportControlsSessionManager?> RequestManagerAsync(bool forceRefresh = false)
    {
        var cached = Volatile.Read(ref _cachedManager);
        if (!forceRefresh && cached is not null && DateTime.UtcNow - _cachedAtUtc < ManagerMaxAge) return cached;

        await ManagerGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // 双检：等锁期间别的调用可能已经重建过了
            cached = Volatile.Read(ref _cachedManager);
            if (!forceRefresh && cached is not null && DateTime.UtcNow - _cachedAtUtc < ManagerMaxAge) return cached;

            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask()
                .ConfigureAwait(false);
            Volatile.Write(ref _cachedManager, manager);
            _cachedAtUtc = DateTime.UtcNow;
            return manager;
        }
        catch (Exception ex)
        {
            // 失败时清空缓存，让下一次调用重新尝试（调用方另有退避）
            Volatile.Write(ref _cachedManager, null);
            Debug.WriteLine($"[SMTC] 取会话管理器失败: {ex.Message}");
            return null;
        }
        finally
        {
            ManagerGate.Release();
        }
    }

    /// <summary>丢弃缓存的会话管理器：下一次 <see cref="RequestManagerAsync"/> 会重新请求。</summary>
    public static void InvalidateManager() => Volatile.Write(ref _cachedManager, null);

    private static GlobalSystemMediaTransportControlsSessionManager? _cachedManager;
    private static DateTime _cachedAtUtc = DateTime.MinValue;
    private static readonly SemaphoreSlim ManagerGate = new(1, 1);

    /// <summary>缓存管理器的最大存活时间：到期无条件换新，兜底防止长期复用同一个对象。</summary>
    private static readonly TimeSpan ManagerMaxAge = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 单个会话读元数据的超时。
    /// <para>
    /// <c>TryGetMediaPropertiesAsync()</c> 是跨进程调用：会话正在消失、或播放器卡住时，
    /// 实测能挂好几秒，把轮询步骤拖过 3 秒阈值（日志里见过「轮询步骤 Browser 耗时 3.x 秒」），
    /// 极端情况会触发看门狗重启主循环。超时只是跳过这一个会话，下一拍再试。
    /// </para>
    /// </summary>
    private static readonly TimeSpan SessionPropertyTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 把会话压成纯数据快照，**并顺手把封面缩略图的引用一并取出**。
    /// 读取失败的单个会话跳过，不影响其它。
    /// </summary>
    /// <param name="appIdFilter">
    /// 可选过滤器：只对<c>可能命中</c>的会话读取元数据。
    /// <c>SourceAppUserModelId</c> 是同步且廉价的属性，而 <c>TryGetMediaPropertiesAsync()</c> 是跨进程异步调用 ——
    /// 每拍对<b>所有</b>会话都读一遍既浪费，也会让同一个会话在同一拍被读两次
    /// （千千静听与浏览器媒体各自都要读它自己的会话）；先过滤再读可以避开这两点。
    /// </param>
    /// <para>
    /// ⚠️ 为什么必须"一次读取、同时拿到元数据与缩略图"：
    /// <c>TryGetMediaPropertiesAsync()</c> 在连续快速调用时**会失败或返回空**
    /// （实测：同一会话连读三次，中间那次标题是空的）。早先的实现一次轮询里调了两次
    /// （一次取标题、一次取封面），于是标题随机会变空、会话被判成"没有媒体信息"
    /// → 整个来源时有时无。合并成一次调用根治。
    /// </para>
    public static async Task<List<SessionSnapshot>> DescribeAsync(
        GlobalSystemMediaTransportControlsSessionManager manager,
        Func<string, bool>? appIdFilter = null)
    {
        var list = new List<SessionSnapshot>();
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions;
        try
        {
            sessions = manager.GetSessions();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SMTC] 枚举会话失败: {ex.Message}");
            // 枚举失败通常意味着这个管理器已经死了（媒体服务重启 / 会话被系统回收）。
            // 之前只返回空列表，缓存里那个坏管理器会一直用到 10 分钟兜底为止 ——
            // 期间所有来源都看不到会话，表现就是"浏览器媒体时有时无"。
            InvalidateManager();
            return list;
        }
        foreach (var session in sessions)
        {
            try
            {
                var appId = session.SourceAppUserModelId ?? "";
                // 先按 AppId 过滤（同步廉价），命中才付异步读元数据的代价
                if (appIdFilter != null && !appIdFilter(appId)) continue;
                var playing = session.GetPlaybackInfo()?.PlaybackStatus ==
                              GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                var props = await session.TryGetMediaPropertiesAsync().AsTask()
                    .WaitAsync(SessionPropertyTimeout).ConfigureAwait(false);
                var timeline = session.GetTimelineProperties();
                list.Add(new SessionSnapshot(
                    appId,
                    playing,
                    props?.Title ?? "",
                    props?.Artist,
                    props?.AlbumTitle,
                    timeline?.Position.TotalSeconds ?? 0,
                    timeline?.EndTime.TotalSeconds ?? 0,
                    props?.Thumbnail,
                    timeline?.LastUpdatedTime ?? default));
            }
            catch (Exception ex)
            {
                // 单个会话读取失败按"不可用"处理，不影响其它候选
                Debug.WriteLine($"[SMTC] 读取会话失败: {ex.Message}");
            }
        }
        return list;
    }

    /// <summary>
    /// 把**已经取到的**缩略图引用读成图片字节（PNG / JPEG 原样）。
    /// 返回 null 表示没有封面或读取失败。
    /// <para>
    /// 实测：系统给的就是媒体自身的封面 —— 浏览器播视频时是视频海报（实测 150×93 PNG），
    /// 音乐播放器是专辑封面（实测 300×300 JPEG）。它不是网页的 favicon。
    /// </para>
    /// <para>
    /// ⚠️ 参数是 <see cref="SessionSnapshot.Thumbnail"/>，不是会话本身：
    /// 不要再拿会话去 <c>TryGetMediaPropertiesAsync()</c> —— 那正是上面注释里说的重复读取坑。
    /// </para>
    /// </summary>
    public static async Task<byte[]?> TryReadThumbnailAsync(IRandomAccessStreamReference? reference)
    {
        if (reference is null) return null;
        try
        {
            using var stream = await reference.OpenReadAsync().AsTask().ConfigureAwait(false);
            // 上界取 8 MB：封面缩略图远小于此，而只挡 int.MaxValue 的话，
            // 一个异常读数会让程序尝试分配 2 GB（OOM 虽被 catch，但白付一次分配）。
            if (stream.Size == 0 || stream.Size > MaxThumbnailBytes) return null;
            var bytes = new byte[stream.Size];
            using var reader = new DataReader(stream);
            // ⚠️ LoadAsync 允许只交付**少于**请求量的字节，必须循环补齐：
            // 只调一次就 ReadBytes(全长) 会在短读时抛异常 → 被下面的 catch 吞掉 →
            // 表现为"封面时有时无"（与本功能其它注释描述的症状同类，难查）。
            var loaded = 0u;
            while (loaded < stream.Size)
            {
                var got = await reader.LoadAsync((uint)(stream.Size - loaded)).AsTask().ConfigureAwait(false);
                if (got == 0) break;
                loaded += got;
            }
            if (loaded < stream.Size) return null;
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SMTC] 读取封面缩略图失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>封面缩略图允许的最大字节数（见 TryReadThumbnailAsync 的上界说明）。</summary>
    private const long MaxThumbnailBytes = 8L * 1024 * 1024;

    /// <summary>在快照里按 AppId 前缀找出第一个匹配项的下标；没有则 -1（大小写不敏感）。</summary>
    public static int IndexOfApp(IReadOnlyList<SessionSnapshot> sessions, string appIdPrefix)
    {
        for (var i = 0; i < sessions.Count; i++)
        {
            if (sessions[i].AppId.StartsWith(appIdPrefix, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }
}
