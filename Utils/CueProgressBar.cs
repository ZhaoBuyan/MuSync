using System;
using System.Drawing;
using System.Windows.Forms;

namespace MuSync.Utils;

/// <summary>保留原生进度值与辅助功能接口，使用中性量尺绘制。</summary>
internal sealed class CueProgressBar : ProgressBar
{
    internal CueProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 0 || Height <= 0) return;
        e.Graphics.Clear(Theme.Inset);
        var fraction = Math.Clamp((Value - (double)Minimum) / Math.Max(1d, Maximum - (double)Minimum), 0, 1);
        using var ink = new SolidBrush(Theme.Ink);
        e.Graphics.FillRectangle(ink, 0, 0, (int)(fraction * Width), Height);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // 通过 ProgressBar 基类写入 Value，也必须使自绘层失效重画。
        if (m.Msg is 0x402 or 0x403 or 0x406) Invalidate();
    }
}
