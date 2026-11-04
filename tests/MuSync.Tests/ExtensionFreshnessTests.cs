using System;
using System.Threading.Tasks;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 暂停数据的新鲜窗口守卫。
/// <para>
/// 起因（2026-11-04 实测）：浏览器把后台标签页的定时器节流到 1 分钟，暂停中的视频只有每分钟一次上报；
/// 桥接原先对播放/暂停都用 5 秒窗口，于是后台暂停的视频有 55/60 秒被视为"没数据"，
/// 状态回落到系统会话里的另一首歌，上报到达后又切回来 —— Steam 状态每分钟抖两下。
/// </para>
/// </summary>
public class ExtensionFreshnessTests
{
    private static readonly TimeSpan Playing = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Paused = TimeSpan.FromSeconds(75);
    private static readonly DateTime Now = new(2026, 11, 4, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(4, true, false)]    // 播放中 4 秒：还新鲜
    [InlineData(6, true, true)]     // 播放中 6 秒：过期（页面关掉要立刻回落）
    [InlineData(6, false, false)]   // 暂停 6 秒：仍新鲜（以前这里会判过期 → 抖动）
    [InlineData(55, false, false)]  // 暂停 55 秒：仍新鲜（后台节流的中间时段）
    [InlineData(74, false, false)]  // 暂停 74 秒：仍新鲜
    [InlineData(76, false, true)]   // 暂停 76 秒：过期（真的没在看了）
    public void FreshnessDependsOnPlaybackState(int ageSeconds, bool isPlaying, bool expectedStale)
    {
        var received = Now.AddSeconds(-ageSeconds);
        var stale = !BrowserExtensionBridge.IsFresh(received, isPlaying, Now, Playing, Paused);
        Assert.Equal(expectedStale, stale);
    }

    private static TimeSpan ProductWindow(string fieldName) =>
        (TimeSpan)typeof(MuSync.Players.BrowserMedia)
            .GetField(fieldName, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(null)!;

    [Fact]
    public void ProductPausedWindowExceedsBackgroundThrottle()
    {
        // 浏览器对后台标签页定时器的节流约 60 秒；产品里的窗口必须大于它，否则每分钟掉一次数据
        var window = ProductWindow("ExtensionFreshnessPaused");
        Assert.True(window > TimeSpan.FromSeconds(60),
            $"BrowserMedia.ExtensionFreshnessPaused={window.TotalSeconds}s，必须大于 60 秒");
    }

    [Fact]
    public void ProductPlayingWindowStaysShort()
    {
        var window = ProductWindow("ExtensionFreshness");
        Assert.InRange(window.TotalSeconds, 1, 10);
    }
    // ---------- "忘了关的暂停标签页"不该长期占着状态 ----------

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task PostAsync(int port, string title, bool playing, bool visible, string sourceId)
    {
        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var body = $$"""{"title":"{{title}}","site":"www.bilibili.com","sourceId":"{{sourceId}}","playing":{{(playing ? "true" : "false")}},"visible":{{(visible ? "true" : "false")}},"position":10,"duration":100}""";
        using var content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync($"http://127.0.0.1:{port}/media", content);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task PausedBackgroundTabWithoutRecentPlayback_IsIgnored()
    {
        var port = FreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            // 从没播过就这样上报：后台 + 暂停 → 不该被当成"此刻在听的内容"
            await PostAsync(port, "很久以前打开的暂停页", playing: false, visible: false, sourceId: "tab-never");
            Assert.Null(BrowserExtensionBridge.TryGetFresh(Playing, Paused));
        }
        finally { BrowserExtensionBridge.Ensure(enabled: false, port: port); }
    }

    [Fact]
    public async Task PausedForegroundTab_IsAccepted()
    {
        var port = FreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            // 用户正在看这个页面、只是暂停了：属于合法内容，不该被过滤
            await PostAsync(port, "前台暂停页", playing: false, visible: true, sourceId: "tab-visible");
            Assert.NotNull(BrowserExtensionBridge.TryGetFresh(Playing, Paused));
        }
        finally { BrowserExtensionBridge.Ensure(enabled: false, port: port); }
    }

    [Fact]
    public async Task PausedAfterPlayback_IsAcceptedWithinHoldWindow()
    {
        var port = FreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            await PostAsync(port, "刚暂停的视频", playing: true, visible: false, sourceId: "tab-played");
            await PostAsync(port, "刚暂停的视频", playing: false, visible: false, sourceId: "tab-played");
            // 播过之后暂停：3 分钟内仍然算数（用户刚关掉页面/切走了）
            Assert.NotNull(BrowserExtensionBridge.TryGetFresh(Playing, Paused));
        }
        finally { BrowserExtensionBridge.Ensure(enabled: false, port: port); }
    }
}