using System;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 重连监督策略的守卫。
/// <para>
/// 起因（2026-11-04 实测）：Steam 连不上时重连循环在切到 WebSocket 后静默死亡 ——
/// 日志停在"尝试自动重连"，进程零连接、状态不再推送，界面永远"正在连接"。
/// 修复靠一个不信任循环自身健康的监督者，这里钉住它的判据。
/// </para>
/// </summary>
public class ReconnectPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 17, 40, 0, DateTimeKind.Utc);

    [Fact]
    public void HealthyState_NeedsNoSupervision()
    {
        // 已连接 / 已停止 / 正在退出，都不该插手
        Assert.False(ReconnectPolicy.ShouldSupervise(Now, Now, isRunning: true, isConnected: true, loopRunning: false, cancelled: false));
        Assert.False(ReconnectPolicy.ShouldSupervise(Now, Now, isRunning: false, isConnected: false, loopRunning: false, cancelled: false));
        Assert.False(ReconnectPolicy.ShouldSupervise(Now, Now, isRunning: true, isConnected: false, loopRunning: false, cancelled: true));
    }

    [Fact]
    public void NoLoopRunning_StartsOne()
    {
        // 断线且没有任何循环在跑（循环已死/从未启动）→ 拉起
        Assert.True(ReconnectPolicy.ShouldSupervise(Now, Now, isRunning: true, isConnected: false, loopRunning: false, cancelled: false));
        // 活动时间戳从未设置也视为需要拉起
        Assert.True(ReconnectPolicy.ShouldSupervise(Now, DateTime.MinValue, isRunning: true, isConnected: false, loopRunning: true, cancelled: false));
    }

    [Fact]
    public void LoopRunningWithRecentActivity_LeavesItAlone()
    {
        // 循环还在正常退避重试（30 秒前刚活动过）→ 不插手，避免两轮循环互相打架
        Assert.False(ReconnectPolicy.ShouldSupervise(Now, Now.AddSeconds(-30), isRunning: true, isConnected: false, loopRunning: true, cancelled: false));
    }

    [Theory]
    [InlineData(89, false)]
    [InlineData(90, true)]
    [InlineData(300, true)]
    public void LoopRunningWithoutActivity_TreatedAsStalled(int idleSeconds, bool expected)
    {
        Assert.Equal(expected, ReconnectPolicy.ShouldSupervise(Now, Now.AddSeconds(-idleSeconds),
            isRunning: true, isConnected: false, loopRunning: true, cancelled: false));
    }

    [Fact]
    public void IsStalled_OnlyWhenLoopRunning()
    {
        Assert.False(ReconnectPolicy.IsStalled(Now, Now.AddMinutes(-10), loopRunning: false));
        Assert.False(ReconnectPolicy.IsStalled(Now, DateTime.MinValue, loopRunning: true));
        Assert.True(ReconnectPolicy.IsStalled(Now, Now.AddMinutes(-10), loopRunning: true));
        Assert.False(ReconnectPolicy.IsStalled(Now, Now.AddSeconds(-10), loopRunning: true));
    }

    [Fact]
    public void StallThreshold_IsLongEnoughForExponentialBackoff()
    {
        // 退避上限是 60 秒：阈值必须明显大于它，否则正常的慢重连会被误判为卡死
        Assert.True(ReconnectPolicy.StallThresholdSeconds > 60,
            $"StallThresholdSeconds={ReconnectPolicy.StallThresholdSeconds} 必须大于最大退避 60 秒");
    }
}
