using System.Collections.Generic;
using MuSync.Models;
using MuSync.Players;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 千千静听（TTPlayer 重制版）SMTC 读取器的纯逻辑测试：
/// 字段映射、进度边界，以及「从多个会话里挑目标会话」的挑选规则。
/// 真正的 WinRT 调用（GlobalSystemMediaTransportControlsSessionManager）无法在单测里伪造，
/// 由实机实测覆盖（见交付说明）。
/// </summary>
public class TTPlayerSmtcTests
{
    [Fact]
    public void BuildInfo_MapsAllFields()
    {
        var info = TTPlayerSmtc.BuildInfo(
            title: "幽ノ楽園", artist: "Albemuth", album: "幽ノ楽園",
            pause: false, position: 15.36, duration: 241.65);

        Assert.Equal("TTPlayer", info.Identity);
        Assert.Equal("幽ノ楽園", info.Title);
        Assert.Equal("Albemuth", info.Artists);
        Assert.Equal("幽ノ楽園", info.Album);
        Assert.Equal(15.36, info.Schedule, 3);
        Assert.Equal(241.65, info.Duration, 3);
        Assert.False(info.Pause);
        Assert.Equal("", info.Cover);   // 封面留空：SMTC 给的是缩略图流，现有封面链路只认 URL
    }

    [Fact]
    public void BuildInfo_TrimsWhitespaceAndNullsMissingFields()
    {
        var info = TTPlayerSmtc.BuildInfo("  Song  ", "  Artist  ", null, pause: true, position: 1, duration: 2);

        Assert.Equal("Song", info.Title);
        Assert.Equal("Artist", info.Artists);
        Assert.Equal("", info.Album);
        Assert.True(info.Pause);
    }

    [Fact]
    public void BuildInfo_EmptyArtistBecomesEmptyString()
    {
        var info = TTPlayerSmtc.BuildInfo("Song", "   ", "", pause: false, position: 0, duration: 0);

        Assert.Equal("", info.Artists);
        Assert.Equal("", info.Album);
    }

    [Fact]
    public void BuildInfo_UnknownDurationStaysZero()
    {
        // SMTC 未知时长时不给可跳转范围，这里保持 0，与其它播放器口径一致
        var info = TTPlayerSmtc.BuildInfo("Song", "A", "B", pause: false, position: 0, duration: 0);

        Assert.Equal(0, info.Duration);
        Assert.Equal(0, info.Schedule);
    }

    [Fact]
    public void BuildInfo_PositionBeyondDurationIsClamped()
    {
        var info = TTPlayerSmtc.BuildInfo("Song", "A", "B", pause: false, position: 999, duration: 100);

        Assert.Equal(100, info.Schedule);
    }

    [Fact]
    public void BuildInfo_NegativeValuesBecomeZero()
    {
        var info = TTPlayerSmtc.BuildInfo("Song", "A", "B", pause: false, position: -5, duration: -1);

        Assert.Equal(0, info.Schedule);
        Assert.Equal(0, info.Duration);
    }

    [Fact]
    public void PickSessionIndex_MatchesAppIdPrefixCaseInsensitively()
    {
        var sessions = new List<TTPlayerSmtc.SmtcSessionSnapshot>
        {
            new("QQMusic.exe", true),
            new("Sonic853.TTPlayerRebuild.EDB4BA0433B1AC19", false)
        };

        Assert.Equal(1, TTPlayerSmtc.PickSessionIndex(sessions, "Sonic853.TTPlayerRebuild"));
    }

    [Fact]
    public void PickSessionIndex_PrefersPlayingSession()
    {
        // 实测出现过两个 TTPlayerRebuild 进程：正在播放的那个才该被采用
        var sessions = new List<TTPlayerSmtc.SmtcSessionSnapshot>
        {
            new("Sonic853.TTPlayerRebuild.AAAA", false),
            new("Sonic853.TTPlayerRebuild.BBBB", true)
        };

        Assert.Equal(1, TTPlayerSmtc.PickSessionIndex(sessions, "Sonic853.TTPlayerRebuild"));
    }

    [Fact]
    public void PickSessionIndex_FallsBackToFirstMatchWhenNonePlaying()
    {
        var sessions = new List<TTPlayerSmtc.SmtcSessionSnapshot>
        {
            new("QQMusic.exe", true),
            new("Sonic853.TTPlayerRebuild.AAAA", false),
            new("Sonic853.TTPlayerRebuild.BBBB", false)
        };

        Assert.Equal(1, TTPlayerSmtc.PickSessionIndex(sessions, "Sonic853.TTPlayerRebuild"));
    }

    [Fact]
    public void PickSessionIndex_IgnoresOtherPlayers()
    {
        // 关键回归：绝不"随便拿第一个会话"，否则会串到 QQ音乐 / 网易云
        var sessions = new List<TTPlayerSmtc.SmtcSessionSnapshot>
        {
            new("QQMusic.exe", true),
            new("cloudmusic.exe", true)
        };

        Assert.Equal(-1, TTPlayerSmtc.PickSessionIndex(sessions, "Sonic853.TTPlayerRebuild"));
    }

    [Fact]
    public void PickSessionIndex_EmptyListReturnsMinusOne()
    {
        Assert.Equal(-1, TTPlayerSmtc.PickSessionIndex([], "Sonic853.TTPlayerRebuild"));
    }
}
