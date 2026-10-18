using System.Collections.Generic;
using System.Windows.Forms;
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
    public void DataSourceWithBuiltInName_IsShadowedByBuiltInToken()
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
    // ---- token 漂移守卫：新增内建 token 却忘了加进保留名集合时，这里必须变红 ----

    [Fact]
    public void EveryCodecBuiltInToken_IsInReservedNames()
    {
        // 反方向（集合 → codec）挡不住"往 Tokens 里加了新 token、忘了加进 BuiltInBlockNames"，
        // 这里从 codec 的 token 表反推：每个内建 token 的名字都必须在保留名集合里。
        foreach (var (token, _) in FormatBlockCodec.Tokens)
        {
            var name = token.Trim('{', '}');
            Assert.Contains(name, SteamStatusManager.BuiltInBlockNames);
        }
    }

    [Fact]
    public void ProgressToken_IsAlsoReserved()
    {
        // {progress} 不在 Tokens 表里（由 Parse 特判拆分），单独钉一次，防止漏加。
        Assert.Contains("progress", SteamStatusManager.BuiltInBlockNames);
    }

    // ---- 名字校验：空名 / 超长 / 含大括号都不能编译成 {} ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("}")]
    [InlineData("a}b")]
    public void Compile_SkipsDataSourceBlockWithInvalidName(string badName)
    {
        var blocks = new System.Collections.Generic.List<FormatBlock>
        {
            new() { Type = FormatBlockType.Song },
            new() { Type = FormatBlockType.DataSource, Text = badName }
        };

        var compiled = FormatBlockCodec.Compile(blocks);

        Assert.Equal("{song}", compiled);      // 不合法的数据源块整体跳过，绝不产出 {}
        Assert.DoesNotContain("{}", compiled);
    }

    [Fact]
    public void Compile_SkipsDataSourceBlockWithTooLongName()
    {
        var longName = new string('X', SteamStatusManager.MaxBlockNameLength + 1);
        var compiled = FormatBlockCodec.Compile([new FormatBlock { Type = FormatBlockType.DataSource, Text = longName }]);

        Assert.Equal("", compiled);
    }

    [Fact]
    public void Compile_TrimsDataSourceName()
    {
        // 前后空格会被去掉：否则 { 心率} 在模板里匹配不上名为"心率"的数据源
        var compiled = FormatBlockCodec.Compile([new FormatBlock { Type = FormatBlockType.DataSource, Text = "  心率  " }]);

        Assert.Equal("{心率}", compiled);
    }

    [Fact]
    public void IsValidBlockName_MatchesTemplateRegexLimits()
    {
        Assert.True(FormatBlockCodec.IsValidBlockName("心率"));
        Assert.True(FormatBlockCodec.IsValidBlockName(new string('X', SteamStatusManager.MaxBlockNameLength)));
        Assert.False(FormatBlockCodec.IsValidBlockName(new string('X', SteamStatusManager.MaxBlockNameLength + 1)));
        Assert.False(FormatBlockCodec.IsValidBlockName(""));
        Assert.False(FormatBlockCodec.IsValidBlockName("  "));
        Assert.False(FormatBlockCodec.IsValidBlockName("{a}"));
        Assert.False(FormatBlockCodec.IsValidBlockName("a\nb"));
    }

    // ---- 撞名提示文案：按实际名字生成，且能算大小写建议 ----

    [Fact]
    public void BuiltInNameWarning_MentionsActualNameAndSuggestion()
    {
        var warning = SettingsForm.BuiltInNameWarning("time");

        Assert.Contains("{time}", warning);
        Assert.Contains("{Time}", warning);   // 建议的可用拼写
    }

    [Fact]
    public void BuiltInNameWarning_EmptyNameIsEmpty()
    {
        Assert.Equal("", SettingsForm.BuiltInNameWarning(""));
    }
    // ---- 设置页接线：撞名保留 + 预设当场识别（审计指出这两处原来零回归守卫）----

    [Fact]
    public void CollectDataSources_KeepsCollidingName_AndWarns()
    {
        var settings = Configurations.Instance.Settings;
        var backup = settings.DataSources;
        try
        {
            settings.DataSources =
            [
                new DataSourceConfig { Enabled = true, Name = "time", Url = "http://127.0.0.1:1/a", ValuePath = "v", Format = "{v}" },
                new DataSourceConfig { Enabled = true, Name = "心率", Url = "http://127.0.0.1:1/b", ValuePath = "v", Format = "{v}" }
            ];

            using var form = new SettingsForm();
            var collect = typeof(SettingsForm).GetMethod("CollectDataSources",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var kept = ((System.Collections.IEnumerable)collect!.Invoke(form, null)!)
                .Cast<DataSourceConfig>().Select(s => s.Name).ToList();

            // 关键：撞名不能把配置删掉（旧版本里配过的名字，升级后动一次设置就没了）
            Assert.Contains("time", kept);
            Assert.Contains("心率", kept);
        }
        finally
        {
            settings.DataSources = backup;
        }
    }

    [Fact]
    public void SyncPresetFromFormats_RecognizesSplitFormImmediately()
    {
        // 审计复现：编辑器把 {progress} 改写成 {bar}{time} 后，同一次会话里预设会跳成「自定义」。
        using var form = new SettingsForm();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var music = (TextBox)typeof(SettingsForm).GetField("_musicFormatBox", flags)!.GetValue(form)!;
        var program = (TextBox)typeof(SettingsForm).GetField("_programFormatBox", flags)!.GetValue(form)!;
        var combined = (TextBox)typeof(SettingsForm).GetField("_combinedFormatBox", flags)!.GetValue(form)!;
        var combo = (ComboBox)typeof(SettingsForm).GetField("_templatePresetCombo", flags)!.GetValue(form)!;
        var sync = typeof(SettingsForm).GetMethod("SyncPresetFromFormats", flags)!;

        music.Text = "{song}{artistPart}{bar}{time}";
        program.Text = "{app}";
        combined.Text = "{app} {sep} {song}{artistPart}";
        sync.Invoke(form, null);

        Assert.Equal(1, combo.SelectedIndex);   // 1 = 简洁（默认）
    }

}
