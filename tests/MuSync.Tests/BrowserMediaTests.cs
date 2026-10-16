using System;
using System.Collections.Generic;
using MuSync;
using MuSync.Players;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 浏览器媒体读取器的纯逻辑测试：会话识别口径与字段映射。
/// <para>
/// 两条识别口径都是实测得出的，容易想当然写错，所以用测试钉住：
/// ① 浏览器发布的 AppId **不是进程名** —— Edge 给的是 <c>MSEdge</c>（没有 .exe 后缀）。
///    按 "msedge.exe" 匹配会一条都命中不了。
/// ② 不能按播放类型挑视频 —— 实测一个 45 分钟的比赛回放，PlaybackType 依然是 Music。
/// </para>
/// </summary>
public class BrowserMediaTests
{
    private static Smtc.SessionSnapshot Session(
        string appId, string title, bool playing = true, double position = 0, double duration = 0,
        string? artist = null, string? album = null,
        DateTimeOffset lastUpdated = default) =>
        new(appId, playing, title, artist, album, position, duration, null, lastUpdated);

    // ---- 进度必须按采样时刻推算到当下 ----

    [Fact]
    public void BuildInfo_StalePosition_IsExtrapolatedToNow()
    {
        // 实测场景：B 站 9 分钟前上报了 4:18（258 秒），系统读数一直冻在 258。
        // 直接当当前位置用会少显示一大截，必须加上"采样到现在的时长"。
        var sampled = new DateTimeOffset(2026, 10, 16, 16, 37, 45, TimeSpan.FromHours(8));
        var session = Session("MSEdge", "比赛回放", position: 258, duration: 4100, lastUpdated: sampled);

        var info = BrowserMedia.BuildInfo(session, "k", null, sampled.AddMinutes(9));

        Assert.Equal(258 + 540, info.Schedule, 3);
    }

    [Fact]
    public void BuildInfo_FreshPosition_IsNotShifted()
    {
        var sampled = new DateTimeOffset(2026, 10, 16, 16, 37, 45, TimeSpan.FromHours(8));
        var session = Session("MSEdge", "比赛回放", position: 258, duration: 4100, lastUpdated: sampled);

        var info = BrowserMedia.BuildInfo(session, "k", null, sampled);

        Assert.Equal(258, info.Schedule, 3);
    }

    [Fact]
    public void BuildInfo_Paused_DoesNotExtrapolate()
    {
        // 暂停时位置不该继续往前走
        var sampled = new DateTimeOffset(2026, 10, 16, 16, 37, 45, TimeSpan.FromHours(8));
        var session = Session("MSEdge", "比赛回放", playing: false, position: 258, duration: 4100, lastUpdated: sampled);

        var info = BrowserMedia.BuildInfo(session, "k", null, sampled.AddMinutes(9));

        Assert.Equal(258, info.Schedule, 3);
        Assert.True(info.Pause);
    }

    [Fact]
    public void BuildInfo_MissingSampleTime_KeepsRawPosition()
    {
        // 部分源不给采样时刻：退回原始读数，不能凭空加时间
        var session = Session("MSEdge", "比赛回放", position: 258, duration: 4100);

        var info = BrowserMedia.BuildInfo(session, "k", null,
            new DateTimeOffset(2026, 10, 16, 17, 0, 0, TimeSpan.FromHours(8)));

        Assert.Equal(258, info.Schedule, 3);
    }

    [Fact]
    public void BuildInfo_PositionBeyondDuration_DurationIsTreatedAsUnknown()
    {
        // 实测场景：B 站换到长视频后，总长仍是上一个视频的 8:20，而位置已经 47:22。
        // 此时**总长不可信**，应置为未知（0 → 不显示进度条），
        // 而不是用不可信的总长去砍可信的位置（那会让进度永远停在满格）。
        var session = Session("MSEdge", "比赛回放", position: 2842, duration: 500);

        var info = BrowserMedia.BuildInfo(session, "k", null);

        Assert.Equal(2842, info.Schedule, 3);
        Assert.Equal(0, info.Duration, 3);
    }

    [Fact]
    public void BuildInfo_Extrapolation_ProducesPositionBeyondStaleDuration_AndClearsIt()
    {
        // 源长时间不上报时推算会越界：位置确实可以超过那个陈旧的总长，此时同样按"总长未知"处理
        var sampled = new DateTimeOffset(2026, 10, 16, 16, 0, 0, TimeSpan.FromHours(8));
        var session = Session("MSEdge", "比赛回放", position: 100, duration: 120, lastUpdated: sampled);

        var info = BrowserMedia.BuildInfo(session, "k", null, sampled.AddMinutes(10));

        Assert.Equal(700, info.Schedule, 3);   // 100 + 600 秒
        Assert.Equal(0, info.Duration, 3);
    }

    // ---- AppId 识别 ----

