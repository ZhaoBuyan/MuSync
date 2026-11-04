using System.Linq;
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

public class EmbeddedSettingsTests
{
    [Fact]
    public void SettingsUseOneChildViewAndCancelRestoresOverview()
    {
        using var main = new MainForm();
        _ = main.Handle;
        var originalBounds = main.Bounds;
        var originalTitle = main.Text;
        main.ShowSettings();
        var view = main.Controls.OfType<SettingsView>().Single();
        Assert.Equal(originalBounds.Size, main.Bounds.Size);
        Assert.IsAssignableFrom<UserControl>(view);
        Assert.False((object)view is Form);
        Assert.Same(main, view.Parent);
        Assert.Same(view.AcceptButton, main.AcceptButton);
        main.ShowSettings();
        Assert.Same(view, main.Controls.OfType<SettingsView>().Single());
        // 隐藏的测试宿主不能 PerformClick（原生 CanSelect 会拦截），直接派发同一个 Click 事件。
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(main.CancelButton, [EventArgs.Empty]);
        Assert.False(view.IsDisposed);
        Assert.Same(view, main.Controls.OfType<SettingsView>().Single());
        Assert.Equal(originalBounds.Size, main.Bounds.Size);
        Assert.Equal(originalTitle, main.Text);
        Assert.Null(main.AcceptButton);
        Assert.Null(main.CancelButton);
        var format = (TextBox)typeof(SettingsView).GetField("_musicFormatBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        var savedFormat = Configurations.Instance.Settings.MusicFormat;
        main.ShowSettings();
        format.Text = "cancelled edit";
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(main.CancelButton, [EventArgs.Empty]);
        main.ShowSettings();
        Assert.Same(view, main.Controls.OfType<SettingsView>().Single());
        Assert.Equal(savedFormat, format.Text);
    }

    [Fact]
    public void DisposingHostDisposesEmbeddedSettings()
    {
        var main = new MainForm();
        main.ShowSettings();
        var view = main.Controls.OfType<SettingsView>().Single();
        main.Dispose();
        Assert.True(view.IsDisposed);
    }

    [Fact]
    public void SwitchingViewsKeepsNativeHostVisibleAndSameHandle()
    {
        using var main = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        main.Show();
        var handle = main.Handle;
        var bounds = main.Bounds;
        var destroyed = 0;
        main.HandleDestroyed += (_, _) => destroyed++;
        main.ControlAdded += (_, e) =>
        {
            if (e.Control is SettingsView) Assert.True(IsWindowVisible(handle));
        };
        main.ShowSettings();
        Assert.True(IsWindowVisible(handle));
        Assert.Equal(handle, main.Handle);
        Assert.Equal(bounds, main.Bounds);
        Assert.Empty(main.Controls.OfType<Form>());
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(main.CancelButton, [EventArgs.Empty]);
        Assert.True(IsWindowVisible(handle));
        Assert.Equal(handle, main.Handle);
        Assert.Equal(bounds, main.Bounds);
        Assert.Equal(0, destroyed);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [Fact]
    public void ReturningPreparesOverviewBeforeRemovingSettingsCover()
    {
        using var main = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        main.Show();
        main.ShowSettings();
        var settings = main.Controls.OfType<SettingsView>().Single();
        var heading = (Label)typeof(MainForm).GetField("_outputHeading", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        var restored = false;
        heading.VisibleChanged += (_, _) =>
        {
            if (!heading.Visible) return;
            restored = true;
            Assert.True(settings.Visible);
        };
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(main.CancelButton, [EventArgs.Empty]);
        Assert.True(restored);
        Assert.False(settings.Visible);
        Assert.True(heading.Visible);
    }

    [Fact]
    public void TrayMainWindowReturnsHomeAndPreservesUnsavedSettingsDraft()
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        var mainField = typeof(Program).GetField("_mainForm", statics)!;
        var previous = mainField.GetValue(null);
        using var main = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        try
        {
            mainField.SetValue(null, main);
            main.Show();
            var title = main.Text;
            var handle = main.Handle;
            main.ShowSettings();
            var view = main.Controls.OfType<SettingsView>().Single();
            var input = (TextBox)typeof(SettingsView).GetField("_musicFormatBox", instance)!.GetValue(view)!;
            var saved = Configurations.Instance.Settings.MusicFormat;
            input.Text = "unsaved-tray-navigation";
            main.Hide();
            using var tray = (NotifyIcon)typeof(Program).GetMethod("CreateTrayIcon", statics)!.Invoke(null, null)!;
            using var menu = tray.ContextMenuStrip!;
            var item = menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == Loc.L("主窗口", "Main window"));
            item.PerformClick();
            Assert.True(main.Visible);
            Assert.Equal(handle, main.Handle);
            Assert.Equal(title, main.Text);
            Assert.False(view.Visible);
            Assert.Null(main.CancelButton);
            Assert.Equal(saved, Configurations.Instance.Settings.MusicFormat);
            main.ShowSettings();
            Assert.Same(view, main.Controls.OfType<SettingsView>().Single());
            Assert.Equal("unsaved-tray-navigation", input.Text);
        }
        finally { mainField.SetValue(null, previous); }
    }
}
