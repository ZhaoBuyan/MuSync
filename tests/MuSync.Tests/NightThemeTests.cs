using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using MuSync.Utils;
using MuSync.Models;
using Xunit;

namespace MuSync.Tests;

[Collection(LanguageSensitiveCollection.Name)]
public class NightThemeTests
{
    [Fact]
    public void NightThemeUsesDarkBackgroundAndInputsAndCanFollowThemeAgain()
    {
        var originalConfig = Configurations.Instance.Settings;
        var originalPalette = Theme.Palette.Id;
        using var scratch = new Panel();
        try
        {
            typeof(Configurations).GetProperty(nameof(Configurations.Settings))!
                .SetValue(Configurations.Instance, new ConfigData { UiTheme = "night" });
            Theme.PreviewPalette(ThemePalettes.NightPrototype, scratch);
            using var main = new MainForm();
            Theme.PreviewPalette(ThemePalettes.NightPrototype, main);
            Assert.Equal(Theme.Paper, main.BackColor);
            Assert.True(main.BackColor.GetBrightness() < .2);
            main.ShowSettings();
            var view = main.Controls.OfType<SettingsView>().Single();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var field = (TextBox)typeof(SettingsView).GetField("_musicFormatBox", flags)!.GetValue(view)!;
            Assert.Equal(Theme.Surface, field.BackColor);
            Assert.Equal(Theme.Ink, field.ForeColor);
            var follows = (CheckBox)typeof(SettingsView).GetField("_backgroundFollowThemeCheck", flags)!.GetValue(view)!;
            Assert.True(follows.Checked);
            follows.Checked = false;
            var theme = (ComboBox)typeof(SettingsView).GetField("_themeCombo", flags)!.GetValue(view)!;
            typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", flags)!.Invoke(theme, [EventArgs.Empty]);
            Assert.True(follows.Checked);
        }
        finally
        {
            typeof(Configurations).GetProperty(nameof(Configurations.Settings))!.SetValue(Configurations.Instance, originalConfig);
            Theme.SwitchPalette(originalPalette, scratch);
        }
    }

    [Fact]
    public void NightNumericArrowsPaintDarkWithoutChangingSpinBehavior()
    {
        var original = Theme.Palette.Id;
        using var scratch = new Panel();
        try
        {
            Theme.PreviewPalette(ThemePalettes.NightPrototype, scratch);
            using var number = new CueNumericUpDown { Value = 5, Size = new Size(100, 30) };
            Theme.Apply(number);
            var arrows = number.Controls.Cast<Control>().Single(c => c is not TextBox);
            using var image = new Bitmap(arrows.Width, arrows.Height);
            arrows.DrawToBitmap(image, arrows.ClientRectangle);
            Assert.Equal(Theme.Surface.ToArgb(), image.GetPixel(arrows.Width - 3, 2).ToArgb());
            number.UpButton(); Assert.Equal(6, number.Value);
            number.DownButton(); Assert.Equal(5, number.Value);
        }
        finally { Theme.SwitchPalette(original, scratch); }
    }

    [Fact]
    public void NightScrollbarsPaintDarkAndKeepNativeScrollRange()
    {
        var original = Theme.Palette.Id;
        using var scratch = new Panel();
        try
        {
            Theme.PreviewPalette(ThemePalettes.NightPrototype, scratch);
            using var host = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ClientSize = new Size(300, 200) };
            var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            panel.Controls.Add(new Panel { Size = new Size(500, 800) });
            host.Controls.Add(panel);
            Theme.Apply(panel);
            host.Show();
            panel.AutoScrollPosition = new Point(100, 200);
            Assert.Equal(-200, panel.AutoScrollPosition.Y);
            Assert.Equal(-100, panel.AutoScrollPosition.X);
            using var bitmap = new Bitmap(panel.Width, panel.Height);
            panel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            Assert.Equal(Theme.Canvas.ToArgb(), bitmap.GetPixel(panel.Width - 2, panel.Height - 2).ToArgb());
            panel.AutoScrollPosition = new Point(100, 400);
            Assert.Equal(-400, panel.AutoScrollPosition.Y);
            Theme.SwitchPalette("fog", panel);
            Assert.Equal(-400, panel.AutoScrollPosition.Y);
        }
        finally { Theme.SwitchPalette(original, scratch); }
    }

    [Fact]
    public void LiveThemeSwitchRecolorsExistingRuleRowsAndEditors()
    {
        var config = Configurations.Instance.Settings;
        var palette = Theme.Palette.Id;
        using var scratch = new Panel();
        try
        {
            var fixture = new ConfigData();
            fixture.Apps.Add(new AppRule { ExeName = "enabled.exe", Enabled = true, IsUserConfirmed = true });
            fixture.Apps.Add(new AppRule { ExeName = "disabled.exe", Enabled = false, IsUserConfirmed = true });
            fixture.Apps.Add(new AppRule { ExeName = "suggested.exe", Enabled = true, IsUserConfirmed = false });
            typeof(Configurations).GetProperty(nameof(Configurations.Settings))!.SetValue(Configurations.Instance, fixture);
            Theme.SwitchPalette("fog", scratch);
            using var main = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
            main.Show(); main.ShowSettings();
            var view = main.Controls.OfType<SettingsView>().Single();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var field = (TextBox)typeof(SettingsView).GetField("_musicFormatBox", flags)!.GetValue(view)!;
            var grid = (DataGridView)typeof(SettingsView).GetField("_rulesGrid", flags)!.GetValue(view)!;
            Theme.PreviewPalette(ThemePalettes.NightPrototype, main);
            Assert.Equal(Theme.Ink, field.ForeColor);
            Assert.Equal(Theme.Ink, grid.Rows[0].DefaultCellStyle.ForeColor);
            Assert.Equal(Theme.Muted, grid.Rows[1].DefaultCellStyle.ForeColor);
            Assert.Equal(Theme.Inset, grid.Rows[2].DefaultCellStyle.BackColor);
            ((TabControl)typeof(SettingsView).GetField("_pages", flags)!.GetValue(view)!).SelectedIndex = 3;
            grid.CurrentCell = grid.Rows[0].Cells[2];
            Assert.True(grid.BeginEdit(false));
            Assert.NotNull(grid.EditingControl);
            Assert.Equal(Theme.Ink, grid.EditingControl.ForeColor);
            Assert.Equal(Theme.Surface, grid.EditingControl.BackColor);
            grid.EndEdit();
            grid.Rows[1].Cells[0].Value = true;
            Assert.Equal(Theme.Ink, grid.Rows[1].DefaultCellStyle.ForeColor);
            grid.Rows[2].Cells[2].Value = "confirmed";
            Assert.Equal(Theme.Paper, grid.Rows[2].DefaultCellStyle.BackColor);
            Theme.SwitchPalette("fog", main);
            Assert.Equal(Theme.Ink, field.ForeColor);
            Assert.Equal(Theme.Paper, grid.Rows[2].DefaultCellStyle.BackColor);
        }
        finally
        {
            typeof(Configurations).GetProperty(nameof(Configurations.Settings))!.SetValue(Configurations.Instance, config);
            Theme.SwitchPalette(palette, scratch);
        }
    }
}
