using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MuSync.Utils;

internal static class ControlShape
{
    internal static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0) return path;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>单行输入的外壳；文字编辑、选择、输入法仍由内部 TextBox 负责。</summary>
internal sealed class CueFieldHost : Panel
{
    private readonly TextBox _field;

    internal CueFieldHost(TextBox field)
    {
        _field = field;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        var parent = field.Parent!;
        var index = parent.Controls.GetChildIndex(field);
        var form = field.FindForm();
        var layoutScale = form?.AutoScaleMode == AutoScaleMode.Dpi && form.AutoScaleDimensions.Width >= field.DeviceDpi
            ? field.DeviceDpi / 96f : 1f;
        Bounds = new Rectangle(field.Location, new Size(field.Width, Math.Max((int)Math.Round(28 * layoutScale), field.Height)));
        TabIndex = field.TabIndex;
        TabStop = false;
        Anchor = field.Anchor;
        field.BorderStyle = BorderStyle.None;
        field.BackColor = field.Enabled ? Theme.Surface : Theme.Canvas;
        field.ForeColor = field.Enabled ? Theme.Ink : Theme.Muted;
        Controls.Add(field);
        field.GotFocus += FieldFocusChanged;
        field.LostFocus += FieldFocusChanged;
        field.EnabledChanged += FieldEnabledChanged;
        parent.Controls.Add(this);
        parent.Controls.SetChildIndex(this, index);
        PositionField();
    }

    private void FieldFocusChanged(object? sender, EventArgs e) => Invalidate();
    private void FieldEnabledChanged(object? sender, EventArgs e)
    {
        _field.BackColor = _field.Enabled ? Theme.Surface : Theme.Canvas;
        _field.ForeColor = _field.Enabled ? Theme.Ink : Theme.Muted;
        Invalidate();
    }

    private void PositionField()
    {
        var inset = Math.Max(4, (int)(8 * DeviceDpi / 96f));
        _field.Location = new Point(inset, Math.Max(0, (Height - _field.PreferredHeight) / 2));
        _field.Width = Math.Max(1, Width - 2 * inset);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); if (_field != null) PositionField(); }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_field != null) PositionField();
    }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _field.Focus(); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var background = new SolidBrush(Parent?.BackColor ?? Theme.Paper);
        e.Graphics.FillRectangle(background, ClientRectangle);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 3 || Height < 3) return;
        var previous = e.Graphics.SmoothingMode;
        var offset = e.Graphics.PixelOffsetMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        var radius = 5 * DeviceDpi / 96f;
        using var outer = ControlShape.Rounded(new RectangleF(0, 0, Width, Height), radius);
        using var inner = ControlShape.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), radius - 1);
        using var border = new SolidBrush(_field.Focused ? Theme.Accent : Theme.Rule);
        using var fill = new SolidBrush(_field.Enabled ? Theme.Surface : Theme.Canvas);
        e.Graphics.FillPath(border, outer);
        e.Graphics.FillPath(fill, inner);
        e.Graphics.SmoothingMode = previous;
        e.Graphics.PixelOffsetMode = offset;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _field.GotFocus -= FieldFocusChanged;
            _field.LostFocus -= FieldFocusChanged;
            _field.EnabledChanged -= FieldEnabledChanged;
        }
        base.Dispose(disposing);
    }
}

