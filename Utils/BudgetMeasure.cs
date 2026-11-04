using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace MuSync.Utils;

/// <summary>已合成输出的 UTF-8 字节预算尺，不改变模板截断规则。</summary>
internal sealed class BudgetMeasure : Control
{
    private int _value;
    private string _feedback = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string Feedback
    {
        get => _feedback;
        set { if (_feedback == value) return; _feedback = value; Invalidate(); }
    }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int Value
    {
        get => _value;
        set { if (_value == value) return; _value = value; Invalidate(); }
    }

    internal BudgetMeasure()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 32;
        Font = Theme.Data;
        AccessibleRole = AccessibleRole.StaticText;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width <= 0 || Height <= 0) return;
        var scale = DeviceDpi / 96f;
        int P(float value) => (int)Math.Round(value * scale);
        var labelWidth = Math.Min(P(160), Width);
        var meterWidth = Math.Max(0, Width - labelWidth - P(16));
        var y = Height / 2;
        using var rule = new Pen(Theme.Rule);
        using var ink = new SolidBrush(_value > 128 ? Theme.OnAir : Theme.Ink);
        e.Graphics.DrawLine(rule, 0, y, meterWidth, y);
        var filled = (int)(Math.Clamp(_value / 128d, 0, 1) * meterWidth);
        if (filled > 0) e.Graphics.FillRectangle(ink, 0, Math.Max(0, y - P(2)), filled, Math.Max(1, P(4)));
        for (var i = 0; i <= 4; i++)
        {
            var x = meterWidth * i / 4;
            e.Graphics.DrawLine(rule, x, y - P(4), x, y + P(4));
        }
        var caption = new Rectangle(Math.Max(0, Width - labelWidth), 0, labelWidth, Height);
        if (_feedback.Length == 0)
        {
            TextRenderer.DrawText(e.Graphics, Text, Theme.Body, caption, Theme.Muted,
                Theme.PaintText | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        TextRenderer.DrawText(e.Graphics, _feedback.Length > 0 ? _feedback : $"{Math.Max(0, _value)} / 128 B", Font,
            caption, _feedback.Length > 0 ? ForeColor : _value > 128 ? Theme.OnAir : Theme.Ink,
            Theme.PaintText | TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
