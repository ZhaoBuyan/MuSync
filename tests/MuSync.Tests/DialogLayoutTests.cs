using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using MuSync;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>裁剪窗口 / 许可窗口的布局守卫：句柄创建（真实客户区确定）后，所有子控件都必须落在客户区内。</summary>
public class DialogLayoutTests
{
    private static string CreateTempImage(int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), $"musync-test-{Guid.NewGuid():N}.png");
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.CornflowerBlue);
        }
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    /// <summary>遍历子控件，断言都在客户区内（越界说明构造期按错误尺寸排的版）。</summary>
    private static void AssertControlsInsideClientArea(Form form, string name)
    {
        Assert.True(form.ClientSize.Width > 0 && form.ClientSize.Height > 0, $"{name} 客户区异常: {form.ClientSize}");
        foreach (Control child in form.Controls)
        {
            var bounds = child.Bounds;
            Assert.True(bounds.Left >= 0 && bounds.Top >= 0, $"{name}/{child.GetType().Name} 坐标越界: {bounds}");
            Assert.True(bounds.Right <= form.ClientSize.Width && bounds.Bottom <= form.ClientSize.Height,
                $"{name}/{child.GetType().Name} 超出客户区: {bounds} vs {form.ClientSize}");
        }
    }

    [Fact]
    public void CropForm_ValidImage_ControlsStayInsideAfterHandleCreation()
    {
        var path = CreateTempImage(800, 400);
        try
        {
            using var form = new BackgroundCropForm(path, null);
            _ = form.Handle; // 句柄创建后客户区才是真实值
            AssertControlsInsideClientArea(form, "BackgroundCropForm");
            Assert.Null(form.ResultCrop); // 没点「确定」前不改结果
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CropForm_WithExistingCrop_ControlsStayInsideAndSelectionRestores()
    {
        var path = CreateTempImage(1000, 500);
        try
        {
            using var form = new BackgroundCropForm(path, [0.25, 0.25, 0.5, 0.5]);
            _ = form.Handle;
            AssertControlsInsideClientArea(form, "BackgroundCropForm(已有裁剪)");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LicenseForm_ControlsStayInsideAfterHandleCreation()
    {
        using var form = new LicenseForm();
        _ = form.Handle;
        AssertControlsInsideClientArea(form, "LicenseForm");
    }

    [Fact]
    public void CropForm_UnsupportedImage_ThrowsSoCallerCanHandleIt()
    {
        // GDI+ 读不了的图（例如 webp）：裁剪窗口构造期就会抛，
        // 设置页必须用 try/catch 兜住并提示用户（见 SettingsForm 的选图流程）。
        var path = Path.Combine(Path.GetTempPath(), $"musync-test-{Guid.NewGuid():N}.webp");
        File.WriteAllText(path, "not an image");
        try
        {
            Assert.ThrowsAny<Exception>(() => new BackgroundCropForm(path, null));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
