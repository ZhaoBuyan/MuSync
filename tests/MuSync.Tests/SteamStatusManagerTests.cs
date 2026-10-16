using System;
using System.Text;
using MuSync;
using MuSync.Models;
using Xunit;

namespace MuSync.Tests;

/// <summary>状态文本生成测试：模板 / 截断 / 组合显示等。</summary>
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
    // ---- 时间格式化：超过一小时必须带小时位 ----

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(9, "0:09")]
    [InlineData(59, "0:59")]
    [InlineData(60, "1:00")]
    [InlineData(1352, "22:32")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(4100, "1:08:20")]      // 实测踩过的值：1 小时 8 分 20 秒
    [InlineData(7325, "2:02:05")]
    public void FormatClockTime_UsesHoursWhenNeeded(double seconds, string expected)
    {
        Assert.Equal(expected, SteamStatusManager.FormatClockTime(seconds));
    }

    [Fact]
    public void FormatClockTime_NegativeBecomesZero()
    {
        Assert.Equal("0:00", SteamStatusManager.FormatClockTime(-5));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1e18)]                          // 超大有限值：同样会把时长算爆
    [InlineData(double.MaxValue)]
    [InlineData(999999.0)]                      // 超出合理范围，按"时长未知"处理
    public void FormatClockTime_NonFiniteOrAbsurd_DoesNotThrow(double seconds)
    {
        // 时长来自外部（SMTC 有源用哨兵值表示"未知"、LX 的 HTTP JSON），
        // 若不夹取则 TimeSpan.FromSeconds 会抛（NaN → ArgumentException；±Infinity / 超大值 → OverflowException），
        // 一路冒到主循环兜底 → 每拍重建全部播放器并清空状态，等于该媒体存在期间同步彻底失效。
        Assert.Equal("0:00", SteamStatusManager.FormatClockTime(seconds));
    }

    [Fact]
    public void TimeSpan_FromSeconds_ThrowsOnNonFiniteAndAbsurdValues()
    {
        // 反向验证：确认"夹取之前确实会抛"，证明上面那条是有意义的回归测试。
        // 实测的类型不同，所以分开断言（ArgumentException 与 OverflowException 之间没有继承关系）；
        // 注：TimeSpan.MaxValue.TotalSeconds 实测**不抛**，所以不用它。
        Assert.Throws<ArgumentException>(() => TimeSpan.FromSeconds(double.NaN));
        Assert.Throws<OverflowException>(() => TimeSpan.FromSeconds(double.PositiveInfinity));
        Assert.Throws<OverflowException>(() => TimeSpan.FromSeconds(1e18));
    }
    // ---- 进度条：非有限的位置/总长不得抛（本项目开了 CheckForOverflowUnderflow）----

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1e18)]
    public void ProgressBar_NonFiniteSchedule_DoesNotThrow(double schedule)
    {
        // 洞曾在 (int)(progress * barLength)：Math.Clamp(NaN,0,1) 返回 NaN（不是 0），
        // checked 上下文中 (int)NaN 抛 OverflowException → 冒到主循环兜底 → 每拍清空全部播放器。
        // 位置来自播放器内存读取，读到错位数据时出现 NaN 是现实可能的。
        var song = MakeSong(schedule: schedule, duration: 255);
        var preview = SteamStatusManager.GetStatusPreview(song, null, DefaultConfig());
        Assert.NotNull(preview);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e18)]
    public void ProgressBar_NonFiniteDuration_DoesNotThrow(double duration)
    {
        var song = MakeSong(schedule: 10, duration: duration);
        var preview = SteamStatusManager.GetStatusPreview(song, null, DefaultConfig());
        Assert.NotNull(preview);
    }

    [Fact]
    public void ProgressBar_ScheduleBeyondDuration_FillsCompletelyWithoutThrowing()
    {
        // 位置远超总长：进度夹到满格，且不得抛（与 MainForm 百分比路径是同型转型）
        var config = DefaultConfig();
        config.MusicFormat = "{song}{progress}";   // 必须显式带上 {progress} 才会渲染进度条
        config.ProgressBarLength = 10;
        var song = MakeSong(schedule: 9999, duration: 100);

        var preview = SteamStatusManager.GetStatusPreview(song, null, config);

        Assert.NotNull(preview);
        var compact = preview.Replace(" ", "");
        Assert.Contains(new string('#', 10), compact);   // 满格
        Assert.DoesNotContain('-', compact);             // 没有空槽
    }
}