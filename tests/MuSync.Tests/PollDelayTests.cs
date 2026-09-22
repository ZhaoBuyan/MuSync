using System;
using MuSync;
using Xunit;

namespace MuSync.Tests;

/// <summary>性能优化口径测试：主循环降频档位（有源 / 普通空闲 / 深度空闲）与界面每秒刷新开关。</summary>
public class PollDelayTests
{
    private static readonly TimeSpan Active = TimeSpan.FromMilliseconds(233);
    private static readonly TimeSpan Idle = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan DeepIdle = TimeSpan.FromSeconds(3);

    [Fact]
    public void PollDelay_PlayingPlayer_StaysAtFullSpeed()
    {
        Assert.Equal(Active, RpcManager.ComputePollDelay(anyPlayerActive: true, hasAppSource: false, TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void PollDelay_NoSource_ShortIdle_UsesNormalIdle()
    {
        Assert.Equal(Idle, RpcManager.ComputePollDelay(anyPlayerActive: false, hasAppSource: false, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void PollDelay_NoSource_DeepIdle_UsesThreeSeconds()
    {
        Assert.Equal(DeepIdle, RpcManager.ComputePollDelay(anyPlayerActive: false, hasAppSource: false, TimeSpan.FromMinutes(5)));
        Assert.Equal(DeepIdle, RpcManager.ComputePollDelay(anyPlayerActive: false, hasAppSource: false, TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void PollDelay_AppSourceActive_NeverDeepIdles()
    {
        // 有程序在同步（人在电脑前）时保持普通空闲，程序切换的响应不被拖慢
        Assert.Equal(Idle, RpcManager.ComputePollDelay(anyPlayerActive: false, hasAppSource: true, TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void UiRefresh_VisibleAndNormal_Runs()
    {
        Assert.True(MainForm.ShouldRunUiRefresh(visible: true, minimized: false));
    }

    [Fact]
    public void UiRefresh_Minimized_Stops()
    {
        Assert.False(MainForm.ShouldRunUiRefresh(visible: true, minimized: true));
    }

    [Fact]
    public void UiRefresh_HiddenToTray_Stops()
    {
        Assert.False(MainForm.ShouldRunUiRefresh(visible: false, minimized: false));
    }
}
