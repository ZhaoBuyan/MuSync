using System;
using System.ComponentModel;
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

/// <summary>无边框按钮：自绘父级背景 + 中心半透明 → 边缘完全透明的径向渐变。</summary>
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

        using (var path = new GraphicsPath())
        {
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

        if (ShowUpdateBadge)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var badgeBrush = new SolidBrush(Color.FromArgb(233, 74, 62));
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
