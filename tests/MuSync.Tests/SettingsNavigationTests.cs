using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using MuSync.Models;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

[Collection(LanguageSensitiveCollection.Name)]
public class SettingsNavigationTests
{
    [Fact]
    public void DataSourceTimerDoesNotRepaintHiddenPageValues()
    {
        using var main = new MainForm { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-20000, -20000) };
        main.Show();
        main.ShowSettings();
        var view = main.Controls.OfType<SettingsView>().Single();
        Descendants(view).OfType<TabControl>().Single().SelectedIndex = 4;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Label Field(string name) => (Label)typeof(SettingsView).GetField(name, flags)!.GetValue(view)!;
        var hiddenLabels = new[] { Field("_combinedPreview"), Field("_memoryInfoLabel"), Field("_accountStatusLabel") };
        foreach (var label in hiddenLabels) label.Text = "hidden-page-marker";
        var changes = 0;
        foreach (var label in hiddenLabels) label.TextChanged += (_, _) => changes++;
        foreach (var timerName in new[] { "_perfTimer", "_previewTimer" })
        {
            var timer = (Timer)typeof(SettingsView).GetField(timerName, flags)!.GetValue(view)!;
            typeof(Timer).GetMethod("OnTick", flags)!.Invoke(timer, [EventArgs.Empty]);
        }
        Assert.Equal(0, changes);
        foreach (var label in hiddenLabels) Assert.Equal("hidden-page-marker", label.Text);
    }

    [Fact]
    public void PreviewUsesUnsavedSourcesAndRemovesDisabledBlocks()
    {
        using var form = new SettingsView();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(SettingsView);
        type.GetMethod("AddDataSourceRow", flags)!.Invoke(form,
            [new DataSourceConfig { Name = "ui_unsaved_probe", Enabled = true }]);
        var format = (TextBox)type.GetField("_musicFormatBox", flags)!.GetValue(form)!;
        format.Text = "{ui_unsaved_probe}";
        type.GetMethod("UpdatePreview", flags)!.Invoke(form, null);
        var preview = (Label)type.GetField("_musicPreview", flags)!.GetValue(form)!;
        Assert.Contains("‹ui_unsaved_probe›", preview.Text);
        var rows = (System.Collections.IList)type.GetField("_dataSourceRows", flags)!.GetValue(form)!;
        var row = rows[rows.Count - 1]!;
        var enabled = (CheckBox)row.GetType().GetProperty("Enabled")!.GetValue(row)!;
        enabled.Checked = false;
        type.GetMethod("UpdatePreview", flags)!.Invoke(form, null);
        Assert.DoesNotContain("ui_unsaved_probe", preview.Text);
        Assert.DoesNotContain(Configurations.Instance.Settings.DataSources,
            source => source.Name == "ui_unsaved_probe");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CompactRuleChoicesKeepTheirOriginalMeaning(int language)
    {
        var settings = Configurations.Instance.Settings;
        var original = settings.Language;
        try
        {
            settings.Language = language;
            using var form = new SettingsView();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var rules = (List<AppRule>)typeof(SettingsView).GetField("_rules", flags)!.GetValue(form)!;
            var rule = new AppRule { ExeName = "ui-rule-test.exe" };
            rules.Add(rule);
            typeof(SettingsView).GetMethod("RefreshRulesGrid", flags)!.Invoke(form, null);
            var grid = (DataGridView)typeof(SettingsView).GetField("_rulesGrid", flags)!.GetValue(form)!;
            var row = grid.Rows.Cast<DataGridViewRow>().Single(r => ReferenceEquals(r.Tag, rule));
            var mode = (DataGridViewComboBoxCell)row.Cells[4];
            mode.Value = mode.Items[1];
            Assert.Equal(AppSyncMode.Always, rule.Mode);
            mode.Value = mode.Items[0];
            Assert.Equal(AppSyncMode.Foreground, rule.Mode);
            var policy = (DataGridViewComboBoxCell)row.Cells[5];
            policy.Value = policy.Items[1];
            Assert.Equal(AppSyncOverride.ForceOn, rule.Override);
            policy.Value = policy.Items[2];
            Assert.Equal(AppSyncOverride.ForceOff, rule.Override);
            policy.Value = policy.Items[0];
            Assert.Equal(AppSyncOverride.FollowCategory, rule.Override);
        }
        finally { settings.Language = original; }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SidebarCanReachEveryPageAndActionsFit(int language)
    {
        var settings = Configurations.Instance.Settings;
        var original = settings.Language;
        try
        {
            settings.Language = language;
            using var form = new SettingsView();
            _ = form.Handle;
            form.PerformLayout();
            var tabs = Descendants(form).OfType<TabControl>().Single();
            var navigation = Descendants(form).OfType<ListBox>()
                .Single(c => c.Name == "SettingsNavigation");
            Assert.Equal(8, tabs.TabCount);
            // 页序是产品决定的一部分（"同步配置"与"外观"必须相邻，且外观紧跟其后），
            // 加页/挪页时要连这条一起改，避免导航与内容错位。
            var expectedTitles = language == 0
                ? new[] { "常规", "同步", "同步配置", "外观", "程序", "数据源", "诊断", "关于" }
                : new[] { "General", "Sync", "Status text", "Appearance", "Apps", "Data sources", "Diagnostics", "About" };
            Assert.Equal(expectedTitles, tabs.TabPages.Cast<TabPage>().Select(p => p.Text).ToArray());
            Assert.Equal(tabs.TabCount, navigation.Items.Count);
            for (var i = 0; i < tabs.TabCount; i++)
            {
                navigation.SelectedIndex = i;
                Assert.Equal(i, tabs.SelectedIndex);
                Assert.True(tabs.ClientSize.Width >= 660);
            }
            Assert.NotNull(form.AcceptButton);
            Assert.NotNull(form.CancelButton);
            foreach (var button in Descendants(form).OfType<Button>()
                         .Where(b => b.Parent is FlowLayoutPanel))
            {
                Assert.True(button.Left >= 0);
                Assert.True(button.Right <= button.Parent!.ClientSize.Width);
                Assert.True(button.Bottom <= button.Parent.ClientSize.Height);
            }
        }
        finally { settings.Language = original; }
    }

    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
