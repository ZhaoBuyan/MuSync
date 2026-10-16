using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    /// <summary>取会话管理器；失败返回 null（调用方自行决定退避）。</summary>
    public static async Task<GlobalSystemMediaTransportControlsSessionManager?> RequestManagerAsync()
    {
        try
        {
            return await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask()
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SMTC] 取会话管理器失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 把当前所有会话压成纯数据快照，**并顺手把封面缩略图的引用一并取出**。
    /// 读取失败的单个会话跳过，不影响其它。
    /// <para>
    /// ⚠️ 为什么必须"一次读取、同时拿到元数据与缩略图"：
    /// <c>TryGetMediaPropertiesAsync()</c> 在连续快速调用时**会失败或返回空**
    /// （实测：同一会话连读三次，中间那次标题是空的）。早先的实现一次轮询里调了两次
    /// （一次取标题、一次取封面），于是标题随机会变空、会话被判成"没有媒体信息"
    /// → 整个来源时有时无。合并成一次调用根治。
    /// </para>
    /// </summary>
    public static async Task<List<SessionSnapshot>> DescribeAsync(
        GlobalSystemMediaTransportControlsSessionManager manager)
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
            return list;
        }
        foreach (var session in sessions)
        {
            try
            {
                var playing = session.GetPlaybackInfo()?.PlaybackStatus ==
                              GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                var props = await session.TryGetMediaPropertiesAsync().AsTask().ConfigureAwait(false);
                var timeline = session.GetTimelineProperties();
                list.Add(new SessionSnapshot(
                    session.SourceAppUserModelId ?? "",
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
            if (stream.Size == 0 || stream.Size > int.MaxValue) return null;
            var bytes = new byte[stream.Size];
            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size).AsTask().ConfigureAwait(false);
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SMTC] 读取封面缩略图失败: {ex.Message}");
            return null;
        }
    }

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
