using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MuSync;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 第二轮代码检查（2026-10-28）的问题回归：
/// R1 下载器缓存分支未统一校验、R2 区域测试没覆盖服务端、
/// #4 数据源过期窗口与刷新间隔脱钩、#5 同站点同标题的两个标签串台、
/// #6 数据源变化不刷新常驻/程序状态、#7 临时服务错误误清令牌。
/// </summary>
public class CodeReviewRound2Tests
{
    // ---------- R1：DownloadAsync 的缓存命中也要过摘要校验 ----------

    [Fact]
    public async Task DownloadAsync_CachedPackageWithWrongDigest_IsNotReused()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "musync-dl-test", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var wrongDigest = "sha256:" + new string('a', 64);
            var asset = new UpdateChecker.UpdateAsset
            {
                Name = "MuSync.exe",
                Size = 8,
                DownloadUrl = "https://example.invalid/MuSync.exe",   // 一定连不上：只验证缓存分支
                Digest = wrongDigest,
            };
            UpdateDownloader.IsAlreadyDownloaded(asset, "9.9.7", out var path, dir);
            System.IO.File.WriteAllBytes(path, new byte[8]);   // 大小对、摘要不对

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await UpdateDownloader.DownloadAsync(asset, "9.9.7", progress: null, cts.Token, dir);

