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

    private static ConfigData DefaultConfig() => new()
    {
        ShowArtistName = true,
        ShowProgressBar = true,
        PauseWhenPlayingGame = true
    };

    private static int Utf8ByteCount(string s) => Encoding.UTF8.GetByteCount(s);

    [Fact]
    public void NullInfo_FallsBackToProductName()
    {
        Assert.Equal("MuSync", SteamStatusManager.GetStatusPreview(null, "网易云音乐", DefaultConfig()));
    }

    [Fact]
    public void BasicFormat_ContainsTitleAndArtist()
    {
        var preview = SteamStatusManager.GetStatusPreview(MakeSong(), "网易云音乐", DefaultConfig());
        Assert.Contains("稻香", preview);
        Assert.Contains("周杰伦", preview);
    }

    [Fact]
    public void ProgressBar_IsFormattedCorrectly()
    {
        var preview = SteamStatusManager.GetStatusPreview(MakeSong(schedule: 150, duration: 255), "网易云音乐", DefaultConfig());
        Assert.Contains("2:30/4:15", preview);
        Assert.Contains("[", preview);
        Assert.Contains("]", preview);
        // 进度 150/255 ≈ 0.588 → 10 格中 5 个已填充
        Assert.Contains("#####-----", preview);
    }

    [Fact]
    public void Paused_ShowsPausedSuffix_AndNoProgressBar()
    {
        var preview = SteamStatusManager.GetStatusPreview(MakeSong(pause: true), "网易云音乐", DefaultConfig());
        Assert.Contains("(Paused)", preview);
        Assert.DoesNotContain("[", preview);
    }

    [Fact]
    public void LongChineseTitle_IsTruncatedWithin63Utf8Bytes()
    {
        var longTitle = new string('音', 40); // 120 字节
        var preview = SteamStatusManager.GetStatusPreview(MakeSong(title: longTitle), "网易云音乐", DefaultConfig());
        Assert.InRange(Utf8ByteCount(preview), 1, 63);
        Assert.DoesNotContain('\uFFFD', preview); // 未截断半个多字节字符
    }

    [Fact]
    public void CustomPrefix_IsPrepended_AndTotalStaysWithinLimit()
    {
        var config = DefaultConfig();
        config.EnableCustomPrefix = true;
        config.CustomPrefix = "正在听: ";
        var preview = SteamStatusManager.GetStatusPreview(MakeSong(), "网易云音乐", config);
        Assert.StartsWith("正在听: ", preview);
        Assert.InRange(Utf8ByteCount(preview), 1, 63);
    }

    [Fact]
    public void OverLongPrefix_AloneIsTruncatedToLimit()
    {
        var config = DefaultConfig();
        config.EnableCustomPrefix = true;
        config.CustomPrefix = new string('前', 40); // 120 字节
        var preview = SteamStatusManager.GetStatusPreview(MakeSong(), "网易云音乐", config);
        Assert.InRange(Utf8ByteCount(preview), 1, 63);
        Assert.DoesNotContain('\uFFFD', preview);
    }

    [Fact]
    public void ArtistPriority_DropsProgressBarWhenTooLong()
    {
        var config = DefaultConfig();
        config.StatusPriority = SteamStatusPriority.Artist;
        var preview = SteamStatusManager.GetStatusPreview(
            MakeSong(title: new string('歌', 8), artists: new string('唱', 10)), "网易云音乐", config);
        // 标题+歌手放得下时：保留歌手、舍弃进度条
        Assert.DoesNotContain("[", preview);
        Assert.Contains(new string('唱', 10), preview);
        Assert.InRange(Utf8ByteCount(preview), 1, 63);
    }

    [Fact]
    public void ProgressBarPriority_DropsArtistWhenTooLong()
    {
        var config = DefaultConfig();
        config.StatusPriority = SteamStatusPriority.ProgressBar;
        config.ShowArtistName = true;
        var preview = SteamStatusManager.GetStatusPreview(
            MakeSong(title: new string('歌', 8), artists: new string('唱', 10)), "网易云音乐", config);
        // 放不下全部时：优先保留进度条、舍弃歌手
        Assert.Contains("[", preview);
        Assert.DoesNotContain(new string('唱', 10), preview);
        Assert.InRange(Utf8ByteCount(preview), 1, 63);
    }

    [Fact]
    public void ExtremelyLongTitle_FallsBackToTruncatedTitleOnly()
    {
        var config = DefaultConfig();
        var preview = SteamStatusManager.GetStatusPreview(
            MakeSong(title: new string('歌', 40), artists: "周杰伦"), "网易云音乐", config);
        // 标题本身已超限时，兜底只保留截断后的标题（歌手与进度条都舍弃）
        Assert.DoesNotContain("周杰伦", preview);
        Assert.DoesNotContain("[", preview);
        Assert.InRange(Utf8ByteCount(preview), 1, 63);
        Assert.DoesNotContain('\uFFFD', preview);
    }
}
