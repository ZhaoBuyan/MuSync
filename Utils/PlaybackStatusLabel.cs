using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MuSync.Utils;

internal enum PlaybackGlyph { None, Play, Pause }

/// <summary>文字和矢量图标分别绘制，避免字体回退改变播放/暂停图形。</summary>
internal sealed class PlaybackStatusLabel : Label
{
    private PlaybackGlyph _glyph;

    [DefaultValue(PlaybackGlyph.None)]
    internal PlaybackGlyph Glyph
    {
        get => _glyph;
        set
        {
            if (_glyph == value) return;
            _glyph = value;
            UpdateIconPadding();
            Invalidate();
        }
    }

    private void UpdateIconPadding() => Padding = new Padding(
        _glyph == PlaybackGlyph.None ? 0 : (int)Math.Round(22 * DeviceDpi / 96f), 0, 0, 0);

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateIconPadding();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var size = base.GetPreferredSize(proposedSize);
        if (_glyph != PlaybackGlyph.None) size.Height = Math.Max(size.Height, (int)Math.Round(18 * DeviceDpi / 96f));
        return size;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_glyph == PlaybackGlyph.None || Width <= 0 || Height <= 0) return;
        var side = Math.Min(16 * DeviceDpi / 96f, Height);
        var state = e.Graphics.Save();
        try
        {
            e.Graphics.TranslateTransform(0, (Height - side) / 2);
            e.Graphics.ScaleTransform(side / 24, side / 24);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var stroke = new Pen(Enabled ? ForeColor : Theme.Muted, 2)
            {
                StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round
            };
            // Lucide 0.468.0 的原始 SVG 几何，见 Resources/Icons；直接按当前 DPI 绘制，不缩放位图。
            if (_glyph == PlaybackGlyph.Pause)
            {
                using var left = ControlShape.Rounded(new RectangleF(6, 4, 4, 16), 1);
                using var right = ControlShape.Rounded(new RectangleF(14, 4, 4, 16), 1);
                e.Graphics.DrawPath(stroke, left);
                e.Graphics.DrawPath(stroke, right);
            }
            else
            {
                e.Graphics.DrawPolygon(stroke, [new PointF(6, 3), new PointF(20, 12), new PointF(6, 21)]);
            }
        }
        finally { e.Graphics.Restore(state); }
    }
}
