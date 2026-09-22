using System;
namespace MuSync.Utils;
/// <summary>
/// 稳定性兜底参数集中配置（2026-09-22 卡死兜底批次）：
/// 节拍等待 / 看门狗阈值 / 心跳间隔，以及各处 await 的超时上限。
/// 背景：2026-09-21 的转储显示主循环会卡在 `Task.Delay` 上（定时器回调整体丢失），
/// 之后主循环每个等待都必须有超时或看门狗兜底。
/// </summary>
internal static class StabilityConfig
{
    /// <summary>看门狗轮询间隔（Thread.Sleep 内核等待，不依赖 .NET 定时器队列）。</summary>
    public static readonly TimeSpan WatchdogPollInterval = TimeSpan.FromSeconds(5);
    /// <summary>主循环停摆判定阈值：超过该时长没有新的心跳即判定停摆并重启循环（自愈）。</summary>
    public static readonly TimeSpan LoopStallThreshold = TimeSpan.FromSeconds(30);
    /// <summary>正常运行时的心跳日志间隔（日志里能看到「最后一拍」是几点，停摆不必再抓转储）。</summary>
    public static readonly TimeSpan HeartbeatLogInterval = TimeSpan.FromMinutes(5);
    /// <summary>单次播放器读取超时（LX Music 走 HTTP，其余为内存读取）。</summary>
    public static readonly TimeSpan PlayerPollTimeout = TimeSpan.FromSeconds(5);
    /// <summary>单次 Steam 状态推送超时。</summary>
    public static readonly TimeSpan StatusPushTimeout = TimeSpan.FromSeconds(5);
    /// <summary>单张封面图片下载总超时（含重试等待）。</summary>
    public static readonly TimeSpan ImageDownloadTimeout = TimeSpan.FromSeconds(20);
}
