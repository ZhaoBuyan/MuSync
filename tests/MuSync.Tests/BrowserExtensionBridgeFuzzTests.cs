using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 模糊测试：桥面对任意垃圾字节、JSON 炸弹、非法 UTF-8、静默连接都不能崩、不能卡。
/// 这些输入"本机任意程序"都能发，所以必须扛住。
/// </summary>
public class BrowserExtensionBridgeFuzzTests
{
    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<string> SendRawAsync(int port, byte[] raw, int readTimeoutMs = 5000)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var stream = client.GetStream();
        await stream.WriteAsync(raw);
        await stream.FlushAsync();

        using var cts = new CancellationTokenSource(readTimeoutMs);
        var buffer = new byte[8192];
        try
        {
            var read = await stream.ReadAsync(buffer, cts.Token);
            return Encoding.ASCII.GetString(buffer, 0, Math.Max(read, 0));
        }
        catch (OperationCanceledException)
        {
            return "(timeout)";
        }
    }

    private static HttpContent Json(string body) => new StringContent(body, Encoding.UTF8, "application/json");

    [Fact]
    public async Task RandomGarbageBytes_DoNotCrashOrHang()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            // 注意：随机字节里没有 \r\n\r\n，服务端会一直等请求头直到 10 秒读超时 ——
            // 这是**设计如此**（不能因为对方没发完就提前判错）。这里只要求：
            // 这些连接不会把监听拖死，后续正常请求照常工作。
            var random = new Random(12345);
            for (int i = 0; i < 20; i++)
            {
                var bytes = new byte[256];
                random.NextBytes(bytes);
                await SendRawAsync(port, bytes, readTimeoutMs: 200);
            }

            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            using var ok = await client.PostAsync("/media", Json("""{"title":"垃圾之后仍可用","playing":true}"""));
            ok.EnsureSuccessStatusCode();
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    [Fact]
    public async Task JsonBombDeepNesting_IsRejected_WithoutStackOverflow()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            var deep = new string('[', 200) + new string(']', 200);
            var body = $$"""{"title":{{deep}},"playing":true}""";

            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.PostAsync("/media", Json(body));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            using var ok = await client.PostAsync("/media", Json("""{"title":"深嵌套之后仍可用","playing":true}"""));
            ok.EnsureSuccessStatusCode();
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    [Fact]
    public async Task InvalidUtf8Body_IsRejected()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            // 非法 UTF-8 字节（0xFF 0xFE 不能出现在合法 UTF-8 序列里）
            var body = new byte[] { (byte)'{', (byte)'"', (byte)'t', (byte)'"', (byte)':', 0xFF, 0xFE, (byte)'}' };
            var header = Encoding.ASCII.GetBytes(
                $"POST /media HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            var raw = new byte[header.Length + body.Length];
            header.CopyTo(raw, 0);
            body.CopyTo(raw, header.Length);

            var response = await SendRawAsync(port, raw);
            Assert.Contains("400", response);
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    [Fact]
    public async Task SilentConnections_DoNotBlockOtherRequests()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        var silent = new System.Collections.Generic.List<TcpClient>();
        try
        {
            // 开 8 个连上就不发数据的连接（模拟恶意/半死客户端）
            for (int i = 0; i < 8; i++)
            {
                var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                silent.Add(client);
            }

            // 正常请求必须照常成功（每个连接独立处理 + 10 秒读超时）
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.PostAsync("/media", Json("""{"title":"静默连接不影响","playing":true}"""));
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            foreach (var client in silent)
            {
                try { client.Dispose(); } catch { /* 关闭期竞态忽略 */ }
            }
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    [Fact]
    public async Task HugeTitle_IsTruncatedToDocumentedLimit()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            var huge = new string('长', 5000);
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.PostAsync("/media", Json($$"""{"title":"{{huge}}","playing":true}"""));
            response.EnsureSuccessStatusCode();

            var json = await client.GetStringAsync("/status");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var title = doc.RootElement.GetProperty("data").GetProperty("title").GetString()!;
            Assert.True(title.Length <= 512, $"标题长度 {title.Length} 超出文档声明的上限 512");
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }
}
