using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using MuSync.Utils;
namespace MuSync;

/// <summary>背景图裁剪窗口：在图上拖动画框选择要显示的区域（不画框 = 用整张图）。</summary>
internal class BackgroundCropForm : Form
{
    private readonly Image _image;
    private readonly PictureBox _pictureBox;
    private readonly double[]? _pendingCrop;
    private Rectangle _selection = Rectangle.Empty;
    private bool _dragging;
    private Point _dragStart;

    /// <summary>点「确定」后生效的归一化裁剪区（null = 用整张图）。</summary>
    public double[]? ResultCrop { get; private set; }

    public BackgroundCropForm(string imagePath, double[]? currentCrop)
    {
        Theme.BeginForm(this);
        Text = Loc.L("裁剪背景图", "Crop background image");
        Size = new Size(880, 640);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei", 9f);
        Icon = AppResource.Icon;

        using (var stream = File.OpenRead(imagePath))
        using (var loaded = Image.FromStream(stream))
        {
            _image = new Bitmap(loaded);
        }
        _pendingCrop = currentCrop;

        _pictureBox = new PictureBox
        {
            Location = new Point(16, 16),
            Size = new Size(ClientSize.Width - 32, ClientSize.Height - 112),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = _image,
            BackColor = Color.FromArgb(32, 32, 32),
            Cursor = Cursors.Cross
        };
        _pictureBox.MouseDown += PictureBox_MouseDown;
        _pictureBox.MouseMove += PictureBox_MouseMove;
        _pictureBox.MouseUp += PictureBox_MouseUp;
        _pictureBox.Paint += PictureBox_Paint;

        var hint = new Label
        {
            Location = new Point(16, ClientSize.Height - 88),
            Size = new Size(ClientSize.Width - 32, 24),
            ForeColor = Color.Gray,
            Text = Loc.L("在图上拖动画框选择要显示的区域；不画框 = 用整张图。", "Drag on the image to select the area to show; no selection = use the whole image.")
        };
        var resetButton = new CueButton
        {
            Text = Loc.L("重画", "Reset crop"),
            Location = new Point(ClientSize.Width - 320, ClientSize.Height - 48),
            Size = new Size(96, 32),
            BackColor = Color.White
        };
        var okButton = new CueButton
        {
            Text = Loc.L("确定", "OK"),
            Location = new Point(ClientSize.Width - 112, ClientSize.Height - 48),
            Size = new Size(96, 32),
            BackColor = Color.White
        };
        var cancelButton = new CueButton
        {
            Text = Loc.L("取消", "Cancel"),
            Location = new Point(ClientSize.Width - 216, ClientSize.Height - 48),
            Size = new Size(96, 32),
            BackColor = Color.White,
            DialogResult = DialogResult.Cancel
        };

        resetButton.Click += (_, _) =>
        {
            _selection = Rectangle.Empty;
            _pictureBox.Invalidate();
        };
        okButton.Click += (_, _) =>
        {
            ResultCrop = BackgroundGeometry.ToNormalizedCrop(_selection, ImageRectInBox());
            DialogResult = DialogResult.OK;
        };

        Controls.AddRange([_pictureBox, hint, resetButton, okButton, cancelButton]);
        AcceptButton = okButton;
        CancelButton = cancelButton;
        Font = Theme.Body;
        Theme.Apply(this);
        Theme.PolishFields(this);
        _pictureBox.BackColor = Theme.Inset;
        Theme.Primary(okButton);
        Theme.EndForm(this);

        // 已有裁剪区时显示出来（便于微调）
        Load += (_, _) => RestoreSelection();
    }

    /// <summary>图片在 PictureBox 里的实际显示矩形（Zoom：等比缩放 + 居中）。</summary>
    private RectangleF ImageRectInBox()
    {
        var box = _pictureBox.ClientSize;
        if (box.Width <= 0 || box.Height <= 0 || _image.Width <= 0) return RectangleF.Empty;
        var scale = Math.Min(box.Width / (float)_image.Width, box.Height / (float)_image.Height);
        var width = _image.Width * scale;
        var height = _image.Height * scale;
        return new RectangleF((box.Width - width) / 2f, (box.Height - height) / 2f, width, height);
    }

    private void RestoreSelection()
    {
        if (_pendingCrop is not { Length: 4 } crop) return;
        var imageRect = ImageRectInBox();
        if (imageRect.Width <= 1) return;
        _selection = Rectangle.Round(new RectangleF(
            imageRect.Left + ((float)crop[0] * imageRect.Width),
            imageRect.Top + ((float)crop[1] * imageRect.Height),
            (float)crop[2] * imageRect.Width,
            (float)crop[3] * imageRect.Height));
        _pictureBox.Invalidate();
    }

    private void PictureBox_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var imageRect = ImageRectInBox();
        if (imageRect.IsEmpty || !imageRect.Contains(e.Location)) return;
        _dragging = true;
        _dragStart = e.Location;
        _selection = Rectangle.Empty;
        _pictureBox.Invalidate();
    }

    private void PictureBox_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        _selection = ClampToImage(Rectangle.FromLTRB(
            Math.Min(_dragStart.X, e.X), Math.Min(_dragStart.Y, e.Y),
            Math.Max(_dragStart.X, e.X), Math.Max(_dragStart.Y, e.Y)));
        _pictureBox.Invalidate();
    }

    private void PictureBox_MouseUp(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        if (_selection.Width < 8 || _selection.Height < 8) _selection = Rectangle.Empty;
        _pictureBox.Invalidate();
    }

    private Rectangle ClampToImage(Rectangle rect)
    {
        var imageRect = ImageRectInBox();
        if (imageRect.IsEmpty) return Rectangle.Empty;
        var left = Math.Max(rect.Left, (int)Math.Ceiling(imageRect.Left));
        var top = Math.Max(rect.Top, (int)Math.Ceiling(imageRect.Top));
        var right = Math.Min(rect.Right, (int)Math.Floor(imageRect.Right));
        var bottom = Math.Min(rect.Bottom, (int)Math.Floor(imageRect.Bottom));
        if (right <= left || bottom <= top) return Rectangle.Empty;
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private void PictureBox_Paint(object? sender, PaintEventArgs e)
    {
        var imageRect = ImageRectInBox();
        if (imageRect.IsEmpty) return;
        if (_selection.IsEmpty)
        {
            // 没画框：整图保持原样，只描一圈提示
            using var border = new Pen(Color.FromArgb(90, 255, 255, 255));
            e.Graphics.DrawRectangle(border, imageRect.X, imageRect.Y, imageRect.Width - 1, imageRect.Height - 1);
            return;
        }
        using (var dim = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
        using (var region = new Region(new RectangleF(0, 0, _pictureBox.ClientSize.Width, _pictureBox.ClientSize.Height)))
        {
            region.Exclude(_selection);
            e.Graphics.FillRegion(dim, region);
        }
        using var pen = new Pen(Color.White, 2) { DashStyle = DashStyle.Dash };
        e.Graphics.DrawRectangle(pen, _selection);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pictureBox.Image = null;
            _image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
