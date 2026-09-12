using System.Text.Json;
using System.Text.Json.Serialization;
using MuSync;
using MuSync.Models;
using Xunit;

namespace MuSync.Tests;

/// <summary>配置兼容性回归：旧版本配置文件（含已废弃字段）必须能正常读取，不得触发"损坏重置"。</summary>
public class ConfigCompatibilityTests
{
    private static JsonSerializerOptions CreateOptions() => new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

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