            // 关键：**不能**因为"大小对得上"就把这份摘要不符的缓存当作已完成
            Assert.NotEqual(UpdateDownloader.DownloadStatus.Completed, result.Status);
            Assert.False(System.IO.File.Exists(path), "摘要不符的缓存必须被删除");
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { /* 清理失败不影响结论 */ }
        }
    }

    // ---------- R2：区域设置必须在**生成响应**的那一侧生效 ----------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    public async Task DescribeCurrent_ProducesValidJson_UnderServerSideCulture(string cultureName)
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            // 先放一份快照（区域不限）
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var post = await client.PostAsync(
                $"http://127.0.0.1:{port}/media",
                new StringContent("""{"title":"区域测试","site":"test.example.com","playing":true,"visible":true}""",
                    Encoding.UTF8, "application/json"));
            Assert.True(post.IsSuccessStatusCode);

            // 关键：在**同一线程**上切成目标区域后直接调用响应生成方法
            // （以前只改测试线程、却让监听线程生成响应，等于没测到服务端）
            Thread.CurrentThread.CurrentCulture = new CultureInfo(cultureName);
            var json = BrowserExtensionBridge.DescribeCurrent();

            using var doc = JsonDocument.Parse(json);
            var age = doc.RootElement.GetProperty("data").GetProperty("ageSeconds");
            Assert.Equal(JsonValueKind.Number, age.ValueKind);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    // ---------- #4：数据源过期窗口跟着刷新间隔 ----------

    [Theory]
    [InlineData(1, 11, false)]     // 1 秒刷新：10 秒窗口内仍算新鲜
    [InlineData(60, 11, false)]    // 60 秒刷新：第 11 秒**不能**判过期（以前的 bug）
    [InlineData(60, 71, true)]     // 超过 刷新间隔 + 宽限 才算过期
    [InlineData(3600, 600, false)] // 一小时刷新：10 分钟时仍有效
    [InlineData(3600, 3611, true)]
    public void StalenessWindow_FollowsRefreshInterval(int refreshSeconds, int ageSeconds, bool expectedStale)
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var lastSuccess = now.AddSeconds(-ageSeconds);

        Assert.Equal(expectedStale, HttpDataSource.IsStale(now, lastSuccess, refreshSeconds));
    }

    [Fact]
    public void StalenessWindow_NeverReadsAfterFirstFetch()
    {
        var now = DateTime.UtcNow;
        Assert.False(HttpDataSource.IsStale(now, DateTime.MinValue, refreshSeconds: 1));
    }

    // ---------- #5：同站点同标题的两个标签靠 sourceId 区分 ----------

    private static int FindFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static StringContent Body(string title, string site, bool playing, bool visible, string sourceId) =>
        new($$"""{"title":"{{title}}","site":"{{site}}","sourceId":"{{sourceId}}","playing":{{(playing ? "true" : "false")}},"visible":{{(visible ? "true" : "false")}},"position":10,"duration":100}""",
            Encoding.UTF8, "application/json");

    private static async Task<string?> StatusTitleAsync(int port)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var json = await client.GetStringAsync($"http://127.0.0.1:{port}/status");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").ValueKind == JsonValueKind.Null
            ? null
            : doc.RootElement.GetProperty("data").GetProperty("title").GetString();
    }

    [Fact]
    public async Task SameSiteSameTitle_DifferentTabs_DoNotStealFromEachOther()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            async Task Post(string title, bool playing, bool visible, string sourceId)
            {
                using var response = await client.PostAsync($"http://127.0.0.1:{port}/media", Body(title, "www.bilibili.com", playing, visible, sourceId));
                Assert.True(response.IsSuccessStatusCode);
            }

            // 两个标签打开同一个视频：标题与站点都一样，只有 sourceId 不同。
            // 后台在这个视频的第 5 秒暂停，前台播到第 30 秒 —— 两边持续上报。
            const string title = "同一个视频";
            for (int i = 0; i < 3; i++)
            {
                await Post(title, playing: true, visible: true, sourceId: "tab-foreground");
                await Post(title, playing: false, visible: false, sourceId: "tab-background");

                var json = await client.GetStringAsync($"http://127.0.0.1:{port}/status");
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.GetProperty("data");
                Assert.Equal(title, data.GetProperty("title").GetString());
                Assert.True(data.GetProperty("playing").GetBoolean(),
                    $"第 {i + 1} 轮：后台暂停的那个标签不该顶掉前台正在播放的（同标题串台）");
            }
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    [Fact]
    public async Task SameSource_NewTitle_StillUpdates()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var first = await client.PostAsync($"http://127.0.0.1:{port}/media", Body("第 1 集", "www.bilibili.com", true, true, "tab-x"));
            using var second = await client.PostAsync($"http://127.0.0.1:{port}/media", Body("第 2 集", "www.bilibili.com", true, true, "tab-x"));

            // 同一标签换集：sourceId 不变、标题变了 → 必须更新（不能因为"不是同一页面"把新标题丢掉）
            Assert.Equal("第 2 集", await StatusTitleAsync(port));
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    // ---------- #6：数据源签名能发现变化且与键顺序无关 ----------

    [Fact]
    public void DynamicBlocksSignature_DetectsValueChanges()
    {
        var before = new Dictionary<string, string> { ["心率"] = "70" };
        var after = new Dictionary<string, string> { ["心率"] = "80" };

        Assert.NotEqual(RpcManager.DynamicBlocksSignature(before), RpcManager.DynamicBlocksSignature(after));
    }

    [Fact]
    public void DynamicBlocksSignature_IsOrderInsensitive()
    {
        var a = new Dictionary<string, string> { ["心率"] = "70", ["温度"] = "21" };
        var b = new Dictionary<string, string> { ["温度"] = "21", ["心率"] = "70" };

        Assert.Equal(RpcManager.DynamicBlocksSignature(a), RpcManager.DynamicBlocksSignature(b));
    }

    [Fact]
    public void DynamicBlocksSignature_EmptyIsEmpty()
    {
        Assert.Equal("", RpcManager.DynamicBlocksSignature(new Dictionary<string, string>()));
    }

    // ---------- #7：只有明确的凭据失效才清令牌 ----------

    [Theory]
    [InlineData(SteamKit2.EResult.InvalidPassword, true)]
    [InlineData(SteamKit2.EResult.AccessDenied, true)]
    [InlineData(SteamKit2.EResult.Expired, true)]
    [InlineData(SteamKit2.EResult.Revoked, true)]
    [InlineData(SteamKit2.EResult.AccountDisabled, true)]
    [InlineData(SteamKit2.EResult.ServiceUnavailable, false)]   // 临时：保留令牌
    [InlineData(SteamKit2.EResult.RateLimitExceeded, false)]    // 限流：保留令牌
    [InlineData(SteamKit2.EResult.Timeout, false)]              // 超时：保留令牌
    [InlineData(SteamKit2.EResult.TryAnotherCM, false)]
    [InlineData(SteamKit2.EResult.Fail, false)]
    public void OnlyCredentialFailuresClearToken(SteamKit2.EResult result, bool expected)
    {
        Assert.Equal(expected, SteamSessionManager.IsCredentialFailure(result));
    }

    [Fact]
    public void NullLogOnResult_IsNotCredentialFailure()
    {
        Assert.False(SteamSessionManager.IsCredentialFailure(null));
    }
}
