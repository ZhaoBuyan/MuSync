using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MuSync.Players;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 浏览器扩展桥（本机回环接收器）的测试。
/// <para>
/// 重点不是"能不能收到"（那是 HttpClient 的事），而是三条边界的约定：
/// ① 解析要宽容但要有下限（没有标题等于没有内容）；② 数值要有上界，别让异常值把进度条撑爆；
/// ③ 只有当开关打开时才监听，且**只绑 127.0.0.1**。
/// </para>
/// </summary>
public class BrowserExtensionBridgeTests
{
    private static byte[] Json(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void Parse_ReadsAllFields()
    {
        var snapshot = BrowserExtensionBridge.Parse(Json(
            """{"title":"稻香","artist":"周杰伦","album":"魔杰座","position":12.5,"duration":223,"playing":true,"site":"www.bilibili.com"}"""));

        Assert.NotNull(snapshot);
        Assert.Equal("稻香", snapshot!.Title);
        Assert.Equal("周杰伦", snapshot.Artist);
        Assert.Equal("魔杰座", snapshot.Album);
        Assert.Equal(12.5, snapshot.PositionSeconds, 3);
        Assert.Equal(223, snapshot.DurationSeconds, 3);
        Assert.True(snapshot.IsPlaying);
        Assert.Equal("www.bilibili.com", snapshot.Site);
    }

    [Fact]
    public void Parse_WithoutTitle_IsRejected()
    {
        // 没有标题等于没有内容：宁可当作没数据，也不要把一坨空白推上状态
        Assert.Null(BrowserExtensionBridge.Parse(Json("""{"title":"   ","position":1}""")));
        Assert.Null(BrowserExtensionBridge.Parse(Json("""{"artist":"周杰伦"}""")));
    }

    [Fact]
    public void Parse_InvalidJson_IsRejectedWithoutThrowing()
    {
        Assert.Null(BrowserExtensionBridge.Parse(Json("not json")));
        Assert.Null(BrowserExtensionBridge.Parse(Json("[1,2,3]")));
        Assert.Null(BrowserExtensionBridge.Parse(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Parse_ClampsNumbersAndIgnoresWrongTypes()
    {
        var snapshot = BrowserExtensionBridge.Parse(Json(
            """{"title":"x","position":-5,"duration":1e9,"playing":"yes","artist":123}"""));

        Assert.NotNull(snapshot);
        Assert.Equal(0, snapshot!.PositionSeconds, 3);          // 负数归零
        Assert.Equal(86_400, snapshot.DurationSeconds, 3);      // 上界一天
        Assert.False(snapshot.IsPlaying);                       // 非 bool 不算播放
        Assert.Equal("", snapshot.Artist);                      // 非字符串忽略
    }

    [Fact]
    public void Parse_TruncatesOverlongTitle()
    {
        var longTitle = new string('长', 2000);
        var snapshot = BrowserExtensionBridge.Parse(Json($$"""{"title":"{{longTitle}}"}"""));

        Assert.NotNull(snapshot);
        Assert.Equal(512, snapshot!.Title.Length);
    }

    [Fact]
    public void BuildInfoFromExtension_MapsFieldsAndRejectsUntrustworthyDuration()
    {
        var snapshot = new BrowserExtensionSnapshot(
            Title: "视频", Artist: "UP 主", Album: "", PositionSeconds: 100, DurationSeconds: 60,
            IsPlaying: true, Site: "www.bilibili.com", ReceivedUtc: DateTime.UtcNow);

        var info = BrowserMedia.BuildInfoFromExtension(snapshot);

        Assert.Equal("视频", info.Title);
        Assert.Equal("UP 主", info.Artists);
        Assert.Equal(100, info.Schedule, 3);
        // 位置已超过总长 → 总长不可信 → 置 0（不显示进度条），与 SMTC 路径同一规则
        Assert.Equal(0, info.Duration, 3);
        Assert.False(info.Pause);
        Assert.Contains("bilibili", info.Identity);
    }

    [Fact]
    public void BuildInfoFromExtension_PausedMapsToPause()
    {
        var snapshot = new BrowserExtensionSnapshot(
            "视频", "", "", 5, 10, IsPlaying: false, Site: "x", ReceivedUtc: DateTime.UtcNow);

        Assert.True(BrowserMedia.BuildInfoFromExtension(snapshot).Pause);
    }

    [Fact]
    public void BuildInfoFromExtension_KeepsTrustworthyDuration()
    {
        var snapshot = new BrowserExtensionSnapshot(
            "视频", "", "", 5, 300, IsPlaying: true, Site: "x", ReceivedUtc: DateTime.UtcNow);

        Assert.Equal(300, BrowserMedia.BuildInfoFromExtension(snapshot).Duration, 3);
    }

    [Fact]
    public async Task Bridge_ReceivesPostOnLoopback_AndExposesFreshSnapshot()
    {
        var port = FindFreePort();
        try
        {
            BrowserExtensionBridge.Ensure(enabled: true, port: port);
            Assert.True(BrowserExtensionBridge.IsRunning, BrowserExtensionBridge.LastError);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var body = """{"title":"测试视频","artist":"","album":"","position":3,"duration":30,"playing":true,"site":"example.com"}""";
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"http://127.0.0.1:{port}/media", content);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var fresh = BrowserExtensionBridge.TryGetFresh(TimeSpan.FromSeconds(5));
            Assert.NotNull(fresh);
            Assert.Equal("测试视频", fresh!.Title);
            Assert.Equal(3, fresh.PositionSeconds, 3);

            // /status 是给用户自查用的
            var status = await client.GetStringAsync($"http://127.0.0.1:{port}/status");
            Assert.Contains("测试视频", status);
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    [Fact]
    public async Task Bridge_RejectsBadBody_WithoutBreakingListener()
    {
        var port = FindFreePort();
        try
        {
            BrowserExtensionBridge.Ensure(enabled: true, port: port);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            using var bad = new StringContent("not json", Encoding.UTF8, "application/json");
            using var badResponse = await client.PostAsync($"http://127.0.0.1:{port}/media", bad);
            Assert.Equal(HttpStatusCode.BadRequest, badResponse.StatusCode);

            // 坏请求之后仍然可用
            var good = """{"title":"之后还能用","playing":true}""";
            using var goodContent = new StringContent(good, Encoding.UTF8, "application/json");
            using var goodResponse = await client.PostAsync($"http://127.0.0.1:{port}/media", goodContent);
            Assert.Equal(HttpStatusCode.OK, goodResponse.StatusCode);
            Assert.Equal("之后还能用", BrowserExtensionBridge.TryGetFresh(TimeSpan.FromSeconds(5))!.Title);
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    [Fact]
    public async Task Bridge_StopsListening_WhenDisabled()
    {
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        Assert.True(BrowserExtensionBridge.IsRunning);

        BrowserExtensionBridge.Ensure(enabled: false, port: port);
        Assert.False(BrowserExtensionBridge.IsRunning);
        Assert.Null(BrowserExtensionBridge.TryGetFresh(TimeSpan.FromSeconds(5)));

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        await Assert.ThrowsAnyAsync<Exception>(() => client.GetStringAsync($"http://127.0.0.1:{port}/status"));
    }

    [Fact]
    public void Bridge_RejectsIllegalPort()
    {
        BrowserExtensionBridge.Ensure(enabled: true, port: 0);
        Assert.False(BrowserExtensionBridge.IsRunning);
        Assert.Contains("端口", BrowserExtensionBridge.LastError);
        BrowserExtensionBridge.Ensure(enabled: false, port: 0);
    }

    /// <summary>取一个当前空闲的本机端口（避免测试之间互相占用）。</summary>
    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
    [Fact]
    public async Task BrowserMedia_PrefersExtensionData_WhenEnabledAndFresh()
    {
        var port = FindFreePort();
        var settings = Configurations.Instance.Settings;
        var backupEnabled = settings.BrowserExtensionEnabled;
        var backupPort = settings.BrowserExtensionPort;
        try
        {
            settings.BrowserExtensionEnabled = true;
            settings.BrowserExtensionPort = port;
            BrowserExtensionBridge.Ensure(enabled: true, port: port);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var body = """{"title":"扩展上报的视频","artist":"某UP主","position":42,"duration":100,"playing":true,"site":"www.bilibili.com"}""";
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            await client.PostAsync($"http://127.0.0.1:{port}/media", content);

            using var media = new BrowserMedia();
            var info = await media.GetPlayerInfoAsync();

            Assert.NotNull(info);
            var value = info.Value;   // PlayerInfo 是 record struct，! 不会拆 Nullable
            Assert.Equal("扩展上报的视频", value.Title);
            Assert.Equal("某UP主", value.Artists);
            Assert.Equal(42, value.Schedule, 3);
            Assert.Equal(100, value.Duration, 3);
            Assert.False(value.Pause);
        }
        finally
        {
            settings.BrowserExtensionEnabled = backupEnabled;
            settings.BrowserExtensionPort = backupPort;
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }
    // ---- 设置页接线：控件与那句提示必须在 ----

    [Fact]
    public void SettingsPage_ExposesExtensionControlsAndWinAHint()
    {
        using var form = new SettingsForm();
        var all = new System.Collections.Generic.List<System.Windows.Forms.Control>();
        var stack = new System.Collections.Generic.Stack<System.Windows.Forms.Control>();
        stack.Push(form);
        while (stack.Count > 0)
        {
            var control = stack.Pop();
            all.Add(control);
            foreach (System.Windows.Forms.Control child in control.Controls) stack.Push(child);
        }

        // 扩展现在有独立分组（复选框文案只是"启用"），所以查分组标题
        Assert.Contains(all.OfType<System.Windows.Forms.GroupBox>(), g => g.Text.Contains("浏览器扩展"));
        Assert.Contains(all.OfType<System.Windows.Forms.Button>(), b => b.Text.Contains("获取扩展"));
        // 那句提示是这条路线的"为什么"：用户按 Win+A 没控件时才知道该装扩展
        Assert.Contains(all.OfType<System.Windows.Forms.Label>(), l => l.Text.Contains("Win+A"));
        // 端口输入框存在且有默认值
        Assert.Contains(all.OfType<System.Windows.Forms.TextBox>(), t => t.Text == "47564");
    }
    [Fact]
    public void Parse_ToleratesUtf8Bom()
    {
        // PowerShell 的 Set-Content -Encoding UTF8 会写 BOM；JsonDocument 默认会判非法。
        // 浏览器扩展不会带 BOM，但宽容一点能少一类"明明是合法 JSON 却报 bad json"的困惑。
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var json = Encoding.UTF8.GetBytes("""{"title":"带BOM","playing":true}""");
        var payload = new byte[bom.Length + json.Length];
        bom.CopyTo(payload, 0);
        json.CopyTo(payload, bom.Length);

        var snapshot = BrowserExtensionBridge.Parse(payload);

        Assert.NotNull(snapshot);
        Assert.Equal("带BOM", snapshot!.Title);
    }
}