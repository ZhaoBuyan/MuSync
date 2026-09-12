using System.Text;
using MuSync;
using MuSync.Models;
using Xunit;

namespace MuSync.Tests;

public class SteamStatusManagerTests
{
    private static PlayerInfo MakeSong(
        string title = "稻香",
        string artists = "周杰伦",
        double schedule = 150,
        double duration = 255,
        bool pause = false,
        string album = "魔杰座")
    {
        return new PlayerInfo
        {
            Identity = "1",
            Title = title,
            Artists = artists,
            Album = album,
            Cover = "",
            Schedule = schedule,
            Duration = duration,
            Url = "",
            Pause = pause
        };
    }

    /// <summary>测试默认：关闭"暂停隐藏"以观察完整文本路径。</summary>
    private static ConfigData DefaultConfig() => new()
    {
        HideMusicWhenPaused = false
    };

    private static int Utf8ByteCount(string s) => Encoding.UTF8.GetByteCount(s);

    // ---- 音乐 ----

    [Fact]
    public void NullMusicAndApp_FallsBackToProductName()
    {
        Assert.Equal("MuSync", SteamStatusManager.GetStatusPreview(null, null, DefaultConfig()));
    }

    [Fact]
    public void MusicOnly_ContainsTitleArtistAndProgress()
    {
        var preview = SteamStatusManager.GetStatusPreview(MakeSong(), null, DefaultConfig());
        Assert.Contains("稻香", preview);
        Assert.Contains("周杰伦", preview);
        Assert.Contains("2:30/4:15", preview);
        Assert.Contains("#####-----", preview);
    }

    [Fact]
    public void MusicPaused_HiddenWhenOptionOn()
    {
        var config = DefaultConfig();
        config.HideMusicWhenPaused = true;
        Assert.Equal("MuSync", SteamStatusManager.GetStatusPreview(MakeSong(pause: true), null, config));
    }

    [Fact]
    public void MusicPaused_ShowsPausedSuffixWhenOptionOff()
    {
        var preview = SteamStatusManager.GetStatusPreview(MakeSong(pause: true), null, DefaultConfig());
        Assert.Contains("(Paused)", preview);
        Assert.DoesNotContain("[", preview);
    }

    [Fact]
    public void MusicSyncDisabled_HidesMusic()
    {
        var config = DefaultConfig();
        config.MusicSyncEnabled = false;
        Assert.Equal("MuSync", SteamStatusManager.GetStatusPreview(MakeSong(), null, config));
    }

    [Fact]
    public void MusicFormat_SupportsCustomPrefixText()
    {
        var config = DefaultConfig();
        config.MusicFormat = "正在听: {song}{artistPart}";
        var preview = SteamStatusManager.GetStatusPreview(MakeSong(), null, config);
        Assert.StartsWith("正在听: ", preview);
        Assert.Contains("稻香 - 周杰伦", preview);
        Assert.DoesNotContain("[", preview); // 模板不含 {progress}
    }

    // ---- 程序 ----

    [Fact]
    public void ProgramOnly_UsesProgramFormat()
    {
        Assert.Equal("卡拉彼丘", SteamStatusManager.GetStatusPreview(null, "卡拉彼丘", DefaultConfig()));
    }

    [Fact]
    public void ProgramFormat_SupportsDecorationText()
    {
        var config = DefaultConfig();
        config.ProgramFormat = "正在玩【{app}】喵~";
        Assert.Equal("正在玩【卡拉彼丘】喵~",
            SteamStatusManager.GetStatusPreview(null, "卡拉彼丘", config));
    }

    // ---- 组合 ----

    [Fact]
    public void Combined_DefaultTemplate_ContainsAppAndMusic()
    {
        var preview = SteamStatusManager.GetStatusPreview(MakeSong(), "卡拉彼丘", DefaultConfig());
        Assert.Contains("卡拉彼丘", preview);
        Assert.Contains("‖", preview);
        Assert.Contains("稻香 - 周杰伦", preview);
    }

    [Fact]
    public void Combined_CustomSeparator_IsApplied()
    {
        var config = DefaultConfig();
        config.CombinedSeparator = " · ";
        var preview = SteamStatusManager.GetStatusPreview(MakeSong(), "卡拉彼丘", config);
        Assert.Contains(" · ", preview);
    }

    [Fact]
    public void Combined_Overlong_KeepsAppAndStaysWithinLimit()
    {
        var preview = SteamStatusManager.GetStatusPreview(
            MakeSong(title: new string('歌', 30), artists: new string('唱', 30)), "卡拉彼丘", DefaultConfig());
        Assert.Contains("卡拉彼丘", preview);
        Assert.InRange(Utf8ByteCount(preview), 1, 128);
        Assert.DoesNotContain('\uFFFD', preview);
    }

    // ---- 长度与截断 ----

    [Fact]
    public void LongChineseTitle_IsTruncatedWithin128Utf8Bytes()
    {
        var longTitle = new string('音', 80); // 240 字节
        var preview = SteamStatusManager.GetStatusPreview(MakeSong(title: longTitle), null, DefaultConfig());
        Assert.InRange(Utf8ByteCount(preview), 1, 128);
        Assert.DoesNotContain('\uFFFD', preview); // 未截断半个多字节字符
    }

    [Fact]
    public void OverlongProgramName_IsTruncatedWithinLimit()
    {
        var preview = SteamStatusManager.GetStatusPreview(null, new string('名', 100), DefaultConfig());
        Assert.InRange(Utf8ByteCount(preview), 1, 128);
        Assert.DoesNotContain('\uFFFD', preview);
    }
}
