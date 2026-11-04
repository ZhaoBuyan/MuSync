using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MuSync.Utils;

/// <summary>保留原生数字编辑和长按增减，用主题色绘制箭头区域。</summary>
internal sealed class CueNumericUpDown : NumericUpDown
{
    internal CueNumericUpDown()
    {
        foreach (Control child in Controls)
            if (child is not TextBox) child.Paint += PaintArrows;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // 仅合成这个小型复合控件，避免原生箭头白底先于主题色出现。
            parameters.ExStyle |= 0x02000000;
            return parameters;
        }
    }

    private void PaintArrows(object? sender, PaintEventArgs e)
    {
        if (sender is not Control arrows || arrows.Width < 4 || arrows.Height < 4) return;
        using var fill = new SolidBrush(Enabled ? Theme.Surface : Theme.Canvas);
        e.Graphics.FillRectangle(fill, arrows.ClientRectangle);
        using var rule = new Pen(Theme.Rule);
        e.Graphics.DrawLine(rule, 0, 0, 0, arrows.Height);
        var half = arrows.Height / 2f;
        e.Graphics.DrawLine(rule, 0, half, arrows.Width, half);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var stroke = new Pen(Enabled ? Theme.Ink : Theme.Muted, DeviceDpi / 96f)
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var x = arrows.Width / 2f;
        var r = 2.5f * DeviceDpi / 96f;
        var up = half / 2;
        var down = half + (arrows.Height - half) / 2;
        e.Graphics.DrawLines(stroke, [new PointF(x - r, up + r / 2), new PointF(x, up - r / 2), new PointF(x + r, up + r / 2)]);
        e.Graphics.DrawLines(stroke, [new PointF(x - r, down - r / 2), new PointF(x, down + r / 2), new PointF(x + r, down - r / 2)]);
    }
}
