using MuSync.Models;
using Xunit;

namespace MuSync.Tests;

/// <summary>进度条渲染测试：格数 / emoji / 边界值。</summary>
public class ProgressBarTests
{
    private static PlayerInfo Song(double schedule, double duration) => new()
    {
        Identity = "test",
        Title = "歌",
        Artists = "手",
        Album = "辑",
        Cover = "",
        Schedule = schedule,
        Duration = duration,
        Url = "",
        Pause = false
    };

    [Fact]
    public void ComposeStatus_UsesConfiguredBarLength()
    {
        var config = new ConfigData
        {
            MusicFormat = "{progress}",
            ProgressBarLength = 5,
            ProgressBarFillChar = "#",
            ProgressBarEmptyChar = "-"
        };
        var status = SteamStatusManager.ComposeStatus(Song(50, 100), null, config);
        Assert.NotNull(status);
        Assert.Contains("[##---]", status);
    }

    [Fact]
    public void ComposeStatus_InvalidBarLength_FallsBackToDefault()
    {
        var config = new ConfigData
        {
            MusicFormat = "{progress}",
            ProgressBarLength = 0,
            ProgressBarFillChar = "#",
            ProgressBarEmptyChar = "-"
        };
        var status = SteamStatusManager.ComposeStatus(Song(50, 100), null, config);
        Assert.NotNull(status);
        Assert.Contains("[#####-----]", status);
    }

    [Fact]
    public void ComposeStatus_EmojiBar_RendersPerCharacter()
    {
        var config = new ConfigData
        {
            MusicFormat = "{progress}",
            ProgressBarLength = 4,
            ProgressBarFillChar = "❤️",
            ProgressBarEmptyChar = "💕"
        };
        var status = SteamStatusManager.ComposeStatus(Song(75, 100), null, config);
        Assert.NotNull(status);
        Assert.Contains("[❤️❤️❤️💕]", status);
    }
}