/// <summary>保留 CheckBox 的切换、键盘与辅助功能语义，绘制无持续动画的拨杆。</summary>
internal sealed partial class ToggleSwitch : CheckBox
{
    internal ToggleSwitch()
    {
        // ButtonBase 默认 Opaque；自绘拨杆依赖 OnPaintBackground 清理整个缓冲区。
        SetStyle(ControlStyles.Opaque, false);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Size = new Size(64, 32);
        AccessibleRole = AccessibleRole.CheckButton;
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        var from = CurrentThumbPosition;
        base.OnCheckedChanged(e);
        ChangeMotionTarget(from);
    }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); if (!Enabled) FinishMotion(); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var background = new SolidBrush(Parent?.BackColor ?? Theme.Paper);
        e.Graphics.FillRectangle(background, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 12 || Height < 12) return;
        var scale = DeviceDpi / 96f;
        var width = Math.Min(48 * scale, Width - 8 * scale);
        var height = Math.Min(24 * scale, Height - 8 * scale);
        if (width <= 4 || height <= 4) return;
        var track = new RectangleF((Width - width) / 2, (Height - height) / 2, width, height);
        var previous = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = ControlShape.Rounded(track, height / 2);
        var position = CurrentThumbPosition;
        using var fill = new SolidBrush(!Enabled ? Theme.Inset : BlendTrack(Theme.Rule, Theme.Accent, position));
        e.Graphics.FillPath(fill, path);
        var inset = 2 * scale;
        var diameter = height - 2 * inset;
        using var thumb = new SolidBrush(Enabled ? Color.White : Theme.Paper);
        e.Graphics.FillEllipse(thumb, track.Left + inset + (track.Width - 2 * inset - diameter) * position,
            track.Top + inset, diameter, diameter);
        e.Graphics.SmoothingMode = previous;
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(Rectangle.Ceiling(track), 3, 3), Theme.Ink, BackColor);
    }
}

/// <summary>轻量按钮外观；点击、快捷键、DialogResult 等仍由原生 Button 处理。</summary>
internal sealed class CueButton : Button
{
    private bool _hover;
    private bool _pressed;

    internal CueButton()
    {
        SetStyle(ControlStyles.Opaque, false);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = _pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _pressed = e.Button == MouseButtons.Left; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _pressed = false; Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) { _pressed = true; Invalidate(); } }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode == Keys.Space) { _pressed = false; Invalidate(); } }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var background = new SolidBrush(Parent?.BackColor ?? Theme.Paper);
        e.Graphics.FillRectangle(background, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        var background = !Enabled ? Theme.Canvas : _pressed ? FlatAppearance.MouseDownBackColor
            : _hover ? FlatAppearance.MouseOverBackColor : BackColor;
        var previous = e.Graphics.SmoothingMode;
        var previousOffset = e.Graphics.PixelOffsetMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        var radius = 5 * DeviceDpi / 96f;
        using var outer = ControlShape.Rounded(new RectangleF(0, 0, Width, Height), radius);
        using var inner = ControlShape.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), Math.Max(0, radius - 1));
        using var fill = new SolidBrush(background);
        using var border = new SolidBrush(Focused && ShowFocusCues ? Theme.Accent : FlatAppearance.BorderColor);
        // 内外轮廓按整数像素内缩，避免中心描边在不同边缘产生半像素厚度差。
        e.Graphics.FillPath(border, outer);
        if (Width > 2 && Height > 2) e.Graphics.FillPath(fill, inner);
        e.Graphics.SmoothingMode = previous;
        e.Graphics.PixelOffsetMode = previousOffset;
        var flags = Theme.PaintText | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;
        else if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Inflate(ClientRectangle, Width < 48 ? -2 : -8, -2),
            Enabled ? ForeColor : Theme.Muted, flags);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), ForeColor, background);
    }
}

/// <summary>侧栏返回入口，保留 Button 的点击与键盘行为。</summary>
internal sealed class SidebarBackButton : Button
{
    private bool _hover;
    private bool _pressed;

