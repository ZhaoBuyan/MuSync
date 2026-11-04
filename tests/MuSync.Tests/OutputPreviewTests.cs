using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

public class OutputPreviewTests
{
    [Fact]
    public void NormalSyncUsesLocalizedTextAndNormalAccent()
    {
        var state = MainForm.SyncPresentation(true);
        Assert.DoesNotContain("ON AIR", state.Text);
        Assert.Equal(Theme.Accent, state.Color);
        Assert.NotEqual(Theme.OnAir, state.Color);
        Assert.Equal(Theme.Muted, MainForm.SyncPresentation(false).Color);
    }

    private static BudgetMeasure Budget(MainForm form) => (BudgetMeasure)typeof(MainForm)
        .GetField("_outputBudget", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    [Fact]
    public void ContinuousStatusTextReservesEnoughHeightToWrap()
    {
        var settings = Configurations.Instance.Settings;
        var previous = (settings.AppearanceFontFamily, settings.AppearanceFontSize);
        try
        {
            settings.AppearanceFontFamily = "";
            settings.AppearanceFontSize = 0;
            using var form = new MainForm { ClientSize = new Size(620, 430) };
            form.UpdateOutputPreview(new string('W', 128), DateTime.UtcNow);
            var preview = (Label)typeof(MainForm).GetField("_outputPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            Assert.True(preview.Height >= preview.Font.Height * 2);
        }
        finally { (settings.AppearanceFontFamily, settings.AppearanceFontSize) = previous; }
    }

    [Fact]
    public void CustomFontKeepsTheArtistLineTallEnoughForItsText()
    {
        var settings = Configurations.Instance.Settings;
        var previous = (settings.AppearanceFontFamily, settings.AppearanceFontSize);
        try
        {
            settings.AppearanceFontFamily = "Segoe UI";
            settings.AppearanceFontSize = 12;
            using var form = new MainForm();
            var artists = (Label[])typeof(MainForm).GetField("_artistLabels", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            Assert.True(artists[0].Height >= artists[0].Font.Height + artists[0].Padding.Vertical);
        }
        finally { (settings.AppearanceFontFamily, settings.AppearanceFontSize) = previous; }
    }

    [Fact]
    public void CopiesExactPushedTextAndRestoresByteCountAfterFeedbackExpires()
    {
        using var form = new MainForm();
        var now = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
        const string text = "正在听 稻香 — 周杰伦 🎵";
        form.UpdateOutputPreview(text, now);
        string? copied = null;
        Assert.True(form.CopyOutput(value => copied = value, now));
        Assert.Equal(text, copied);
        var budget = Budget(form);
        Assert.Equal(Encoding.UTF8.GetByteCount(text), budget.Value);
        Assert.NotEmpty(budget.Feedback);
        form.UpdateOutputPreview(text, now.AddMilliseconds(1000));
        Assert.NotEmpty(budget.Feedback);
        form.UpdateOutputPreview(text, now.AddMilliseconds(1300));
        Assert.Empty(budget.Feedback);
        Assert.Equal(Encoding.UTF8.GetByteCount(text), budget.Value);
    }

    [Fact]
    public void EmptyOutputDoesNotWriteClipboardAndChangedOutputClearsOldFeedback()
    {
        using var form = new MainForm();
        var now = DateTime.UtcNow;
        Assert.False(form.CopyOutput(_ => throw new Exception("Empty output must not be copied"), now));
        Assert.Empty(Budget(form).Feedback);
        form.UpdateOutputPreview("old", now);
        form.CopyOutput(_ => { }, now);
        form.UpdateOutputPreview("new", now.AddMilliseconds(100));
        Assert.Empty(Budget(form).Feedback);
        Assert.Equal(3, Budget(form).Value);
    }

    [Fact]
    public void ClipboardFailureIsReportedAndCanBeRetried()
    {
        using var form = new MainForm();
        var now = DateTime.UtcNow;
        form.UpdateOutputPreview("example", now);
        Assert.False(form.CopyOutput(_ => throw new ExternalException("Clipboard busy"), now));
        Assert.NotEmpty(Budget(form).Feedback);
        Assert.Equal(Theme.OnAir, Budget(form).ForeColor);
        Assert.True(form.CopyOutput(_ => { }, now.AddMilliseconds(100)));
        Assert.Equal(Theme.Accent, Budget(form).ForeColor);
        Assert.Equal(7, Budget(form).Value);
    }

    [Fact]
    public void OutputChangedDuringClipboardWriteDoesNotReceiveStaleCopiedFeedback()
    {
        using var form = new MainForm();
        var now = DateTime.UtcNow;
        form.UpdateOutputPreview("old", now);
        Assert.True(form.CopyOutput(_ => form.UpdateOutputPreview("new", now), now));
        Assert.Empty(Budget(form).Feedback);
    }
}
