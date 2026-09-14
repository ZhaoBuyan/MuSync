using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MuSync.Utils;

/// <summary>支持半透明背景的 Panel（让窗体背景图 / 背景色透出来）。</summary>
internal sealed class TranslucentPanel : Panel
{
    public TranslucentPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    }
}

/// <summary>顶部实心半透明、底边渐隐到透明的面板（音乐 / 程序信息板）。</summary>
internal sealed class FadingBottomPanel : Panel
{
    private readonly int _solidHeight;

    public FadingBottomPanel(int solidHeight = 125)
    {
        _solidHeight = solidHeight;
        SetStyle(
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var solid = Math.Clamp(_solidHeight, 0, Height);
        if (solid > 0)
        {
            using var solidBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
            e.Graphics.FillRectangle(solidBrush, 0, 0, Width, solid);
        }
        if (Height > solid)
        {
            var fadeRect = new Rectangle(0, solid, Width, Height - solid);
            using var fadeBrush = new LinearGradientBrush(
                fadeRect, Color.FromArgb(200, 255, 255, 255), Color.Transparent,
                LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(fadeBrush, fadeRect);
        }
        base.OnPaint(e);
    }
}

/// <summary>无边框按钮：中心半透明 → 边缘完全透明（径向渐变）。</summary>
internal sealed class FadingButton : Button
{
    private bool _hover;

    public FadingButton()
    {
        SetStyle(
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
        BackColor = Color.Transparent;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var rect = ClientRectangle;
        if (rect.Width > 0 && rect.Height > 0)
        {
            using var path = new GraphicsPath();
            path.AddEllipse(rect);
            using var brush = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(_hover ? 220 : 185, 255, 255, 255),
                SurroundColors = [Color.Transparent]
            };
            e.Graphics.FillEllipse(brush, rect);
        }
        TextRenderer.DrawText(
            e.Graphics, Text, Font, rect, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>「半透明 → 透明」的横向渐变分隔线。</summary>
internal sealed class GradientDivider : Control
{
    public GradientDivider()
    {
        SetStyle(
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
        BackColor = Color.Transparent;
        Height = 8;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(110, 255, 255, 255),
            Color.Transparent,
            LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(brush, ClientRectangle);
        base.OnPaint(e);
    }
}