    [Theory]
    [InlineData("MSEdge")]                 // 实测值：Edge 给的就是这个
    [InlineData("msedge")]
    [InlineData("chrome")]
    [InlineData("Chrome")]
    [InlineData("brave")]
    [InlineData("vivaldi")]
    [InlineData("opera")]
    [InlineData("firefox")]
    [InlineData("librewolf")]
    [InlineData("waterfox")]
    public void IsBrowserAppId_RecognizesBrowsers(string appId)
    {
        Assert.True(BrowserMedia.IsBrowserAppId(appId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("QQMusic.exe")]
    [InlineData("cloudmusic.exe")]
    [InlineData("Sonic853.TTPlayerRebuild.EDB4BA0433B1AC19")]
    [InlineData("EdgeDriver")]        // 首段不相等，不能被当成浏览器
    [InlineData("EdgeUpdate")]
    public void IsBrowserAppId_RejectsNonBrowsers(string appId)
    {
        Assert.False(BrowserMedia.IsBrowserAppId(appId));
    }

    // ---- 会话挑选 ----

    [Fact]
    public void PickBrowserSession_ChoosesTheBrowser_AmongOtherPlayers()
    {
        var sessions = new List<Smtc.SessionSnapshot>
        {
            Session("QQMusic.exe", "某首歌", playing: false),
            Session("MSEdge", "比赛回放", playing: true),
            Session("Sonic853.TTPlayerRebuild.AAAA", "某首歌", playing: false)
        };

        Assert.Equal(1, BrowserMedia.PickBrowserSession(sessions));
    }

    [Fact]
    public void PickBrowserSession_PrefersPlayingBrowser()
    {
        // 两个浏览器都开着：取正在播放的那个
        var sessions = new List<Smtc.SessionSnapshot>
        {
            Session("chrome", "暂停的页面", playing: false),
            Session("MSEdge", "在放的视频", playing: true)
        };

        Assert.Equal(1, BrowserMedia.PickBrowserSession(sessions));
    }

    [Fact]
    public void PickBrowserSession_AllPaused_FallsBackToFirstWithTitle()
    {
        var sessions = new List<Smtc.SessionSnapshot>
        {
            Session("MSEdge", "暂停的页面", playing: false)
        };

        Assert.Equal(0, BrowserMedia.PickBrowserSession(sessions));
    }

    [Fact]
    public void PickBrowserSession_IgnoresBrowserSessionWithoutTitle()
    {
        // 浏览器开着但没有媒体信息（常见：页面没在放东西）
        var sessions = new List<Smtc.SessionSnapshot>
        {
            Session("MSEdge", "   ", playing: true)
        };

        Assert.Equal(-1, BrowserMedia.PickBrowserSession(sessions));
    }

    [Fact]
    public void PickBrowserSession_NoBrowser_ReturnsMinusOne()
    {
        var sessions = new List<Smtc.SessionSnapshot>
        {
            Session("QQMusic.exe", "某首歌", playing: true)
        };

        Assert.Equal(-1, BrowserMedia.PickBrowserSession(sessions));
    }

    [Fact]
    public void PickBrowserSession_EmptyList_ReturnsMinusOne()
    {
        Assert.Equal(-1, BrowserMedia.PickBrowserSession([]));
    }

    // ---- 字段映射 ----

    [Fact]
    public void BuildInfo_KeepsTitleExactlyAsReported()
    {
        // 标题原样使用、不做清洗：网页给的标题就是它的标题（可能带站名后缀 / #标签）
        const string raw = "【玩机器直播回放】2025.1.18 BLAST赏金赛S1 第五日 #弹幕版";
        var info = BrowserMedia.BuildInfo(Session("MSEdge", raw, position: 1806, duration: 2722), "MSEdge|" + raw, null);

        Assert.Equal(raw, info.Title);
        Assert.Equal("", info.Artists);           // 浏览器会话没有歌手
        Assert.Equal(1806, info.Schedule, 3);
        Assert.Equal(2722, info.Duration, 3);
        Assert.False(info.Pause);
    }

    [Fact]
    public void BuildInfo_NotPlaying_MarksAsPaused()
    {
        var info = BrowserMedia.BuildInfo(Session("MSEdge", "某个视频", playing: false), "k", null);

        Assert.True(info.Pause);
    }

    [Fact]
    public void BuildInfo_PassesThroughCoverBytes()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var info = BrowserMedia.BuildInfo(Session("MSEdge", "某个视频"), "k", bytes);

        Assert.Same(bytes, info.CoverImage);
        Assert.Equal("", info.Cover);             // 字节封面不走 URL
    }

    [Fact]
    public void BuildInfo_UnknownTimeline_StaysZero()
    {
        // 网页未上报时间线时（实测 QQ 音乐也会是这样）不应变成负数或异常值
        var info = BrowserMedia.BuildInfo(Session("MSEdge", "v", position: -5, duration: -1), "k", null);

        Assert.Equal(0, info.Schedule, 3);
        Assert.Equal(0, info.Duration, 3);
    }

    // ---- 开关默认值 ----

    [Fact]
    public void BrowserMediaSync_IsOffByDefault()
    {
        // 默认关闭：浏览器经常挂着后台页面在放，不该自动顶掉当前状态
        Assert.False(new ConfigData().BrowserMediaSyncEnabled);
    }
}
