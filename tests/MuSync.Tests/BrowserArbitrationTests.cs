using System;
using MuSync;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 浏览器媒体的仲裁规则：**只在“正在播放”时参与**。
/// <para>
/// 用户诉求：听歌时看直播，Steam 状态要跟着直播标题；但直播暂停后不要一直挂着它。
/// 所以浏览器在两轮仲裁里只参加“播放中”那一轮，暂停则让位给音乐播放器。
/// </para>
/// </summary>
public class BrowserArbitrationTests
{
    [Theory]
    [InlineData("Browser", true, true)]     // 浏览器播放中：参与（优先级排第一时压过音乐）
    [InlineData("Browser", false, false)]   // 浏览器暂停：不参与 → 让位给音乐
    [InlineData("Tencent", true, true)]
    [InlineData("Tencent", false, true)]    // 音乐暂停仍参与（是否推送由 HideMusicWhenPaused 决定）
    [InlineData("NetEase", false, true)]
    [InlineData("TTPlayer", false, true)]
    public void BrowserOnlyParticipatesWhilePlaying(string key, bool playingOnly, bool expected)
    {
        Assert.Equal(expected, RpcManager.ParticipatesInPass(key, playingOnly));
    }

    [Fact]
    public void RuleIsCaseInsensitive()
    {
        // 配置里的键来自旧配置文件，大小写不应影响判定
        Assert.False(RpcManager.ParticipatesInPass("browser", playingOnly: false));
        Assert.True(RpcManager.ParticipatesInPass("Browser", playingOnly: true));
    }
}
