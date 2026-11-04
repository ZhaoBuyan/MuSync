using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

[Collection(LanguageSensitiveCollection.Name)]
public class ToggleAnimationTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Click(ToggleSwitch toggle) => typeof(ToggleSwitch).GetMethod("OnClick", Flags)!.Invoke(toggle, [EventArgs.Empty]);
    private static void SetElapsed(ToggleSwitch toggle, double seconds) =>
        typeof(ToggleSwitch).GetField("_motionStarted", Flags)!.SetValue(toggle, Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * seconds));

    [Fact]
    public void LoadingAndProgrammaticChangesNeverCreateAnAnimationTimer()
    {
        using var toggle = new ToggleSwitch();
        for (var i = 0; i < 100; i++) toggle.Checked = !toggle.Checked;
        Assert.False(toggle.IsAnimating);
        Assert.Null(typeof(ToggleSwitch).GetField("_motionTimer", Flags)!.GetValue(toggle));
        Assert.Equal(toggle.Checked ? 1 : 0, toggle.CurrentThumbPosition);
    }

    [Fact]
    public void UserToggleUpdatesBusinessStateImmediatelyAndStopsAtItsEndpoint()
    {
        using var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        using var toggle = new ToggleSwitch();
        form.Controls.Add(toggle); form.Show();
        var changes = 0;
        toggle.CheckedChanged += (_, _) => changes++;
        Click(toggle);
        Assert.True(toggle.Checked);
        Assert.Equal(1, changes);
        if (ToggleSwitch.SystemMotionEnabled)
        {
            Assert.True(toggle.IsAnimating);
            SetElapsed(toggle, .06);
            Assert.InRange(toggle.CurrentThumbPosition, .4f, .99f);
            SetElapsed(toggle, 1);
            var timer = (Timer)typeof(ToggleSwitch).GetField("_motionTimer", Flags)!.GetValue(toggle)!;
            typeof(Timer).GetMethod("OnTick", Flags)!.Invoke(timer, [EventArgs.Empty]);
        }
        Assert.False(toggle.IsAnimating);
        Assert.Equal(1, toggle.CurrentThumbPosition);
        Assert.True(toggle.AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("disabled")]
    [InlineData("disposed")]
    public void HiddenDisabledAndDisposedTogglesLeaveNoRunningAnimation(string action)
    {
        using var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        using var toggle = new ToggleSwitch();
        form.Controls.Add(toggle); form.Show(); Click(toggle);
        switch (action)
        {
            case "hidden":
                form.Hide();
                Assert.False(toggle.Visible);
                // WinForms may defer inherited visibility notifications. The next frame must stop, without painting.
                if (typeof(ToggleSwitch).GetField("_motionTimer", Flags)!.GetValue(toggle) is Timer timer)
                    typeof(Timer).GetMethod("OnTick", Flags)!.Invoke(timer, [EventArgs.Empty]);
                break;
            case "disabled": toggle.Enabled = false; break;
            case "disposed": toggle.Dispose(); break;
        }
        Assert.False(toggle.IsAnimating);
    }

    [Fact]
    public void RapidReversalContinuesFromCurrentPositionWithoutDeferringCheckedState()
    {
        using var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        using var toggle = new ToggleSwitch();
        form.Controls.Add(toggle); form.Show(); Click(toggle);
        if (ToggleSwitch.SystemMotionEnabled)
        {
            SetElapsed(toggle, .06);
            var before = toggle.CurrentThumbPosition;
            Click(toggle);
            Assert.False(toggle.Checked);
            Assert.InRange(toggle.CurrentThumbPosition, before - .15f, before + .15f);
            toggle.Checked = true; // A config reload snaps rather than spawning a second animation.
            Assert.False(toggle.IsAnimating);
            Assert.Equal(1, toggle.CurrentThumbPosition);
        }
        else
        {
            Assert.False(toggle.IsAnimating);
            Click(toggle);
            Assert.False(toggle.Checked);
        }
    }
}
