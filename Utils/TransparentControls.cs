using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MuSync.Utils;

/// <summary>音乐 / 程序内容面板：无渐变，继承主窗口底色或背景图。</summary>
internal sealed class FadingBottomPanel : Panel
{


    public FadingBottomPanel(int solidHeight = 125)
    {

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
        // 退化尺寸（最小化 / 布局中途）下不绘制：0 宽高的矩形会让 LinearGradientBrush 抛异常
        if (Width <= 0 || Height <= 0) return;
        if (Parent?.BackgroundImage == null)
        {
            using var surface = new SolidBrush(Parent?.BackColor ?? Theme.Paper);
            e.Graphics.FillRectangle(surface, ClientRectangle);
        }
        base.OnPaint(e);
    }
}

/// <summary>设置按钮：继承父级背景，使用轻量圆角与中性悬停色。</summary>
/// <remarks>
/// 不能用框架的「透明背景模拟」——Button 基类在该路径下会渲染出错误的父级背景区域
/// （实测为背景图左下角的采样或纯色块，见 0.4.0 修复记录），因此本控件自行绘制按钮
/// 所在区域的父级背景（背景色 + 背景图四种布局），保证与主界面背景无缝衔接。
/// </remarks>
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

    /// <summary>右上角显示「有新版本」红点（原由外部 Paint 事件绘制，事件在自绘按钮上不会触发）。</summary>
    [DefaultValue(false)]
    public bool ShowUpdateBadge { get; set; }

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

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 背景在 OnPaint 中自绘，避免框架透明模拟渲染出错误区域
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        DrawParentBackdrop(e.Graphics, this);

        if (Width > 2 && Height > 2)
        {
            var state = e.Graphics.Save();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            var radius = 5 * DeviceDpi / 96f;
            using var outer = ControlShape.Rounded(new RectangleF(0, 0, Width, Height), radius);
            using var inner = ControlShape.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), radius - 1);
            if (_hover)
            {
                using var fill = new SolidBrush(Theme.Inset);
                e.Graphics.FillPath(fill, inner);
            }
            outer.FillMode = FillMode.Alternate;
            outer.AddPath(inner, false);
            using var border = new SolidBrush(Theme.Rule);
            e.Graphics.FillPath(border, outer);
            e.Graphics.Restore(state);
        }
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(rect, -4, -4));

        TextRenderer.DrawText(
            e.Graphics, Text, Font, rect, ForeColor,
            Theme.PaintText | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        if (ShowUpdateBadge)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var badgeBrush = new SolidBrush(Theme.OnAir);
            e.Graphics.FillEllipse(badgeBrush, Width - 13, 4, 9, 9);
        }
    }

    /// <summary>把父控件背景的「本按钮所在区域」渲染进按钮画布（复刻 WinForms 背景图四种布局）。</summary>
    private static void DrawParentBackdrop(Graphics g, Control child)
    {
        var parent = child.Parent;
        if (parent is null) return;

        var state = g.Save();
        try
        {
            g.TranslateTransform(-child.Left, -child.Top);
            g.SetClip(new Rectangle(child.Left, child.Top, child.Width, child.Height));

            var client = parent.ClientRectangle;
            using (var brush = new SolidBrush(parent.BackColor))
            {
                g.FillRectangle(brush, client);
            }

            var img = parent.BackgroundImage;
            if (img is null) return;

            switch (parent.BackgroundImageLayout)
            {
                case ImageLayout.Stretch:
                    g.DrawImage(img, client);
                    break;

                case ImageLayout.Center:
                {
                    var dest = new Rectangle(
                        client.X + (client.Width - img.Width) / 2,
                        client.Y + (client.Height - img.Height) / 2,
                        img.Width, img.Height);
                    g.DrawImage(img, dest, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel);
                    break;
                }

                case ImageLayout.Zoom:
                {
                    float widthRatio = client.Width / (float)img.Width;
                    float heightRatio = client.Height / (float)img.Height;
                    float ratio = Math.Min(widthRatio, heightRatio);
                    int zw = Math.Max(1, (int)(img.Width * ratio));
                    int zh = Math.Max(1, (int)(img.Height * ratio));
                    var dest = new Rectangle(
                        client.X + (client.Width - zw) / 2,
                        client.Y + (client.Height - zh) / 2,
                        zw, zh);
                    g.DrawImage(img, dest, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel);
                    break;
                }

                case ImageLayout.Tile:
                    for (int y = client.Y; y < client.Bottom; y += img.Height)
                    {
                        for (int x = client.X; x < client.Right; x += img.Width)
                        {
                            g.DrawImageUnscaled(img, x, y);
                        }
                    }
                    break;

                case ImageLayout.None:
                    g.DrawImageUnscaled(img, client.X, client.Y);
                    break;
            }
        }
        finally
        {
            g.Restore(state);
        }
    }
}

/// <summary>沿用旧控件接口的单像素中性分隔线。</summary>
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
        // 同 FadingBottomPanel：退化尺寸不画（0 宽高会让 LinearGradientBrush 抛异常）
        if (Width <= 0 || Height <= 0) return;
        using var pen = new Pen(Theme.Rule);
        e.Graphics.DrawLine(pen, 0, Height / 2, Width, Height / 2);
        base.OnPaint(e);
    }
}
