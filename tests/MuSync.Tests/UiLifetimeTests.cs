using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

[Collection(LanguageSensitiveCollection.Name)]
public class UiLifetimeTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void FirstShowingWithSettingsSelectedDoesNotStartOverviewRefresh()
    {
        using var main = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        main.ShowSettings();
        main.Show();
        Application.DoEvents();
        var timer = (Timer)typeof(MainForm).GetField("_updateTimer", Fields)!.GetValue(main)!;
        Assert.False(timer.Enabled);
        main.ShowOverview();
        Assert.True(timer.Enabled);
        main.Hide();
        Assert.False(timer.Enabled);
    }

    [Fact]
    public void UpdateCheckDoesNotDuplicateOrTouchDisposedViewAfterCompletion()
    {
        using var view = new SettingsView();
        var result = new TaskCompletionSource<UpdateChecker.UpdateInfo?>();
        var calls = 0;
        Task<UpdateChecker.UpdateInfo?> Fetch() { calls++; return result.Task; }
        var first = view.CheckUpdateFromAboutAsync(Fetch);
        var button = (Button)typeof(SettingsView).GetField("_checkUpdateButton", Fields)!.GetValue(view)!;
        Assert.False(button.Enabled);
        Assert.True(view.CheckUpdateFromAboutAsync(Fetch).IsCompletedSuccessfully);
        Assert.Equal(1, calls);
        view.Dispose();
        result.SetResult(null);
        var deadline = Environment.TickCount64 + 3000;
        while (!first.IsCompleted && Environment.TickCount64 < deadline) Application.DoEvents();
        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(button.IsDisposed);
    }

    [Fact]
    public void FormReleasesOwnedImagesButLeavesSharedCacheImageAlive()
    {
        var main = new MainForm();
        var background = new Bitmap(4, 4);
        var icon = new Bitmap(4, 4);
        var placeholder = new Bitmap(4, 4);
        using var shared = new Bitmap(4, 4);
        main.BackgroundImage = background;
        ((PictureBox)typeof(MainForm).GetField("_appIconBox", Fields)!.GetValue(main)!).Image = icon;
        ((Image?[])typeof(MainForm).GetField("_placeholderImages", Fields)!.GetValue(main)!)[0] = placeholder;
        ((PictureBox[])typeof(MainForm).GetField("_coverPictureBoxes", Fields)!.GetValue(main)!)[0].Image = shared;
        var tooltip = (ToolTip)typeof(MainForm).GetField("_songUrlHint", Fields)!.GetValue(main)!;
        var tooltipDisposed = false;
        tooltip.Disposed += (_, _) => tooltipDisposed = true;
        main.Dispose();
        Assert.Throws<ArgumentException>(() => background.Width);
        Assert.Throws<ArgumentException>(() => icon.Width);
        Assert.Throws<ArgumentException>(() => placeholder.Width);
        Assert.Equal(4, shared.Width);
        Assert.True(tooltipDisposed);
    }

    [Fact]
    public void RepeatedViewSwitchingReusesOneViewAndDisposedViewsAreCollectible()
    {
        var references = Enumerable.Range(0, 8).Select(_ => CreateAndDispose()).ToArray();
        for (var i = 0; i < 3; i++)
        {
            Application.DoEvents();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Assert.All(references, reference => Assert.False(reference.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndDispose()
    {
        using var main = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        main.Show();
        Application.DoEvents();
        main.ShowSettings();
        var view = main.Controls.OfType<SettingsView>().Single();
        var count = view.Controls.Count;
        for (var i = 0; i < 25; i++)
        {
            main.ShowOverview(); main.ShowSettings();
            Assert.Same(view, main.Controls.OfType<SettingsView>().Single());
            Assert.Equal(count, view.Controls.Count);
        }
        return new WeakReference(view);
    }
}
