using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

[Collection(LanguageSensitiveCollection.Name)]
public class AppearanceFontTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public void SupportedFontSizesKeepHeadingsAndFooterInsideTheirBounds(int size)
    {
        var config = Configurations.Instance;
        var previous = config.Settings;
        try
        {
            typeof(Configurations).GetProperty(nameof(Configurations.Settings))!.SetValue(config,
                new ConfigData { AppearanceFontFamily = "Microsoft YaHei", AppearanceFontSize = size });
            using var main = new MainForm();
            Control Field(string name) => (Control)typeof(MainForm).GetField(name, Fields)!.GetValue(main)!;
            var title = ((Label[])typeof(MainForm).GetField("_songTitleLabels", Fields)!.GetValue(main)!)[0];
            Assert.True(title.Height >= title.Font.Height);
            var heading = Field("_outputHeading");
            Assert.True(heading.Height >= heading.Font.Height);
            var button = Field("_settingsButton");
            Assert.True(button.Height >= button.Font.Height + 4);
            Assert.True(Field("_outputBudget").Bottom < button.Top);
            Assert.True(Field("_appSyncPanel").Bottom < heading.Top);
        }
        finally { typeof(Configurations).GetProperty(nameof(Configurations.Settings))!.SetValue(config, previous); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothSaveButtonsPersistAndApplyFontAndResetRestoresDefault(bool returnHome)
    {
        var config = Configurations.Instance;
        var previous = config.Settings;
        var pathField = typeof(Configurations).GetField("_path", Fields)!;
        var previousPath = pathField.GetValue(config);
        var temporary = Path.Combine(Path.GetTempPath(), "musync-font-" + Guid.NewGuid() + ".json");
        try
        {
            typeof(Configurations).GetProperty(nameof(Configurations.Settings))!.SetValue(config,
                new ConfigData { AutoStart = Win32Api.AutoStart.Check() });
            pathField.SetValue(config, temporary);
            using var main = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
            main.Show(); main.ShowSettings();
            var view = main.Controls.OfType<SettingsView>().Single();
            var title = ((Label[])typeof(MainForm).GetField("_songTitleLabels", Fields)!.GetValue(main)!)[0];
            var time = ((Label[])typeof(MainForm).GetField("_progressLabels", Fields)!.GetValue(main)!)[0];
            var originalTitle = title.Font;
            var originalTime = time.Font;
            typeof(SettingsView).GetField("_appearanceFontFamily", Fields)!.SetValue(view, "Arial");
            typeof(SettingsView).GetField("_appearanceFontSize", Fields)!.SetValue(view, 11f);
            typeof(SettingsView).GetField("_appearanceFontStyle", Fields)!.SetValue(view, FontStyle.Italic);
            ((Button)typeof(SettingsView).GetField(returnHome ? "_okButton" : "_applyButton", Fields)!.GetValue(view)!).PerformClick();
            Assert.Equal("Arial", title.Font.FontFamily.Name);
            Assert.True(title.Font.Italic);
            Assert.True(title.Font.Bold);
            Assert.Equal("Arial", time.Font.FontFamily.Name);
            typeof(MainForm).GetMethod("RelayoutForSize", Fields)!.Invoke(main, null);
            Assert.Equal("Arial", time.Font.FontFamily.Name);
            var saved = JsonSerializer.Deserialize<ConfigData>(File.ReadAllText(temporary), ConfigJson.Options)!;
            Assert.Equal("Arial", saved.AppearanceFontFamily);
            Assert.Equal(11, saved.AppearanceFontSize);
            Assert.Equal((int)FontStyle.Italic, saved.AppearanceFontStyle);
            var font = title.Font;
            main.ApplyAppearance();
            Assert.Same(font, title.Font); // 未改变设置，不重复分配字体资源。
            main.ShowSettings();
            typeof(SettingsView).GetMethod("ResetAppearanceControls", Fields)!.Invoke(view, null);
            ((Button)typeof(SettingsView).GetField("_applyButton", Fields)!.GetValue(view)!).PerformClick();
            Assert.Same(originalTitle, title.Font);
            Assert.Same(originalTime, time.Font);
        }
        finally
        {
            pathField.SetValue(config, previousPath);
            typeof(Configurations).GetProperty(nameof(Configurations.Settings))!.SetValue(config, previous);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
