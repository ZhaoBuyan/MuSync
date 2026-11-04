using System;
using System.Collections.Generic;
using MuSync.Players;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// Spotify（Windows 桌面版）接入的纯逻辑守卫。
/// <para>
/// 本机没有 Spotify 账号，端到端要等有账号的人实测；但**最脆的一环**——AUMID 匹配——
/// 可以用真实字符串在单测里钉死：商店版 AUMID、安装版的裸名与完整路径，以及
/// 不能误匹配的邻居（浏览器、QQ音乐、千千静听、Media Player）。
/// 读取路径与千千静听同构，已由 <c>TTPlayerSmtcTests</c> 覆盖。
/// </para>
/// </summary>
public class SpotifySmtcTests
{
    // ---------- AUMID 匹配 ----------

    [Theory]
    // 商店版：PFN + 应用 ID，后缀哈希随构建变化
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", true)]
    [InlineData("SpotifyAB.SpotifyMusic", true)]
    // 安装版：裸文件名，或完整路径
    [InlineData("Spotify.exe", true)]
    [InlineData(@"C:\Users\someone\AppData\Roaming\Spotify\Spotify.exe", true)]
    // 大小写不该影响
    [InlineData("spotify.exe", true)]
    // —— 不能误匹配的邻居 ——
    [InlineData("MSEdge", false)]
    [InlineData("Chrome", false)]
    [InlineData("QQMusic.exe", false)]
    [InlineData("cloudmusic.exe", false)]
    [InlineData("Sonic853.TTPlayerRebuild.EDB4BA0433B1AC19", false)]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", false)]
    [InlineData("", false)]
    public void IsSpotifyAppId_MatchesOnlySpotify(string appId, bool expected)
    {
        Assert.Equal(expected, SpotifySmtc.IsSpotifyAppId(appId));
    }

    // ---------- 会话挑选 ----------

    private static SpotifySmtc.SmtcSessionSnapshot Snap(string appId, bool playing) => new(appId, playing);

    [Fact]
    public void PickSessionIndex_PrefersPlayingSession()
    {
        var sessions = new List<SpotifySmtc.SmtcSessionSnapshot>
        {
            Snap("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", playing: false),   // 暂停的商店版
            Snap("MSEdge", playing: true),                                          // 别的程序在播
            Snap("Spotify.exe", playing: true)                                      // 安装版在播 → 选它
        };
        Assert.Equal(2, SpotifySmtc.PickSessionIndex(sessions));
    }

    [Fact]
    public void PickSessionIndex_FallsBackToFirstMatchWhenNonePlaying()
    {
        var sessions = new List<SpotifySmtc.SmtcSessionSnapshot>
        {
            Snap("MSEdge", playing: false),
            Snap(@"C:\Program Files\WindowsApps\Spotify\Spotify.exe", playing: false)
        };
        Assert.Equal(1, SpotifySmtc.PickSessionIndex(sessions));
    }

    [Fact]
    public void PickSessionIndex_ReturnsMinusOneWhenAbsent()
    {
        var sessions = new List<SpotifySmtc.SmtcSessionSnapshot>
        {
            Snap("MSEdge", playing: true),
            Snap("QQMusic.exe", playing: true)
        };
        Assert.Equal(-1, SpotifySmtc.PickSessionIndex(sessions));
    }

    [Fact]
    public void PickSessionIndex_HandlesEmptyList()
    {
        Assert.Equal(-1, SpotifySmtc.PickSessionIndex(Array.Empty<SpotifySmtc.SmtcSessionSnapshot>()));
    }

    // ---------- 字段映射 ----------

    [Fact]
    public void BuildInfo_MapsFieldsAndIdentity()
    {
        var info = SpotifySmtc.BuildInfo("Song", "Artist", "Album", pause: false, position: 30, duration: 200);
        Assert.Equal("Spotify", info.Identity);
        Assert.Equal("Song", info.Title);
        Assert.Equal("Artist", info.Artists);
        Assert.Equal("Album", info.Album);
        Assert.Equal(30, info.Schedule);
        Assert.Equal(200, info.Duration);
        Assert.False(info.Pause);
    }

    [Fact]
    public void BuildInfo_TrimsAndNormalizesEmptyFields()
    {
        var info = SpotifySmtc.BuildInfo("  Song  ", "  ", null!, pause: true, position: 0, duration: 0);
        Assert.Equal("Song", info.Title);
        Assert.Equal("", info.Artists);
        Assert.Equal("", info.Album);
        Assert.True(info.Pause);
    }

    [Fact]
    public void BuildInfo_ExtrapolatesPositionWhilePlaying()
    {
        var now = DateTimeOffset.Now;
        var info = SpotifySmtc.BuildInfo("Song", "Artist", "Album", pause: false,
            position: 10, duration: 100, lastUpdated: now - TimeSpan.FromSeconds(5), now: now);
        Assert.InRange(info.Schedule, 14.9, 15.1);
    }

    [Fact]
    public void BuildInfo_DoesNotExtrapolateWhilePaused()
    {
        var now = DateTimeOffset.Now;
        var info = SpotifySmtc.BuildInfo("Song", "Artist", "Album", pause: true,
            position: 10, duration: 100, lastUpdated: now - TimeSpan.FromSeconds(5), now: now);
        Assert.Equal(10, info.Schedule);
    }

    [Fact]
    public void BuildInfo_TreatsDurationAsUnknownWhenPositionExceedsIt()
    {
        // 换了曲目但时长还没刷新：位置超过总长时把总长当未知，而不是把位置砍到满格
        var info = SpotifySmtc.BuildInfo("Song", "Artist", "Album", pause: true, position: 240, duration: 200);
        Assert.Equal(0, info.Duration);
        Assert.Equal(240, info.Schedule);
    }

    [Fact]
    public void BuildInfo_ClampsNegativeValues()
    {
        var info = SpotifySmtc.BuildInfo("Song", "Artist", "Album", pause: true, position: -5, duration: -1);
        Assert.Equal(0, info.Schedule);
        Assert.Equal(0, info.Duration);
    }
}
