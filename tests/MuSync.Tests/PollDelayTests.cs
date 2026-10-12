using System;
using MuSync;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 主循环降频档位测试：正在播放（全速）/ 仅暂停（1 秒）/ 普通空闲 / 深度空闲，
/// 以及界面每秒刷新开关。
/// <para>
/// "暂停档"：暂停时播放位置不变，没必要按 233ms 读内存（白费 CPU、
/// 也平白多读播放器内存）。判定必须用"有没有读到正在播放的源"，
/// 而不是"有没有检测到播放器进程"——后者在暂停时同样为真，等于没降频。
/// </para>
/// </summary>
public class PollDelayTests
{
    private static readonly TimeSpan Active = TimeSpan.FromMilliseconds(233);
    private static readonly TimeSpan Paused = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Idle = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan DeepIdle = TimeSpan.FromSeconds(3);

    [Fact]
    public void PollDelay_PlayingPlayer_StaysAtFullSpeed()
    {
        Assert.Equal(Active,
            RpcManager.ComputePollDelay(anyPlayerPlaying: true, pausedOnly: false, hasAppSource: false, TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void PollDelay_PausedPlayer_SlowsDownToPausedTier()
    {
        // 关键：有播放器进程但处于暂停态 → 降到 1 秒，而不是继续全速
        Assert.Equal(Paused,
            RpcManager.ComputePollDelay(anyPlayerPlaying: false, pausedOnly: true, hasAppSource: false, TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void PollDelay_PausedPlayer_NeverFallsIntoDeepIdle()
    {
        // 暂停档与空闲时长无关：暂停 1 小时也不该被当成"没在听歌"
        // （恢复播放要能在 1 秒内被察觉，深空闲 3 秒会把响应拖长）
        Assert.Equal(Paused,
            RpcManager.ComputePollDelay(anyPlayerPlaying: false, pausedOnly: true, hasAppSource: false, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void PollDelay_PausedPlayer_FasterThanIdle()
    {
        // 暂停档必须比普通空闲快：暂停时用户随时可能点继续播放
        Assert.True(Paused < Idle, $"暂停档 {Paused} 应快于空闲档 {Idle}");
        Assert.True(Active < Paused, $"播放档 {Active} 应快于暂停档 {Paused}");
    }

    [Fact]
    public void PollDelay_NoSource_ShortIdle_UsesNormalIdle()
    {
        Assert.Equal(Idle,
            RpcManager.ComputePollDelay(anyPlayerPlaying: false, pausedOnly: false, hasAppSource: false, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void PollDelay_NoSource_DeepIdle_UsesThreeSeconds()
    {
        Assert.Equal(DeepIdle,
            RpcManager.ComputePollDelay(anyPlayerPlaying: false, pausedOnly: false, hasAppSource: false, TimeSpan.FromMinutes(5)));
        Assert.Equal(DeepIdle,
            RpcManager.ComputePollDelay(anyPlayerPlaying: false, pausedOnly: false, hasAppSource: false, TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void PollDelay_AppSourceActive_NeverDeepIdles()
    {
        // 有程序在同步（人在电脑前）时保持普通空闲，程序切换的响应不被拖慢
        Assert.Equal(Idle,
            RpcManager.ComputePollDelay(anyPlayerPlaying: false, pausedOnly: false, hasAppSource: true, TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void PollDelay_AppSourceAndPlaying_PlayingWins()
    {
        // 两个条件同时成立时，播放档优先（用户在听歌，进度要顺）
        Assert.Equal(Active,
            RpcManager.ComputePollDelay(anyPlayerPlaying: true, pausedOnly: false, hasAppSource: true, TimeSpan.FromMinutes(30)));
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
