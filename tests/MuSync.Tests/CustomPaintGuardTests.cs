using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>自定义绘制控件的退化尺寸守卫：0 宽 / 0 高时不允许抛异常（v0.4.2 实测崩溃点）。</summary>
public class CustomPaintGuardTests
{
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
