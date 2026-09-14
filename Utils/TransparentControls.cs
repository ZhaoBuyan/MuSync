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
