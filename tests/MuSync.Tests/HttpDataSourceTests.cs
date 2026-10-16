using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using MuSync;
using MuSync.Models;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 自定义 HTTP 数据源的测试：JSON 取值表达式、数字/文本两种值、显示格式、失败降级。
/// <para>
/// 背景：数据源的价值全在"配错了也能看出来 + 取不到不影响其它同步"。
/// 所以这里重点钉住"取值"与"降级"，而不是网络本身。
/// </para>
/// </summary>
public class HttpDataSourceTests
{
    // ---- 取值表达式 ----

    [Fact]
    public void ExtractValue_HeartRateStylePayload_ReturnsNumber()
    {
        // 实测过的真实响应
        const string json = """{"heart_rate":92,"connected":true,"status":"Connected","timestamp":1792171944275,"speed":null}""";

        Assert.Equal("92", HttpDataSource.ExtractValue(json, "heart_rate"));
    }

    [Fact]
    public void ExtractValue_NestedPath_IsSupported()
    {
        const string json = """{"data":{"bpm":88,"zone":"aerobic"}}""";

        Assert.Equal("88", HttpDataSource.ExtractValue(json, "data.bpm"));
        Assert.Equal("aerobic", HttpDataSource.ExtractValue(json, "data.zone"));
    }

    [Fact]
    public void ExtractValue_EmptyPath_TakesRootValue()
    {
        // 对端直接返回一个裸数字/字符串时用得上
        Assert.Equal("65", HttpDataSource.ExtractValue("65", ""));
        Assert.Equal("hello", HttpDataSource.ExtractValue("\"hello\"", ""));
    }

    [Fact]
    public void ExtractValue_TextValue_IsReturnedAsIs()
    {
        // 支持文本是刻意的：这样"任意 HTTP 数据塞进状态"都能用（观众数、下载速度、天气…）
        Assert.Equal("小雨", HttpDataSource.ExtractValue("""{"weather":"小雨"}""", "weather"));
    }

    [Fact]
    public void ExtractValue_NumberFormatting_UsesInvariantCulture()
    {
        // 整数不带小数点；小数不因区域设置变成逗号（中文/德语区域下会踩到）
        Assert.Equal("92", HttpDataSource.ExtractValue("""{"v":92.0}""", "v"));
        Assert.Equal("92.5", HttpDataSource.ExtractValue("""{"v":92.5}""", "v"));
    }

    [Theory]
    [InlineData("""{"a":1}""", "missing")]          // 字段不存在
    [InlineData("""{"a":1}""", "a.b")]              // 路径穿到非对象
    [InlineData("""{"a":null}""", "a")]             // null
    [InlineData("""{"a":[1,2]}""", "a")]            // 数组
    [InlineData("""{"a":{"b":1}}""", "a")]          // 对象
    [InlineData("not json", "a")]                   // 根本不是 JSON
    [InlineData("", "a")]
    [InlineData(null, "a")]
    public void ExtractValue_UnusableInputs_ReturnNull(string? json, string path)
    {
        // 一律返回 null（→ 积木展开为空），绝不抛异常
        Assert.Null(HttpDataSource.ExtractValue(json, path));
    }

    [Fact]
    public void ExtractValue_Boolean_IsFormatted()
    {
        Assert.Equal("true", HttpDataSource.ExtractValue("""{"ok":true}""", "ok"));
        Assert.Equal("false", HttpDataSource.ExtractValue("""{"ok":false}""", "ok"));
    }

    // ---- 显示格式 ----

    [Fact]
    public void ApplyDisplayFormat_ReplacesValueToken()
    {
        Assert.Equal("❤92", HttpDataSource.ApplyDisplayFormat("❤{v}", "92"));
        Assert.Equal("[92 bpm]", HttpDataSource.ApplyDisplayFormat("[{v} bpm]", "92"));
    }

