using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

[Collection(LanguageSensitiveCollection.Name)]
public class ThemeAndRecoveryTests
{
    [Fact]
    public void WithdrawnNightThemeIsHiddenAndOldConfigFallsBackToFog()
    {
        var originalConfig = Configurations.Instance.Settings;
        var previous = Theme.Palette.Id;
        using var scratch = new Panel();
        try
        {
            typeof(Configurations).GetProperty(nameof(Configurations.Settings))!
                .SetValue(Configurations.Instance, new ConfigData { UiTheme = "night" });
            Theme.SwitchPalette("night", scratch);
            Assert.Equal("fog", Theme.Palette.Id);
            Assert.DoesNotContain(ThemePalettes.All, p => p.Id == "night" || p.IsDark);
            using var main = new MainForm();
            main.ShowSettings();
            var view = main.Controls.OfType<SettingsView>().Single();
            var selector = (ComboBox)typeof(SettingsView).GetField("_themeCombo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
            Assert.Equal(4, selector.Items.Count);
            Assert.DoesNotContain(selector.Items.Cast<ThemePalette>(), p => p.Id == "night");
            Assert.Equal("fog", ((ThemePalette)selector.SelectedItem!).Id);
            Assert.False(Theme.Palette.IsDark);
        }
        finally
        {
            typeof(Configurations).GetProperty(nameof(Configurations.Settings))!.SetValue(Configurations.Instance, originalConfig);
            Theme.SwitchPalette(previous, scratch);
        }
    }

    [Fact]
    public void OldAndUnknownThemeConfigFallsBackWithoutLosingSettings()
    {
        var old = JsonSerializer.Deserialize<ConfigData>("{\"MusicFormat\":\"{song}\"}", ConfigJson.Options)!;
        Assert.Equal("fog", old.UiTheme);
        var unknown = JsonSerializer.Deserialize<ConfigData>("{\"UiTheme\":\"future\",\"MusicFormat\":\"{song}\"}", ConfigJson.Options)!;
        Assert.Equal("fog", ThemePalettes.Resolve(unknown.UiTheme).Id);
        Assert.Equal("{song}", unknown.MusicFormat);
        unknown.UiTheme = "sage";
        Assert.Equal("sage", JsonSerializer.Deserialize<ConfigData>(JsonSerializer.Serialize(unknown, ConfigJson.Options), ConfigJson.Options)!.UiTheme);
    }

    [Fact]
    public void SwitchingThemeRecolorsUiButPreservesCustomAndErrorColors()
    {
        var original = Theme.Palette.Id;
        using var root = new Panel { BackColor = Theme.Paper };
        var themed = new CueButton { BackColor = Theme.Accent, ForeColor = Theme.Paper };
        var custom = new Label { ForeColor = Color.Fuchsia };
        var error = new Label { ForeColor = Theme.OnAir };
        var customWhite = new CueButton { Name = "TitleColorSwatch", BackColor = Color.White };
        root.Controls.AddRange([themed, custom, error, customWhite]);
        try
        {
            foreach (var palette in ThemePalettes.All)
            {
                Theme.SwitchPalette(palette.Id, root);
                Assert.Equal(palette.Paper, root.BackColor);
                Assert.Equal(palette.Accent, themed.BackColor);
                Assert.Equal(Color.Fuchsia, custom.ForeColor);
                Assert.Equal(Theme.OnAir, error.ForeColor);
                Assert.Equal(Color.White, customWhite.BackColor);
            }
        }
        finally { Theme.SwitchPalette(original, root); }
    }

    [Fact]
    public void AllThemesHaveReadableTextAndPrimaryButtons()
    {
        static double L(Color c)
        {
            static double Linear(byte value) { var v = value / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        }
        static double Ratio(Color a, Color b) => (Math.Max(L(a), L(b)) + .05) / (Math.Min(L(a), L(b)) + .05);
        foreach (var p in ThemePalettes.All)
        {
            Assert.True(Ratio(p.Paper, p.Ink) >= 7, p.Id);
            Assert.True(Ratio(p.Paper, p.Muted) >= 4.5, p.Id);
            Assert.True(Ratio(p.Paper, p.Accent) >= 4.5, p.Id);
        }
    }

    [Fact]
    public void RecoverableFailureIsCountedAndNextOperationCanRun()
    {
        var count = UiHealth.Failures;
        Assert.False(UiHealth.TryRun("test-recovery-" + Guid.NewGuid(), () => throw new InvalidOperationException("offline fixture")));
        Assert.Equal(count + 1, UiHealth.Failures);
        var ran = false;
        Assert.True(UiHealth.TryRun("test-next-operation", () => ran = true));
        Assert.True(ran);
    }

    [Fact]
    public void FeedbackLinkOnlyOpensIssueChooserWithoutSendingDiagnostics()
    {
        var uri = new Uri(FeedbackLink.IssuesUrl);
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("github.com", uri.Host);
        Assert.Equal("/ZhaoBuyan/MuSync/issues/new/choose", uri.AbsolutePath);
        Assert.Equal("", uri.Query);
    }

    [Fact]
    public void RepeatedRefreshFailurePausesTimerAndPageSwitchCanRecover()
    {
        using var main = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        main.Show(); main.ShowSettings();
        var view = main.Controls.OfType<SettingsView>().Single();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var pages = (TabControl)typeof(SettingsView).GetField("_pages", flags)!.GetValue(view)!;
        pages.SelectedIndex = 5;   // 数据源页（加了"外观"页后索引右移）
        var field = typeof(SettingsView).GetField("_browserExtensionStatusLabel", flags)!;
        var original = field.GetValue(view);
        var timer = (Timer)typeof(SettingsView).GetField("_previewTimer", flags)!.GetValue(view)!;
        try
        {
            field.SetValue(view, null);
            for (var i = 0; i < 3; i++) typeof(Timer).GetMethod("OnTick", flags)!.Invoke(timer, [EventArgs.Empty]);
            Assert.False(timer.Enabled);
        }
        finally { field.SetValue(view, original); }
        pages.SelectedIndex = 0;
        pages.SelectedIndex = 5;   // 数据源页（加了"外观"页后索引右移）
        Assert.True(timer.Enabled);
    }
}
