using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 桥的协议健壮性：畸形请求不能让监听卡住、不能让状态被污染。
/// 这些用例都是"本地任意程序都能发"的输入，必须扛得住。
/// </summary>
public class BrowserExtensionBridgeProtocolTests
{
    private static int FindFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<string> SendRawAsync(int port, string raw)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, port);
        using var stream = client.GetStream();
        var bytes = Encoding.ASCII.GetBytes(raw);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();

        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[4096];
        var read = await stream.ReadAsync(buffer, cts.Token);
        return Encoding.ASCII.GetString(buffer, 0, Math.Max(read, 0));
    }

    [Fact]
    public async Task OversizedBody_IsRejected_AndListenerKeepsWorking()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            // Content-Length 超过 16 KB：应直接 400，而不是读进来
            var raw = "POST /media HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: application/json\r\n" +
                      $"Content-Length: {20 * 1024}\r\nConnection: close\r\n\r\n";
            var response = await SendRawAsync(port, raw);
            Assert.Contains("400", response);

            // 监听仍然可用
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            using var ok = await client.PostAsync("/media", new StringContent(
                """{"title":"超限之后仍可用","playing":true}""", Encoding.UTF8, "application/json"));
            ok.EnsureSuccessStatusCode();
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    [Fact]
    public async Task MissingContentLength_IsRejected()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            var raw = "POST /media HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n{}";
            var response = await SendRawAsync(port, raw);
            Assert.Contains("400", response);
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    [Fact]
    public async Task UnknownPath_Returns404_WithoutTouchingState()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync("/nope");
            Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    [Fact]
    public async Task Status_WorksWithQueryString()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            var json = await client.GetStringAsync("/status?cachebust=1");
            Assert.Contains("listening", json);
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    [Fact]
    public async Task ConcurrentPosts_AllSettle_WithoutHanging()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
            var tasks = new Task[8];
            for (int i = 0; i < tasks.Length; i++)
            {
                var title = $"并发-{i}";
                tasks[i] = Task.Run(async () =>
                {
                    using var response = await client.PostAsync("/media", new StringContent(
                        $$"""{"title":"{{title}}","playing":true}""", Encoding.UTF8, "application/json"));
                    Assert.True(response.IsSuccessStatusCode);
                });
            }
            await Task.WhenAll(tasks);

            var json = await client.GetStringAsync("/status");
            Assert.Contains("并发-", json);
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }
}
