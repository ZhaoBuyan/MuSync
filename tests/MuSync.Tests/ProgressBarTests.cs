using MuSync.Models;
using Xunit;

namespace MuSync.Tests;

/// <summary>进度条渲染测试：格数 / emoji / 边界值。</summary>
public class ProgressBarTests
{
    private static PlayerInfo Song(double schedule, double duration, bool pause = false) => new()
    {
        Identity = "test",
        Title = "歌",
        Artists = "手",
        Album = "辑",
        Cover = "",
        Schedule = schedule,
        Duration = duration,
        Url = "",
        Pause = pause
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
    // ---- 进度条与时间轴拆分成两块积木 ----

    private static ConfigData Config(string format) => new()
    {
        MusicSyncEnabled = true,
        HideMusicWhenPaused = false,
        MusicFormat = format,
        ProgressBarLength = 5,
        ProgressBarFillChar = "#",
        ProgressBarEmptyChar = "-"
    };

    [Fact]
    public void BarAndTime_Together_EqualLegacyProgress()
    {
        // 拆分布局必须与旧的 {progress} 输出**逐字符相同**，
        // 否则老用户改成两块后状态里会多/少空格。
        var song = Song(50, 100);

        var legacy = SteamStatusManager.ComposeStatus(song, null, Config("{song}{progress}"));
        var split = SteamStatusManager.ComposeStatus(song, null, Config("{song}{bar}{time}"));

        Assert.Equal(legacy, split);
        Assert.Contains("[##---]", split);
        Assert.Contains("0:50/1:40", split);
    }

    [Fact]
    public void BarOnly_HasNoTime()
    {
        var status = SteamStatusManager.ComposeStatus(Song(50, 100), null, Config("{bar}"));

        Assert.Contains("[##---]", status);
        Assert.DoesNotContain("/", status);      // 没有 "0:50/1:40"
        Assert.DoesNotContain(":", status);
    }

    [Fact]
    public void TimeOnly_HasNoBar()
    {
        var status = SteamStatusManager.ComposeStatus(Song(50, 100), null, Config("{time}"));

        Assert.Contains("0:50/1:40", status);
        Assert.DoesNotContain("[", status);
        Assert.DoesNotContain("#", status);
    }

    [Fact]
    public void Paused_BarShowsPaused_TimeIsEmpty()
    {
        var paused = Song(50, 100, pause: true);


        Assert.Equal("(Paused)", SteamStatusManager.ComposeStatus(paused, null, Config("{bar}")));
        // 暂停时没有意义的时间不该显示；{progress} 的老行为保持不变
        Assert.Equal("(Paused)", SteamStatusManager.ComposeStatus(paused, null, Config("{progress}")));
    }

    [Fact]
    public void UnknownDuration_BothPartsEmpty()
    {
        // duration<=0：拿不到时长时两块都应为空，不能显示 0:00/0:00
        var barOnly = SteamStatusManager.ComposeStatus(Song(10, 0), null, Config("{bar}"));
        Assert.True(string.IsNullOrEmpty(barOnly), "拿不到时长时进度条应为空");
        var timeOnly = SteamStatusManager.ComposeStatus(Song(10, 0), null, Config("{time}"));
        Assert.True(string.IsNullOrEmpty(timeOnly), "拿不到时长时时间轴应为空");
        var combined = SteamStatusManager.ComposeStatus(Song(10, 0), null, Config("{song}{bar}{time}"));
        Assert.Equal("歌", combined);
    }
    [Fact]
    public void Paused_TimeOnly_IsEmpty()
    {
        // 审计指出：名字叫 TimeIsEmpty 的测试从未真的断言过 {time}
        var status = SteamStatusManager.ComposeStatus(Song(50, 100, pause: true), null, Config("{time}"));

        Assert.True(string.IsNullOrEmpty(status));
    }

    [Fact]
    public void SplitRender_HasExpectedValue()
    {
        // 只断言 legacy == split 时，两边同时错也能过；这里钉死具体期望
        var status = SteamStatusManager.ComposeStatus(Song(50, 100), null, Config("{bar}{time}"));

        // 注意：ApplyTemplate 末尾会 Trim()，所以前导空格不会出现在最终状态里
        Assert.Equal("[##---] 0:50/1:40", status);
    }
}