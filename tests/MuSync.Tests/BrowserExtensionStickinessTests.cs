using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 多标签并发上报时的"在位者粘性"：两个页面都在播放时，扩展会每秒各报一次，
/// 如果谁后到就听谁的，Steam 状态会每秒来回跳。这里锁定取舍规则。
/// </summary>
public class BrowserExtensionStickinessTests
{
    private static int FindFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static StringContent Body(string title, string site, bool playing) =>
        new($$"""{"title":"{{title}}","site":"{{site}}","playing":{{(playing ? "true" : "false")}},"position":10,"duration":100}""",
            Encoding.UTF8, "application/json");

    private static async Task<string?> StatusTitleAsync(int port)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var json = await client.GetStringAsync($"http://127.0.0.1:{port}/status");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").ValueKind == System.Text.Json.JsonValueKind.Null
            ? null
            : doc.RootElement.GetProperty("data").GetProperty("title").GetString();
    }

    [Fact]
    public async Task BothPlaying_KeepsIncumbent_ToAvoidFlipFlop()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var first = await client.PostAsync($"http://127.0.0.1:{port}/media", Body("标签A", "a.example.com", true));
            Assert.True(first.IsSuccessStatusCode);

            // 另一个页面也在播：不应该把 A 顶掉（否则每秒来回跳）
            using var second = await client.PostAsync($"http://127.0.0.1:{port}/media", Body("标签B", "b.example.com", true));
            Assert.True(second.IsSuccessStatusCode);

            Assert.Equal("标签A", await StatusTitleAsync(port));
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    [Fact]
    public async Task NewTabPlaying_TakesOver_WhenIncumbentPaused()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var paused = await client.PostAsync($"http://127.0.0.1:{port}/media", Body("标签A", "a.example.com", false));
            Assert.True(paused.IsSuccessStatusCode);

            // 用户切到另一个页面开始播：应该接管
            using var playing = await client.PostAsync($"http://127.0.0.1:{port}/media", Body("标签B", "b.example.com", true));
            Assert.True(playing.IsSuccessStatusCode);

            Assert.Equal("标签B", await StatusTitleAsync(port));
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    [Fact]
    public async Task SamePage_AlwaysUpdates()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var first = await client.PostAsync($"http://127.0.0.1:{port}/media", Body("同一页面", "a.example.com", true));
            Assert.True(first.IsSuccessStatusCode);
            using var second = await client.PostAsync($"http://127.0.0.1:{port}/media", Body("同一页面", "a.example.com", true));
            Assert.True(second.IsSuccessStatusCode);

            // 同一页面必须持续更新（进度才会走），不能被粘性规则挡掉
            Assert.Equal("同一页面", await StatusTitleAsync(port));
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }
}
