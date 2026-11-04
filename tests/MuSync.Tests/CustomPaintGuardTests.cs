using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>自定义绘制控件的退化尺寸守卫：0 宽 / 0 高时不允许抛异常（v0.4.2 实测崩溃点）。</summary>
public class CustomPaintGuardTests
{
    [Fact]
    public void PolishingFieldsKeepsNumericEditorOwnedByItsSpinner()
    {
        using var panel = new Panel();
        var number = new NumericUpDown { Minimum = 1, Maximum = 50, Value = 5 };
        var text = new TextBox();
        panel.Controls.AddRange([number, text]);
        var editor = number.Controls.OfType<TextBox>().Single();
        Theme.PolishFields(panel);
        Assert.Same(number, editor.Parent);
        Assert.IsType<CueFieldHost>(text.Parent);
        number.UpButton();
        Assert.Equal(6, number.Value);
        number.Value = number.Minimum;
        number.DownButton();
        Assert.Equal(number.Minimum, number.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void BufferedWindowPaint_ClearsOldPixelsOutsideCustomShape(int kind)
    {
        using var parent = new Panel { BackColor = Theme.Sidebar };
        using Control control = kind switch
        {
            0 => new ToggleSwitch(),
            1 => new CueButton(),
            _ => new SidebarBackButton()
        };
        control.Size = new Size(120, 40);
        parent.Controls.Add(control);
        control.CreateControl();
        using var bitmap = new Bitmap(120, 40);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Magenta);
        var hdc = graphics.GetHdc();
        try
        {
            // WM_PAINT 走真实双缓冲路径；DrawToBitmap 的 WM_PRINT 会另外清背景，掩盖残影。
            var message = Message.Create(control.Handle, 0xF, hdc, IntPtr.Zero);
            var arguments = new object[] { message };
            typeof(Control).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(control, arguments);
        }
        finally { graphics.ReleaseHdc(hdc); }
        Assert.Equal(Theme.Sidebar.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void ButtonPaintingKeepsParentTranslationAndClip()
    {
        using var button = new CueButton { Size = new Size(120, 32), Text = "TEST", ForeColor = Color.Black };
        using var bitmap = new Bitmap(240, 120);
        using var graphics = Graphics.FromImage(bitmap);
        var marker = Color.Magenta;
        graphics.Clear(marker);
        graphics.TranslateTransform(80, 40);
        graphics.SetClip(button.ClientRectangle);
        typeof(CueButton).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(button, [new PaintEventArgs(graphics, button.ClientRectangle)]);
        var expected = new Rectangle(80, 40, 120, 32);
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (!expected.Contains(x, y)) Assert.Equal(marker.ToArgb(), bitmap.GetPixel(x, y).ToArgb());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComboBox_PrintDoesNotEraseSiblingControls(bool reverseOrder)
    {
        using var surface = new Panel { Size = new Size(200, 120), BackColor = Theme.Paper };
        var marker = new Panel { Bounds = new Rectangle(8, 8, 48, 24), BackColor = Theme.OnAir };
        var combo = new CueComboBox { Bounds = new Rectangle(8, 56, 140, 28), DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.Add("Example");
        combo.SelectedIndex = 0;
        surface.Controls.AddRange(reverseOrder ? [combo, marker] : [marker, combo]);
        surface.CreateControl();
        using var bitmap = new Bitmap(200, 120);
        surface.DrawToBitmap(bitmap, surface.ClientRectangle);
        Assert.Equal(Theme.OnAir.ToArgb(), bitmap.GetPixel(16, 16).ToArgb());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CueButton_AllFourStraightEdgesHaveOneUniformPixel(bool danger)
    {
        using var button = new CueButton { Size = new Size(120, 32), BackColor = Theme.Paper };
        var edge = danger ? Theme.OnAir : Theme.Rule;
        button.FlatAppearance.BorderColor = edge;
        using var bitmap = new Bitmap(button.Width, button.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Theme.Paper);
        typeof(CueButton).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(button, [new PaintEventArgs(graphics, button.ClientRectangle)]);
        foreach (var point in new[] { new Point(60, 0), new Point(60, 31), new Point(0, 16), new Point(119, 16) })
            Assert.Equal(edge.ToArgb(), bitmap.GetPixel(point.X, point.Y).ToArgb());
        foreach (var point in new[] { new Point(60, 1), new Point(60, 30), new Point(1, 16), new Point(118, 16) })
            Assert.Equal(Theme.Paper.ToArgb(), bitmap.GetPixel(point.X, point.Y).ToArgb());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(64, 32)]
    public void ModernControls_SmallSizesAndStates_DoNotThrow(int width, int height)
    {
        using var toggle = new ToggleSwitch { Size = new Size(width, height) };
        Paint(toggle);
        toggle.Checked = true;
        Paint(toggle);
        toggle.Enabled = false;
        Paint(toggle);
        using var button = new CueButton { Size = new Size(width, height), Text = "Save" };
        Paint(button);
        button.Enabled = false;
        Paint(button);
    }

    [Fact]
    public void ToggleSwitch_ClickRetainsNativeCheckedAndAccessibilityState()
    {
        using var toggle = new ToggleSwitch { AccessibleName = "Sync" };
        var changes = 0;
        toggle.CheckedChanged += (_, _) => changes++;
        var click = typeof(CheckBox).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        click.Invoke(toggle, [EventArgs.Empty]);
        Assert.True(toggle.Checked);
        Assert.True(toggle.AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
        Assert.Equal("Sync", toggle.AccessibilityObject.Name);
        click.Invoke(toggle, [EventArgs.Empty]);
        Assert.False(toggle.Checked);
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 100)]
    public void CueProgressBar_TinySizes_DoNotThrow(int width, int value)
    {
        using var progress = new CueProgressBar { Size = new Size(width, 6), Value = value };
        Paint(progress);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 128)]
    [InlineData(32, 256)]
    public void BudgetMeasure_TinySizesAndOverBudget_DoNotThrow(int width, int value)
    {
        using var measure = new BudgetMeasure { Size = new Size(width, 32), Value = value };
        Paint(measure);
    }

    private static void Paint(Control control)
    {
        using var bitmap = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height));
        using var graphics = Graphics.FromImage(bitmap);
        var args = new PaintEventArgs(graphics, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        var method = control.GetType().GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(control, [args]);   // 抛异常 = 测试失败
    }

    [Fact]
    public void FadingBottomPanel_ZeroWidth_PaintDoesNotThrow()
    {
        using var panel = new FadingBottomPanel { Size = new Size(0, 155) };
        Paint(panel);
    }

    [Fact]
    public void FadingBottomPanel_ZeroHeight_PaintDoesNotThrow()
    {
        using var panel = new FadingBottomPanel { Size = new Size(620, 0) };
        Paint(panel);
    }

    [Fact]
    public void GradientDivider_ZeroSize_PaintDoesNotThrow()
    {
        using var divider = new GradientDivider { Size = new Size(0, 0) };
        Paint(divider);
    }

    [Fact]
    public void FadingButton_ZeroWidth_PaintDoesNotThrow()
    {
        // 「设置」按钮走的就是这条路径：退化尺寸下 PathGradientBrush 不能炸（护住 v0.4.2 那类回归）
        using var button = new FadingButton { Size = new Size(0, 28) };
        Paint(button);
    }

    [Fact]
    public void FadingButton_ZeroHeight_PaintDoesNotThrow()
    {
        using var button = new FadingButton { Size = new Size(48, 0) };
        Paint(button);
    }

    [Fact]
    public void FadingButton_ZeroSizeWithUpdateBadge_PaintDoesNotThrow()
    {
        // 带更新红点时尺寸守卫要先于徽标绘制生效（徽标用的 Width - 13 在 0 宽下会画到负坐标）
        using var button = new FadingButton { Size = new Size(0, 0), ShowUpdateBadge = true };
        Paint(button);
    }

    [Fact]
    public void FadingButton_WithParentZeroSized_PaintDoesNotThrow()
    {
        // DrawParentBackdrop 会取父控件背景与背景图，父控件退化时同样不能抛
        using var parent = new Panel { Size = new Size(0, 0) };
        using var button = new FadingButton { Size = new Size(0, 0) };
        parent.Controls.Add(button);
        Paint(button);
    }

    [Fact]
    public void FadingBottomPanel_ZeroSizeWithBackgroundParent_PaintDoesNotThrow()
    {
        using var parent = new Panel { Size = new Size(0, 0) };
        using var panel = new FadingBottomPanel { Size = new Size(0, 0) };
        parent.Controls.Add(panel);
        Paint(panel);
    }
}
