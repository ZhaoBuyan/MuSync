using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using MuSync;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 代码检查报告（2026-10-28）里的三条问题的回归测试：
/// 1. 缓存更新包必须重新校验（摘要 + 签名），不能只比大小
/// 2. 前台**暂停**的页面不能抢走后**正在播放**的后台页面（会反复抢占）
/// 3. /status 的 ageSeconds 必须与区域设置无关（逗号小数点会产出非法 JSON）
/// </summary>
public class CodeReviewRegressionTests
{
    // ---------- 1. 缓存更新包校验 ----------

    private static UpdateChecker.UpdateAsset Asset(long size, string digest = "") => new()
    {
        Name = "MuSync.exe",
        Size = size,
        DownloadUrl = "https://example.invalid/MuSync.exe",
        Digest = digest,
    };

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "musync-update-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void CachedPackage_MissingFile_IsNotUsable()
    {
        var dir = NewTempDir();
        try
        {
            var verdict = UpdateDownloader.EvaluateCachedPackage(
                Asset(size: 8), "9.9.9", out _, out _, out _, dir);

            Assert.Equal(UpdateDownloader.CachedPackageVerdict.NotUsable, verdict);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void CachedPackage_SizeMismatch_IsNotUsable()
    {
        var dir = NewTempDir();
        try
        {
            UpdateDownloader.IsAlreadyDownloaded(Asset(size: 8), "9.9.9", out var path, dir);
            File.WriteAllBytes(path, new byte[4]);   // 大小不对

            var verdict = UpdateDownloader.EvaluateCachedPackage(
                Asset(size: 8), "9.9.9", out _, out _, out _, dir);

            Assert.Equal(UpdateDownloader.CachedPackageVerdict.NotUsable, verdict);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void CachedPackage_DigestMismatch_IsRejectedAndDeleted()
    {
        var dir = NewTempDir();
        try
        {
            // 摘要故意给错：内容与声明不符 → 必须拒绝，且删掉这份可疑文件
            var wrongDigest = "sha256:" + new string('a', 64);
            UpdateDownloader.IsAlreadyDownloaded(Asset(size: 8, wrongDigest), "9.9.9", out var path, dir);
            File.WriteAllBytes(path, new byte[8]);

            var verdict = UpdateDownloader.EvaluateCachedPackage(
                Asset(size: 8, wrongDigest), "9.9.9", out var reported, out _, out _, dir);

            Assert.Equal(UpdateDownloader.CachedPackageVerdict.NotUsable, verdict);
            Assert.False(File.Exists(reported));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void CachedPackage_UnsignedButIntact_IsUsable_WithWarning()
    {
        var dir = NewTempDir();
        try
        {
            UpdateDownloader.IsAlreadyDownloaded(Asset(size: 8), "9.9.9", out var path, dir);
            File.WriteAllBytes(path, new byte[8]);   // 未签名的普通文件

            var verdict = UpdateDownloader.EvaluateCachedPackage(
                Asset(size: 8), "9.9.9", out _, out var warning, out _, dir);

            // 关键回归：**重新打开更新窗口**时也必须拿到"未签名"的提示，而不是直接静默进入可安装状态
            Assert.Equal(UpdateDownloader.CachedPackageVerdict.Usable, verdict);
            Assert.False(string.IsNullOrEmpty(warning), "未签名的缓存包必须带出警告文案");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ---------- 2. 前台暂停不能抢走后**正在播放** ----------

    private static int FindFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static StringContent Body(string title, string site, bool playing, bool visible) =>
        new($$"""{"title":"{{title}}","site":"{{site}}","playing":{{(playing ? "true" : "false")}},"visible":{{(visible ? "true" : "false")}},"position":10,"duration":100}""",
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
    public async Task ForegroundPaused_DoesNotStealFrom_BackgroundPlaying_Repeatedly()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            async Task Post(string title, string site, bool playing, bool visible)
            {
                using var response = await client.PostAsync($"http://127.0.0.1:{port}/media", Body(title, site, playing, visible));
                Assert.True(response.IsSuccessStatusCode);
            }

            // 后台在播、前台暂停，来回交替上报：必须一直是后台那个（否则状态每秒抖）
            for (int i = 0; i < 3; i++)
            {
                await Post("后台播放中", "live.example.com", playing: true, visible: false);
                Assert.Equal("后台播放中", await StatusTitleAsync(port));

                await Post("前台已暂停", "video.example.com", playing: false, visible: true);
                Assert.Equal("后台播放中", await StatusTitleAsync(port));
            }
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    [Fact]
    public async Task ForegroundPlaying_StillTakesOver_BackgroundPlaying()
    {
        // 修 P2 时不能把"前台正在播放可以接管后台"这条也弄坏
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var bg = await client.PostAsync($"http://127.0.0.1:{port}/media", Body("后台直播", "live.example.com", true, false));
            using var fg = await client.PostAsync($"http://127.0.0.1:{port}/media", Body("前台视频", "video.example.com", true, true));

            Assert.Equal("前台视频", await StatusTitleAsync(port));
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    // ---------- 3. /status 的 JSON 与区域设置无关 ----------

    [Theory]
    [InlineData("de-DE")]   // 逗号作小数点
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    public async Task StatusJson_IsParseable_UnderAnyCulture(string cultureName)
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var post = await client.PostAsync($"http://127.0.0.1:{port}/media", Body("区域测试", "test.example.com", true, true));
            Assert.True(post.IsSuccessStatusCode);

            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(cultureName);
            var json = await client.GetStringAsync($"http://127.0.0.1:{port}/status");

            // 以前用 {age:F1} 拼字符串，德语下会变成 "ageSeconds":0,0 → JSON 非法
            using var doc = JsonDocument.Parse(json);
            var age = doc.RootElement.GetProperty("data").GetProperty("ageSeconds");
            Assert.Equal(JsonValueKind.Number, age.ValueKind);
            Assert.True(age.GetDouble() >= 0);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }
    // ---------- 1b. 端到端：重新打开更新窗口这条路径 ----------

    /// <summary>构造一个"磁盘上已经有包"的 UpdateInfo（版本用 9.9.9 避免撞真实版本）。</summary>
    private static UpdateChecker.UpdateInfo InfoWithCachedAsset(UpdateChecker.UpdateAsset asset, string version = "9.9.9") =>
        new()
        {
            Tag = "v" + version,
            Version = version,
            Body = "",
            Url = "https://example.invalid/release",
            Assets = [asset],
        };

    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(target);

    [Fact]
    public void ReopenedUpdateWindow_UnsignedCachedPackage_StillWarns()
    {
        var version = "9.9.9";
        var asset = Asset(size: 8);
        UpdateDownloader.IsAlreadyDownloaded(asset, version, out var path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllBytes(path, new byte[8]);   // 未签名、大小正确

            using var form = new UpdateForm("0.0.1", InfoWithCachedAsset(asset, version));

            // 修复前：构造时只比大小就直接 Done，且 _signatureWarning 为空 → 点一下就装，无任何提示
            Assert.Equal("Done", Field(form, "_state")!.ToString());
            Assert.False(string.IsNullOrEmpty((string?)Field(form, "_signatureWarning")),
                "重新打开窗口时也必须带出「未签名」警告");
        }
        finally
        {
            try { File.Delete(path); } catch { /* 清理失败不影响结论 */ }
        }
    }

    [Fact]
    public void ReopenedUpdateWindow_DigestMismatch_DoesNotOfferInstall()
    {
        var version = "9.9.8";
        var wrongDigest = "sha256:" + new string('b', 64);
        var asset = Asset(size: 8, wrongDigest);
        UpdateDownloader.IsAlreadyDownloaded(asset, version, out var path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllBytes(path, new byte[8]);   // 内容与声明的摘要不符

            using var form = new UpdateForm("0.0.1", InfoWithCachedAsset(asset, version));

            // 摘要不符 → 不能被当成"已下载完成"，必须留在待下载状态（点击会重新下载）
            Assert.Equal("Idle", Field(form, "_state")!.ToString());
            Assert.True(string.IsNullOrEmpty((string?)Field(form, "_packagePath")));
        }
        finally
        {
            try { File.Delete(path); } catch { /* 清理失败不影响结论 */ }
        }
    }
}
