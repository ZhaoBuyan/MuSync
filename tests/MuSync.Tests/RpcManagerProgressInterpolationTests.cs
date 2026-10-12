using System;
using MuSync;
using MuSync.Models;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 播放进度「对表」推算的测试。
/// <para>
/// 为什么不能直接用源的读数：位置只在源自己上报时才更新（SMTC 类源约 5 秒一报），
/// 直接用会表现成"跳一格"。而用「距上次轮询多久」来推算也不行 —— 轮询每 233ms 一次，
/// 那个值永远是 0。改为按「该位置读数的采样时刻」推算后，两次上报之间也能连续推进。
/// </para>
/// </summary>
public class RpcManagerProgressInterpolationTests
{
    private static readonly DateTime T0 = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    private static PlayerInfo Info(double schedule, double duration, bool pause = false) => new()
    {
        Identity = "TTPlayer",
        Title = "幽ノ楽園",
        Artists = "Albemuth",
        Album = "幽ノ楽園",
        Cover = "",
        Schedule = schedule,
        Duration = duration,
        Url = "",
        Pause = pause
    };

    [Fact]
    public void AdvancesByElapsedSinceSample()
    {
        // 读数 100s 是 5 秒前采到的 → 现在应该推算到 105s
        var result = RpcManager.InterpolatePosition(Info(100, 241), T0, T0.AddSeconds(5));

        Assert.Equal(105, result.Schedule, 3);
    }

    [Fact]
    public void AdvanceIsContinuous_NotStepped()
    {
        // 关键回归：连续几次调用必须连续前进（原来恒等于读数，一直不动）
        var info = Info(100, 241);
        var previous = 0.0;
        for (var half = 0; half <= 10; half++)
        {
            var now = T0.AddSeconds(half * 0.5);
            var current = RpcManager.InterpolatePosition(info, T0, now).Schedule;
            Assert.True(current > previous || half == 0, $"第 {half} 次没有前进：{previous} -> {current}");
            previous = current;
        }
        Assert.Equal(105, previous, 3);
    }

    [Fact]
    public void ClampsAtDuration()
    {
        var result = RpcManager.InterpolatePosition(Info(238, 241), T0, T0.AddSeconds(30));

        Assert.Equal(241, result.Schedule, 3);
    }

    [Fact]
    public void PausedDoesNotAdvance()
    {
        var result = RpcManager.InterpolatePosition(Info(100, 241, pause: true), T0, T0.AddSeconds(10));

        Assert.Equal(100, result.Schedule, 3);
    }

    [Fact]
    public void UnknownDurationDoesNotAdvance()
    {
        // 时长未知（0）时无法判断边界，保持原样
        var result = RpcManager.InterpolatePosition(Info(100, 0), T0, T0.AddSeconds(10));

        Assert.Equal(100, result.Schedule, 3);
    }

    [Fact]
    public void MissingSampleTimeDoesNotAdvance()
    {
        var result = RpcManager.InterpolatePosition(Info(100, 241), DateTime.MinValue, T0);

        Assert.Equal(100, result.Schedule, 3);
    }

    [Fact]
    public void SampleTimeInFutureDoesNotRewind()
    {
        // 时钟回拨 / 采样时刻晚于现在：不得把进度往回推
        var result = RpcManager.InterpolatePosition(Info(100, 241), T0.AddSeconds(10), T0);

        Assert.Equal(100, result.Schedule, 3);
    }

    [Fact]
    public void PreservesOtherFields()
    {
        var result = RpcManager.InterpolatePosition(Info(100, 241), T0, T0.AddSeconds(1));

        Assert.Equal("幽ノ楽園", result.Title);
        Assert.Equal("Albemuth", result.Artists);
        Assert.Equal("TTPlayer", result.Identity);
        Assert.Equal(241, result.Duration, 3);
        Assert.False(result.Pause);
    }
}
