using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 共享令牌：配置了就只接受带正确 <c>X-MuSync-Token</c> 的请求。
/// 目的是挡住"本机其它程序伪装成扩展、把 Steam 状态改成任意文本"。
/// </summary>
public class BrowserExtensionTokenTests
{
    private static int FindFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static HttpRequestMessage Media(string title, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/media")
        {
            Content = new StringContent(
                $$"""{"title":"{{title}}","playing":true,"position":5,"duration":100}""",
                Encoding.UTF8, "application/json")
        };
        if (token is not null) request.Headers.TryAddWithoutValidation("X-MuSync-Token", token);
        return request;
    }

    [Fact]
    public async Task WithoutToken_Configured_RejectsRequestMissingHeader()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port, sharedToken: "s3cret");
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.SendAsync(Media("伪造的标题", token: null));
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);

            // 被拒绝的请求不能进状态
            using var status = await client.GetAsync("/status");
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, status.StatusCode);
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port, sharedToken: null);
        }
    }

    [Fact]
    public async Task WithoutToken_Configured_RejectsWrongToken()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port, sharedToken: "s3cret");
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.SendAsync(Media("伪造的标题", token: "wrong"));
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port, sharedToken: null);
        }
    }

    [Fact]
    public async Task WithToken_Configured_AcceptsCorrectToken()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port, sharedToken: "s3cret");
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.SendAsync(Media("正常标题", token: "s3cret"));
            response.EnsureSuccessStatusCode();

            var request = new HttpRequestMessage(HttpMethod.Get, "/status");
            request.Headers.TryAddWithoutValidation("X-MuSync-Token", "s3cret");
            using var status = await client.SendAsync(request);
            status.EnsureSuccessStatusCode();
            var json = await status.Content.ReadAsStringAsync();
            Assert.Contains("正常标题", json);
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port, sharedToken: null);
        }
    }

    [Fact]
    public async Task TokenOptional_WhenNotConfigured_RequestWithoutHeaderStillWorks()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port, sharedToken: "");
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.SendAsync(Media("不校验时的标题", token: null));
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port, sharedToken: null);
        }
    }

    [Fact]
    public async Task CorsPreflight_IsNotBlockedByToken()
    {
        // 预检请求不带自定义头，必须放行，否则浏览器连正式请求都发不出去
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port, sharedToken: "s3cret");
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Options, "/media");
            using var response = await client.SendAsync(request);
            Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port, sharedToken: null);
        }
    }
}
