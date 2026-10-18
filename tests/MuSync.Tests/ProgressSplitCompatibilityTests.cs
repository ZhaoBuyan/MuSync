using System.Collections.Generic;
using System.Linq;
using MuSync;
using MuSync.Models;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 「进度条 / 时间轴」拆分相关的兼容性守门测试。
/// <para>
/// 这一组盯的不是渲染本身（那在 <see cref="ProgressBarTests"/> 里），
/// 而是拆分带来的**副作用**：内建标记与自定义数据源撞名、以及
/// <c>{progress}</c> 被编辑器改写成 <c>{bar}{time}</c> 后模板预设识别是否还正常。
/// </para>
/// </summary>
public class ProgressSplitCompatibilityTests
{
    private static PlayerInfo Song(double schedule = 50, double duration = 100, bool pause = false) => new()
    {
        Identity = "t",
        Title = "歌",
        Artists = "手",
        Album = "辑",
        Cover = "",
        Schedule = schedule,
        Duration = duration,
        Url = "",
        Pause = pause
    };

    private static string Render(string format, PlayerInfo? song, bool musicEnabled = true)
    {
        var config = new ConfigData
        {
            MusicSyncEnabled = musicEnabled,
            HideMusicWhenPaused = false,
            MusicFormat = format,
            ProgressBarLength = 5,
            ProgressBarFillChar = "#",
            ProgressBarEmptyChar = "-"
        };
        return SteamStatusManager.GetStatusPreview(song, null, config);
    }

    // ---- 内建保留名：不能和自定义数据源撞名 ----

    [Fact]
    public void BuiltInNames_AreExactlyTheOnesCodecTreatsAsBuiltIn()
    {
        // 保留名集合与积木编解码必须一致：集合里的名字解析出来不允许是"数据源块"，
        // 否则会出现"设置页拒收该名字、但模板里它本来也不会被当成数据源"的错位。
        foreach (var name in SteamStatusManager.BuiltInBlockNames)
        {
            var blocks = FormatBlockCodec.Parse("{" + name + "}");
            Assert.DoesNotContain(blocks, b => b.Type == FormatBlockType.DataSource);
        }
    }

    [Theory]
    [InlineData("app")]
    [InlineData("song")]
    [InlineData("artist")]
    [InlineData("artistPart")]
    [InlineData("bar")]
    [InlineData("time")]
    [InlineData("progress")]
    [InlineData("sep")]
    public void BuiltInNames_CoverEveryBuiltInToken(string name)
    {
        Assert.Contains(name, SteamStatusManager.BuiltInBlockNames);
    }

    [Fact]
    public void BuiltInNames_DoNotContainUnrelatedNames()
    {
        Assert.DoesNotContain("心率", SteamStatusManager.BuiltInBlockNames);
        Assert.DoesNotContain("heart_rate", SteamStatusManager.BuiltInBlockNames);
    }

    [Fact]
    public void DataSourceWithBuiltInName_CannotRender_WhichIsWhyWeRejectIt()
    {
        // 这条说明"为什么必须在设置页拒收"：内建标记优先，
        // 同名数据源即使有值也永远渲染不到（这里用 name=time 演示）。
        var blocks = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["time"] = "数据源的值"
        };
        var config = new ConfigData
        {
            MusicSyncEnabled = true,
            HideMusicWhenPaused = false,
            MusicFormat = "{time}",
            ProgressBarLength = 5
        };

        var text = SteamStatusManager.GetStatusPreview(Song(), null, config, blocks);

        Assert.Contains("/", text);              // 是时间轴的 "0:50/1:40"
        Assert.DoesNotContain("数据源的值", text);  // 数据源被内建标记抢走
    }

    // ---- 预设识别：{progress} 被改写成 {bar}{time} 后仍是「简洁」 ----

    [Fact]
    public void PresetNormalization_TreatsSplitFormAsDefault()
    {
        Assert.Equal("{song}{artistPart}{progress}",
            SettingsForm.NormalizeFormatForPreset("{song}{artistPart}{bar}{time}"));
    }

    [Fact]
    public void PresetNormalization_KeepsOtherFormatsUnchanged()
    {
        Assert.Equal("{song}", SettingsForm.NormalizeFormatForPreset("{song}"));
        Assert.Equal("{app} {sep} {song}", SettingsForm.NormalizeFormatForPreset("{app} {sep} {song}"));
        // 只有"相邻的 {bar}{time}"才等价于 {progress}；中间有文字时不能归一（否则会把用户的排版也抹掉）
        Assert.Equal("{bar} {time}", SettingsForm.NormalizeFormatForPreset("{bar} {time}"));
        Assert.Equal("", SettingsForm.NormalizeFormatForPreset(null));
    }

    // ---- 拆分等价性：覆盖各种状态（审计指出原来只测了正常播放）----

    [Theory]
    [InlineData(50, 100, false)]      // 正常
    [InlineData(0, 100, false)]       // 开头
    [InlineData(100, 100, false)]     // 结尾
    [InlineData(-5, 100, false)]      // 位置为负（应夹到 0:00）
    [InlineData(5000, 4000, false)]   // 位置超出时长
    [InlineData(3725, 7450, false)]   // 超过 1 小时
    [InlineData(50, 0, false)]        // 拿不到时长
    [InlineData(50, -10, false)]      // 时长非法
    [InlineData(50, 100, true)]       // 暂停
    public void SplitForm_RendersIdenticallyToLegacy_AcrossStates(double schedule, double duration, bool pause)
    {
        var song = Song(schedule, duration, pause);

        var legacy = Render("{song}{progress}", song);
        var split = Render("{song}{bar}{time}", song);

        Assert.Equal(legacy, split);
    }

    [Fact]
    public void SplitForm_RendersIdenticallyToLegacy_ForNonFiniteValues()
    {
        // NaN / ±Infinity 不能放进 InlineData，单独测：
        // 这些值现实可能出现（内存读取错位），必须两边都安全地展开为空。
        foreach (var song in new[]
        {
            Song(double.NaN, 100),
            Song(50, double.NaN),
            Song(double.PositiveInfinity, 100),
            Song(50, double.NegativeInfinity),
            Song(double.PositiveInfinity, double.NaN)
        })
        {
            var legacy = Render("{song}{progress}", song);
            var split = Render("{song}{bar}{time}", song);
            Assert.Equal(legacy, split);
        }
    }

    [Fact]
    public void SplitForm_MatchesLegacy_InCombinedFormatToo()
    {
        // 组合模板走的是另一条路径（含超长降级链），同样要保持等价
        foreach (var duration in new[] { 100.0, 0.0, double.NaN })
        {
            var s = Song(50, duration);
            var legacyConfig = new ConfigData
            {
                MusicSyncEnabled = true,
                HideMusicWhenPaused = false,
                CombinedFormat = "{app} {sep} {song}{progress}",
                CombinedSeparator = "‖",
                ProgressBarLength = 5
            };
            var splitConfig = new ConfigData
            {
                MusicSyncEnabled = true,
                HideMusicWhenPaused = false,
                CombinedFormat = "{app} {sep} {song}{bar}{time}",
                CombinedSeparator = "‖",
                ProgressBarLength = 5
            };

            Assert.Equal(
                SteamStatusManager.GetStatusPreview(s, "卡拉彼丘", legacyConfig),
                SteamStatusManager.GetStatusPreview(s, "卡拉彼丘", splitConfig));
        }
    }
}
