using System;
using MuSync;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>卡死兜底 · 看门狗测试：停摆判定（纯函数）与稳定性参数口径。</summary>
public class WatchdogTests
{
    private static readonly TimeSpan Threshold = StabilityConfig.LoopStallThreshold;

    [Fact]
    public void Stall_NotStarted_NeverRestarts()
    {
        Assert.False(RpcManager.ShouldRestartLoop(loopStarted: false, TimeSpan.FromMinutes(10), Threshold));
    }

    [Fact]
    public void Stall_FreshTick_DoesNotRestart()
    {
        Assert.False(RpcManager.ShouldRestartLoop(true, TimeSpan.FromSeconds(3), Threshold));
    }

    [Fact]
    public void Stall_DeepIdleCycle_DoesNotRestart()
    {
        // 深度空闲一拍最长 3 秒（见 ComputePollDelay），远小于阈值，不许误判成停摆
        Assert.False(RpcManager.ShouldRestartLoop(true, TimeSpan.FromSeconds(5), Threshold));
    }

    [Fact]
    public void Stall_ExactlyAtThreshold_DoesNotRestart()
    {
        Assert.False(RpcManager.ShouldRestartLoop(true, Threshold, Threshold));
    }

    [Fact]
    public void Stall_BeyondThreshold_Restarts()
    {
        Assert.True(RpcManager.ShouldRestartLoop(true, Threshold + TimeSpan.FromSeconds(1), Threshold));
    }

    [Fact]
    public void Stall_LikeReportedDump_Restarts()
    {
        // 复现 2026-09-21 转储里的现场：最后一拍停在 4 分 16 秒前 → 必须判定为停摆
        Assert.True(RpcManager.ShouldRestartLoop(true, TimeSpan.FromSeconds(256), Threshold));
    }

    [Fact]
    public void Stall_ClockWentBackwards_DoesNotRestart()
    {
        Assert.False(RpcManager.ShouldRestartLoop(true, TimeSpan.FromSeconds(-5), Threshold));
    }

    [Fact]
    public void StallThreshold_MatchesSpec()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), StabilityConfig.LoopStallThreshold);
        Assert.Equal(TimeSpan.FromSeconds(5), StabilityConfig.WatchdogPollInterval);
    }

    [Fact]
    public void AwaitTimeouts_AreBounded()
    {
        // 主循环里每一个 await 都必须有超时上限（否则又一次「无声停摆」）
        Assert.InRange(StabilityConfig.PlayerPollTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
        Assert.InRange(StabilityConfig.StatusPushTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
        Assert.InRange(StabilityConfig.ImageDownloadTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));
        // 看门狗节奏必须比停摆阈值快，且心跳日志不能比看门狗轮询还密
        Assert.True(StabilityConfig.WatchdogPollInterval < StabilityConfig.LoopStallThreshold);
        Assert.True(StabilityConfig.HeartbeatLogInterval >= StabilityConfig.WatchdogPollInterval);
    }
}
