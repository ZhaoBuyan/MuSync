using System;

namespace MuSync.Utils;

/// <summary>
/// 重连监督策略（纯函数，便于单测）。
/// <para>
/// 起因（2026-11-04 实测）：Steam 反复连不上时，<see cref="RpcManager"/> 侧的重连循环会先切到
/// WebSocket，然后在某一次 <c>Connect()</c> 之后**彻底静默**——日志停在"尝试自动重连"，
/// 进程没有任何到 Steam 的连接，状态也不再推送，而界面只显示"正在连接"。
/// 循环死亡的可能路径有两种：连接调用被阻塞，或循环体里抛出未捕获异常。
/// 所以这里给出一个**不依赖循环自身是否健康**的判据：由外部监督者周期性判断
/// "该不该拉起/重建一轮重连"。
/// </para>
/// </summary>
internal static class ReconnectPolicy
{
    /// <summary>循环在跑但这么久没有任何活动，视为卡死（例如 Connect 阻塞、线程池被占住）。</summary>
    internal const int StallThresholdSeconds = 90;

    /// <summary>
    /// 是否需要监督者介入。
    /// <list type="bullet">
    /// <item>已连接 / 已停止 / 正在退出：不需要</item>
    /// <item>没有循环在跑：需要（正常拉起）</item>
    /// <item>循环在跑但超过 <paramref name="stallThresholdSeconds"/> 没活动：需要（重建）</item>
    /// </list>
    /// </summary>
    internal static bool ShouldSupervise(
        DateTime nowUtc, DateTime lastActivityUtc, bool isRunning, bool isConnected,
        bool loopRunning, bool cancelled, int stallThresholdSeconds = StallThresholdSeconds)
    {
        if (!isRunning || cancelled || isConnected) return false;
        if (!loopRunning) return true;
        if (lastActivityUtc == DateTime.MinValue) return true;
        return (nowUtc - lastActivityUtc).TotalSeconds >= stallThresholdSeconds;
    }

    /// <summary>
    /// 连续连接失败后该切到哪个协议（纯函数，便于单测）。返回 null 表示不切换、继续重试当前协议。
    /// <para>
    /// 以前只有单向的 TCP → WebSocket 回退：一旦切过去，之后即使代理关了、WebSocket 一直连不上，
    /// 也永远不会回头试直连 —— 用户只能重启程序。现在两个方向都通，形成一个来回切换的循环。
    /// </para>
    /// </summary>
    internal static string? NextProtocolFallback(string currentProtocol, bool allowWebSocketFallback) =>
        currentProtocol switch
        {
            "Tcp" when allowWebSocketFallback => "WebSocket",
            "Tcp" => null,                       // 设置里关掉了 WS 回退：只能在 TCP 上继续重试
            "WebSocket" => "Tcp",                // WS 一直失败（例如代理关了）→ 回头试直连
            _ => null
        };

    /// <summary>循环在跑但已卡死（用于决定是否连同客户端一起重建，而不只是再起一轮）。</summary>
    internal static bool IsStalled(
        DateTime nowUtc, DateTime lastActivityUtc, bool loopRunning,
        int stallThresholdSeconds = StallThresholdSeconds)
    {
        if (!loopRunning || lastActivityUtc == DateTime.MinValue) return false;
        return (nowUtc - lastActivityUtc).TotalSeconds >= stallThresholdSeconds;
    }
}
