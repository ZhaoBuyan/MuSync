using System;
using System.Drawing;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>背景图几何测试：裁剪区换算、宽高比与窗口尺寸收敛。</summary>
public class BackgroundGeometryTests
{
    [Fact]
    public void ToNormalizedCrop_MapsSelectionToFractions()
    {
        var crop = BackgroundGeometry.ToNormalizedCrop(new Rectangle(50, 50, 100, 50), new RectangleF(0, 0, 200, 100));
        Assert.NotNull(crop);
        Assert.Equal([0.25, 0.5, 0.5, 0.5], crop!);
    }

    [Fact]
    public void ToNormalizedCrop_TinySelection_TreatedAsNone()
    {
        Assert.Null(BackgroundGeometry.ToNormalizedCrop(new Rectangle(10, 10, 4, 4), new RectangleF(0, 0, 200, 100)));
        Assert.Null(BackgroundGeometry.ToNormalizedCrop(Rectangle.Empty, new RectangleF(0, 0, 200, 100)));
    }

    [Fact]
    public void ToNormalizedCrop_SelectionOutsideImage_IsClamped()
    {
        var crop = BackgroundGeometry.ToNormalizedCrop(new Rectangle(-50, -50, 150, 150), new RectangleF(0, 0, 100, 100));
        Assert.NotNull(crop);
        Assert.Equal([0.0, 0.0, 1.0, 1.0], crop!);
    }

    [Fact]
    public void ToPixelCrop_HalfRect()
    {
        var rect = BackgroundGeometry.ToPixelCrop([0.25, 0.25, 0.5, 0.5], 800, 400);
        Assert.Equal(new Rectangle(200, 100, 400, 200), rect);
    }

    [Fact]
    public void ToPixelCrop_NullOrBadCrop_ReturnsFullImage()
    {
        Assert.Equal(new Rectangle(0, 0, 800, 400), BackgroundGeometry.ToPixelCrop(null, 800, 400));
        Assert.Equal(new Rectangle(0, 0, 800, 400), BackgroundGeometry.ToPixelCrop([0.5], 800, 400));
    }

    [Fact]
    public void Aspect_UsesCropWhenPresent()
    {
        Assert.Equal(2f, BackgroundGeometry.Aspect(1000, 500, null));
        // 裁掉左右各 1/4：剩 500×500 → 1.0
        Assert.Equal(1f, BackgroundGeometry.Aspect(1000, 500, [0.25, 0, 0.5, 1.0]));
    }

    [Fact]
    public void DesiredClientSize_BaseAspect_KeepsBaseSize()
    {
        var size = BackgroundGeometry.DesiredClientSize(BackgroundGeometry.BaseAspect, 780);
        Assert.Equal(620, size.Width);
        Assert.Equal(430, size.Height);
    }

    [Fact]
    public void DesiredClientSize_SquareImage_UsesHeightLimit()
    {
        // 1:1 → 620×620（在限制内，直接按比例）
        var size = BackgroundGeometry.DesiredClientSize(1f, 780);
        Assert.Equal(620, size.Width);
        Assert.Equal(620, size.Height);
    }

    [Fact]
    public void DesiredClientSize_WideImage_ClampedToWidthRange()
    {
        // 2:1 → 620×310 高度不足 → 抬到 430 高（宽 860 超上限）→ 收到 780 宽再贴回 430 高
        var size = BackgroundGeometry.DesiredClientSize(2f, 780);
        Assert.Equal(780, size.Width);
        Assert.Equal(430, size.Height);
    }

    [Fact]
    public void DesiredClientSize_TallImage_ClampedToHeightLimit()
    {
        // 极窄（钳到 0.55）→ 高超限 → 收到屏幕限高，宽不低过 560
        var size = BackgroundGeometry.DesiredClientSize(0.3f, 780);
        Assert.Equal(560, size.Width);
        Assert.Equal(780, size.Height);
    }

    [Fact]
    public void DesiredClientSize_SmallScreenRespectsMaxHeight()
    {
        var size = BackgroundGeometry.DesiredClientSize(1f, 500);
        Assert.Equal(560, size.Width);
        Assert.Equal(500, size.Height);
    }

    [Fact]
    public void MaxClientHeight_ClampedToRange()
    {
        Assert.Equal(430, BackgroundGeometry.MaxClientHeight(400));
        Assert.Equal(780, BackgroundGeometry.MaxClientHeight(2000));
        Assert.Equal(780, BackgroundGeometry.MaxClientHeight(1080));
    }
}
