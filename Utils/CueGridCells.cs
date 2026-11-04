using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MuSync.Utils;

internal sealed class CueGridComboBoxCell : DataGridViewComboBoxCell
{
    public CueGridComboBoxCell() { }
    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex,
        DataGridViewElementStates cellState, object? value, object? formattedValue, string? errorText,
        DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
    {
        base.Paint(graphics, clipBounds, cellBounds, rowIndex, cellState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);
        if (!Theme.Palette.IsDark || (paintParts & DataGridViewPaintParts.ContentForeground) == 0 || DisplayStyle == DataGridViewComboBoxDisplayStyle.Nothing) return;
        var scale = (DataGridView?.DeviceDpi ?? 96) / 96f;
        var width = (int)(18 * scale);
        var area = new Rectangle(cellBounds.Right - width - 1, cellBounds.Top + 1, width, cellBounds.Height - 2);
        if (area.Width <= 0 || area.Height <= 0) return;
        var state = graphics.Save();
        graphics.SetClip(Rectangle.Intersect(clipBounds, area), CombineMode.Intersect);
        var selected = (cellState & DataGridViewElementStates.Selected) != 0;
        using var fill = new SolidBrush(selected ? cellStyle.SelectionBackColor : cellStyle.BackColor);
        graphics.FillRectangle(fill, area);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.Muted, 1.25f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var x = area.Left + area.Width / 2f;
        var y = area.Top + area.Height / 2f;
        graphics.DrawLines(pen, [new PointF(x - 3 * scale, y - scale), new PointF(x, y + 2 * scale), new PointF(x + 3 * scale, y - scale)]);
        graphics.Restore(state);
    }
}

internal sealed class CueGridCheckBoxCell : DataGridViewCheckBoxCell
{
    public CueGridCheckBoxCell() { }
    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex,
        DataGridViewElementStates cellState, object? value, object? formattedValue, string? errorText,
        DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
    {
        base.Paint(graphics, clipBounds, cellBounds, rowIndex, cellState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);
        if (!Theme.Palette.IsDark || (paintParts & DataGridViewPaintParts.ContentForeground) == 0) return;
        var scale = (DataGridView?.DeviceDpi ?? 96) / 96f;
        var side = (int)(16 * scale);
        var box = new Rectangle(cellBounds.Left + (cellBounds.Width - side) / 2, cellBounds.Top + (cellBounds.Height - side) / 2, side, side);
        var state = graphics.Save();
        graphics.SetClip(Rectangle.Intersect(clipBounds, box), CombineMode.Intersect);
        var isChecked = formattedValue is true || formattedValue is CheckState.Checked;
        using var fill = new SolidBrush(isChecked ? Theme.Accent : Theme.Surface);
        using var border = new Pen(isChecked ? Theme.Accent : Theme.Rule);
        graphics.FillRectangle(fill, box);
        graphics.DrawRectangle(border, box.Left, box.Top, box.Width - 1, box.Height - 1);
        if (isChecked)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var check = new Pen(Theme.Paper, 1.6f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            graphics.DrawLines(check, [new PointF(box.Left + 3 * scale, box.Top + 8 * scale), new PointF(box.Left + 6 * scale, box.Top + 11 * scale), new PointF(box.Left + 13 * scale, box.Top + 4 * scale)]);
        }
        graphics.Restore(state);
    }
}
