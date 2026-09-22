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
}
