using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MuSync;
using MuSync.Models;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>配置兼容性回归：旧版本配置文件（含已废弃字段）必须能正常读取，不得触发"损坏重置"。</summary>
public class ConfigCompatibilityTests
{
    // 使用生产代码的真实选项（回归保护：宽容枚举解析必须始终生效）
    private static JsonSerializerOptions CreateOptions() => ConfigJson.Options;

    [Fact]
    public void ProductionJsonOptions_ReadsRealConfigShape_WithStringEnums()
    {
        // 关键回归：启动时 Load 在类型初始化期间运行，曾拿到未初始化的选项
        // → 默认转换器读不了枚举字符串 → 每次启动“配置解析失败”并重置程序列表。
        // 现在 ConfigJson.Options 惰性初始化，任何时机都可用且宽容。
        const string json = """
            {
              "SyncSpeed": "Standard",
              "Apps": [
                { "ExeName": "browser.exe", "DisplayName": "browser", "Category": "Other", "IsUserConfirmed": false },
                { "ExeName": "QQ.exe", "DisplayName": "QQ", "Category": "Social", "IsUserConfirmed": false }
              ]
            }
            """;
        var config = JsonSerializer.Deserialize<ConfigData>(json, ConfigJson.Options);
        Assert.NotNull(config);
        Assert.Equal(SyncSpeedLevel.Standard, config!.SyncSpeed);
        Assert.Equal(2, config.Apps.Count);
        Assert.Equal(AppCategory.Other, config.Apps[0].Category);
        Assert.Equal(AppCategory.Social, config.Apps[1].Category);
    }

    [Fact]
    public void LegacyConfig_WithRemovedStatusPriorityField_LoadsSuccessfully()
    {
        // 旧版本会写入 "StatusPriority"（新版已废弃）。此前该字段导致整个配置解析失败被重置，
        // 本测试确保它作为未知字段被安全忽略。
        const string legacyJson = """
            {
              "AutoStart": false,
              "CloseToTray": true,
              "ShowArtistName": true,
              "ShowProgressBar": true,
              "SteamUsername": "someone",
              "StatusPriority": "Artist",
              "EnableCustomPrefix": false,
              "CustomPrefix": "",
              "MusicSyncEnabled": true,
              "AllowWebSocketFallback": true
            }
            """;

        var config = JsonSerializer.Deserialize<ConfigData>(legacyJson, CreateOptions());

        Assert.NotNull(config);
        Assert.Equal("someone", config.SteamUsername);
        Assert.True(config.MusicSyncEnabled);
        Assert.True(config.AllowWebSocketFallback);
    }

    [Fact]
    public void JsonNodeLoadPath_WithAppRuleCategory_MustSucceed()
    {
        // 回归：配置读取曾走 JsonNode.Deserialize，导致含 Apps 枚举的配置每次都读取失败被重置
        const string json = """{"Apps":[{"ExeName":"qq.exe","DisplayName":"QQ","Category":"Social","Mode":"Foreground","Override":"FollowCategory","Enabled":true,"IsUserConfirmed":true}]}""";
        var options = CreateOptions();
        var node = JsonNode.Parse(json);
        var config = node!.Deserialize<ConfigData>(options);
        Assert.NotNull(config);
        Assert.Single(config!.Apps);
        Assert.Equal(AppCategory.Social, config.Apps[0].Category);
    }

    [Theory]
    [InlineData("\"game\"")]
    [InlineData("4")]
    [InlineData("\"4\"")]
    [InlineData("\"NotARealValue\"")]
    [InlineData("null")]
    public void LenientEnumRead_AnyFormat_DoesNotThrow(string categoryJson)
    {
        // 宽容转换器：任何历史/异常格式的枚举值都不得导致整份配置读取失败
        var json = "{\"Apps\":[{\"ExeName\":\"x.exe\",\"Category\":" + categoryJson + "}]}";
        var config = JsonSerializer.Deserialize<ConfigData>(json, CreateOptions());
        Assert.NotNull(config);
        Assert.Single(config!.Apps);
    }

    [Fact]
    public void EnumNumberValue_DefaultConverter_Behavior()
    {
        // 调查：旧版保存的配置里 Category 是数字时，默认 JsonStringEnumConverter 能否读取
        const string json = """{"Apps":[{"ExeName":"x.exe","Category":4}]}""";
        var config = JsonSerializer.Deserialize<ConfigData>(json, CreateOptions());
        Assert.NotNull(config);
        Assert.Equal(AppCategory.Social, config!.Apps[0].Category);
    }

    [Fact]
    public void SavePath_SerializeToNode_Then_Deserialize_RoundTrip()
    {
        // 调查：Save 走的 SerializeToNode -> ToJsonString 链路是否与读取兼容
        var settings = new ConfigData
        {
            Apps = [new AppRule { ExeName = "x.exe", DisplayName = "X", Category = AppCategory.Social, Enabled = true }]
        };
        var options = CreateOptions();
        var node = JsonSerializer.SerializeToNode(settings, options);
        Assert.NotNull(node);
        var json = node!.ToJsonString(options);
        Assert.Contains("Social", json); // 若失败说明 SerializeToNode 未应用枚举转换器（写成了数字）
        var back = JsonSerializer.Deserialize<ConfigData>(json, options);
        Assert.NotNull(back);
        Assert.Equal(AppCategory.Social, back!.Apps[0].Category);
    }

    [Fact]
    public void Config_RoundTrip_PreservesAppsAndPlayerPriority()
    {
        var original = new ConfigData
        {
            PlayerPriority = ["LxMusic", "NetEase", "Tencent"],
            Apps =
            [
                new AppRule
                {
                    ExeName = "strinova.exe",
                    DisplayName = "卡拉彼丘",
                    Category = AppCategory.Game,
                    Mode = AppSyncMode.Foreground,
                    Override = AppSyncOverride.FollowCategory,
                    Enabled = true,
                    IsUserConfirmed = true
                }
            ],
            MusicFormat = "正在听：{song}{artistPart}",
            ProgressBarFillChar = "█",
            ProgressBarEmptyChar = "░"
        };

        var json = JsonSerializer.Serialize(original, CreateOptions());
        var back = JsonSerializer.Deserialize<ConfigData>(json, CreateOptions());

        Assert.NotNull(back);
        Assert.Equal(["LxMusic", "NetEase", "Tencent"], back.PlayerPriority);
        Assert.Single(back.Apps);
        Assert.Equal("strinova.exe", back.Apps[0].ExeName);
        Assert.Equal(AppCategory.Game, back.Apps[0].Category);
        Assert.Equal(AppSyncOverride.FollowCategory, back.Apps[0].Override);
        Assert.Equal("正在听：{song}{artistPart}", back.MusicFormat);
        Assert.Equal("█", back.ProgressBarFillChar);
    }

    [Fact]
    public void LegacyConfig_WithoutNewFields_UsesDefaults()
    {
        const string oldJson = """
            {
              "AutoStart": false,
              "CloseToTray": true,
              "EnableSteamSync": true
            }
            """;

        var config = JsonSerializer.Deserialize<ConfigData>(oldJson, CreateOptions());

        Assert.NotNull(config);
        Assert.True(config.MusicSyncEnabled);
        Assert.True(config.AppSyncEnabled);
        Assert.True(config.AllowWebSocketFallback);
        Assert.Equal(["NetEase", "Tencent", "LxMusic"], config.PlayerPriority);
        Assert.Empty(config.Apps);
    }
}