    internal SidebarBackButton()
    {
        SetStyle(ControlStyles.Opaque, false);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = _pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _pressed = e.Button == MouseButtons.Left; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _pressed = false; Invalidate(); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var background = new SolidBrush(Parent?.BackColor ?? Theme.Sidebar);
        e.Graphics.FillRectangle(background, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 32 || Height < 8) return;
        var state = e.Graphics.Save();
        var scale = DeviceDpi / 96f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (_hover || (Focused && ShowFocusCues))
        {
            using var path = ControlShape.Rounded(new RectangleF(0, 0, Width, Height), 5 * scale);
            using var fill = new SolidBrush(_pressed ? Theme.Accent : Theme.SidebarSelected);
            e.Graphics.FillPath(fill, path);
        }
        var color = _hover || Focused ? Theme.SidebarText : Theme.SidebarMuted;
        var y = Height / 2f;
        using var arrow = new Pen(color, 1.5f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        e.Graphics.DrawLines(arrow, [new PointF(13 * scale, y - 5 * scale), new PointF(8 * scale, y), new PointF(13 * scale, y + 5 * scale)]);
        e.Graphics.DrawLine(arrow, 8 * scale, y, 22 * scale, y);
        e.Graphics.Restore(state);
        var inset = (int)(32 * scale);
        TextRenderer.DrawText(e.Graphics, Text, Font,
            new Rectangle(inset, 0, Math.Max(1, Width - inset - 4), Height), color,
            Theme.PaintText | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3), Theme.SidebarText, BackColor);
    }
}

/// <summary>保留系统下拉列表及键盘操作，仅覆盖折叠态边线和箭头。</summary>
internal sealed class CueComboBox : ComboBox
{
    internal CueComboBox()
    {
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = Math.Max(20, Font.Height + 4);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        ItemHeight = Math.Max((int)(20 * DeviceDpi / 96f), Font.Height + 4);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;
        var collapsed = (e.State & DrawItemState.ComboBoxEdit) != 0;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var fill = new SolidBrush(!Enabled ? Theme.Canvas : selected && !collapsed ? Theme.Inset : Theme.Surface);
        e.Graphics.FillRectangle(fill, e.Bounds);
        if (e.Index < 0 || e.Index >= Items.Count) return;
        var bounds = Rectangle.Inflate(e.Bounds, -8, 0);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, bounds,
            Enabled ? Theme.Ink : Theme.Muted,
            Theme.PaintText | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnDropDownClosed(EventArgs e) { base.OnDropDownClosed(e); Invalidate(); }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is not (0xF or 0x317 or 0x318) || !IsHandleCreated || Width < 24 || Height < 4) return;
        using var graphics = m.Msg == 0xF ? Graphics.FromHwnd(Handle) : Graphics.FromHdc(m.WParam);
        using var fill = new SolidBrush(Enabled ? BackColor : Theme.Canvas);
        var arrowWidth = SystemInformation.VerticalScrollBarWidth;
        var borderColor = Focused ? Theme.Accent : Theme.Rule;
        if (DropDownStyle == ComboBoxStyle.DropDownList)
        {
            // WM_PRINT 使用父级共享 HDC；Clear 会越过控件边界擦掉已画好的兄弟控件。
            using var paper = new SolidBrush(Parent?.BackColor ?? Theme.Paper);
            graphics.FillRectangle(paper, ClientRectangle);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            var radius = 5 * DeviceDpi / 96f;
            using var outer = ControlShape.Rounded(new RectangleF(0, 0, Width, Height), radius);
            using var inner = ControlShape.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), radius - 1);
            using var outline = new SolidBrush(borderColor);
            graphics.FillPath(outline, outer);
            graphics.FillPath(fill, inner);
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.PixelOffsetMode = PixelOffsetMode.Default;
            TextRenderer.DrawText(graphics, Text, Font, new Rectangle(8, 0, Math.Max(1, Width - arrowWidth - 16), Height),
                Enabled ? Theme.Ink : Theme.Muted,
                Theme.PaintText | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
        else
        {
            graphics.FillRectangle(fill, Width - arrowWidth - 1, 1, arrowWidth, Height - 2);
            using var border = new Pen(borderColor);
            graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var arrow = new Pen(Theme.Muted, 1.5f);
        var x = Width - arrowWidth / 2f - 1;
        var y = Height / 2f;
        graphics.DrawLines(arrow, [new PointF(x - 3, y - 1), new PointF(x, y + 2), new PointF(x + 3, y - 1)]);
    }
}