    [Fact]
    public void ApplyDisplayFormat_EmptyFormat_ShowsValueOnly()
    {
        Assert.Equal("92", HttpDataSource.ApplyDisplayFormat("", "92"));
        Assert.Equal("92", HttpDataSource.ApplyDisplayFormat(null, "92"));
    }

    // ---- 模板集成：{心率} 这类自定义积木 ----

    private static PlayerInfo Song() => new()
    {
        Identity = "k", Title = "稻香", Artists = "周杰伦", Album = "", Cover = "",
        Schedule = 10, Duration = 100, Url = "", Pause = false
    };

    private static ConfigData Config() => new()
    {
        MusicSyncEnabled = true,
        HideMusicWhenPaused = false,
        MusicFormat = "{song} {心率}",
        ProgressBarLength = 0
    };

    [Fact]
    public void ComposeStatus_CustomBlock_IsExpanded()
    {
        var blocks = new Dictionary<string, string> { ["心率"] = "❤92" };

        var text = SteamStatusManager.GetStatusPreview(Song(), null, Config(), blocks);

        Assert.Contains("❤92", text);
        Assert.Contains("稻香", text);
    }

    [Fact]
    public void ComposeStatus_MissingBlock_ExpandsToNothing()
    {
        // 服务没开 / 取不到值 → 积木展开为空，其余内容照常
        var text = SteamStatusManager.GetStatusPreview(Song(), null, Config(),
            new Dictionary<string, string>());

        Assert.Contains("稻香", text);
        Assert.DoesNotContain("{心率}", text);
        Assert.DoesNotContain("心率", text);
    }

    [Fact]
    public void ComposeStatus_NoResolverAtAll_StillWorks()
    {
        // 完全没有注册数据源（例如格式编辑器预览）：未知积木同样展开为空，不得抛异常
        var text = SteamStatusManager.GetStatusPreview(Song(), null, Config());

        Assert.Contains("稻香", text);
    }

    [Fact]
    public void ComposeStatus_BlockNameMatchingBuiltinToken_KeepsBuiltinMeaning()
    {
        // 内建标记优先：即使用户把数据源命名成 app，也不会把 {app} 抢走
        var blocks = new Dictionary<string, string> { ["app"] = "绝不生效" };
        var config = Config();
        config.MusicFormat = "{song}";

        var text = SteamStatusManager.GetStatusPreview(Song(), "卡拉彼丘", config, blocks);

        Assert.Contains("卡拉彼丘", text);
        Assert.DoesNotContain("绝不生效", text);
    }

    [Fact]
    public void ComposeStatus_TextBlock_WithTemplateInjectedText_IsNotReExpanded()
    {
        // 值里含占位符一样不得被二次展开（单趟扫描的保证）
        var blocks = new Dictionary<string, string> { ["心率"] = "{song}" };
        var config = Config();

        var text = SteamStatusManager.GetStatusPreview(Song(), null, config, blocks);

        Assert.Contains("{song}", text);
    }

    // ---- 真实服务（可选）：只有本机开着心率服务时才有意义 ----

    [Fact]
    public async Task Probe_RealHeartRateService_WhenRunning()
    {
        var source = new DataSourceConfig
        {
            Enabled = true,
            Name = "心率",
            Url = "http://127.0.0.1:9000/heartrate",
            ValuePath = "heart_rate",
            Format = "❤{v}"
        };
        var result = await HttpDataSource.ProbeAsync(source);
        if (!result.Ok)
        {
            // 服务没开是正常情况（该功能默认关闭），不把环境依赖写进断言
            return;
        }
        Assert.NotNull(result.Value);
        Assert.StartsWith("❤", result.Display);
        Assert.All(result.Value!, c => Assert.True(char.IsDigit(c)));
    }

    [Fact]
    public async Task Probe_UnreachableAddress_FailsWithoutThrowing()
    {
        var source = new DataSourceConfig
        {
            Enabled = true,
            Name = "无效",
            Url = "http://127.0.0.1:59999/nope",
            ValuePath = "v"
        };

        var result = await HttpDataSource.ProbeAsync(source);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }
}
