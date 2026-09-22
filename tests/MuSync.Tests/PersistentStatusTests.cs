using MuSync.Models;
using Xunit;

namespace MuSync.Tests;

/// <summary>常驻状态测试：有源不推 / 无源推 / 空文案回退清除 / 超长截断。</summary>
public class PersistentStatusTests
{
    private static PlayerInfo Song() => new()
    {
        Identity = "test",
        Title = "歌",
        Artists = "手",
        Album = "辑",
        Cover = "",
        Schedule = 1,
        Duration = 10,
        Url = "",
        Pause = false
    };

    [Fact]
    public void ComposeStatus_WithMusicSource_IgnoresPersistentText()
    {
        var config = new ConfigData { PersistentStatusEnabled = true, PersistentStatusText = "休息中" };
        var status = SteamStatusManager.ComposeStatus(Song(), null, config);
        Assert.NotNull(status);
        Assert.DoesNotContain("休息中", status);
    }

    [Fact]
    public void ComposeStatus_WithAppSource_IgnoresPersistentText()
    {
        var config = new ConfigData { PersistentStatusEnabled = true, PersistentStatusText = "休息中" };
        var status = SteamStatusManager.ComposeStatus(null, "VS Code", config);
        Assert.NotNull(status);
        Assert.DoesNotContain("休息中", status);
    }

    [Fact]
    public void ComposeStatus_NoSource_PushesPersistentText()
    {
        var config = new ConfigData { PersistentStatusEnabled = true, PersistentStatusText = "在摸鱼 🐟" };
        Assert.Equal("在摸鱼 🐟", SteamStatusManager.ComposeStatus(null, null, config));
    }

    [Fact]
    public void ComposeStatus_NoSource_EmptyText_FallsBackToClear()
    {
        var config = new ConfigData { PersistentStatusEnabled = true, PersistentStatusText = "   " };
        Assert.Null(SteamStatusManager.ComposeStatus(null, null, config));
    }

    [Fact]
    public void ComposeStatus_PersistentDisabled_NoSource_ReturnsNull()
    {
        var config = new ConfigData { PersistentStatusText = "在摸鱼" };
        Assert.Null(SteamStatusManager.ComposeStatus(null, null, config));
    }

    [Fact]
    public void ComposeStatus_PersistentText_TruncatedToStatusLimit()
    {
        var config = new ConfigData { PersistentStatusEnabled = true, PersistentStatusText = new string('好', 200) };
        var status = SteamStatusManager.ComposeStatus(null, null, config);
        Assert.NotNull(status);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(status!) <= 128);
    }
}
