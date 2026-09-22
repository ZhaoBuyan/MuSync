using System;
using System.Drawing;
namespace MuSync.Utils;

/// <summary>背景图几何计算：裁剪区归一化 / 像素换算、宽高比与窗口客户区目标尺寸（纯函数，便于测试）。</summary>
internal static class BackgroundGeometry
{
    /// <summary>基准客户区（无背景图时的窗口尺寸）。</summary>
    public static readonly Size BaseClientSize = new(620, 430);

    /// <summary>比例限幅（过窄 / 过宽都收敛到这个范围）。</summary>
    public const float MinAspect = 0.55f;
    public const float MaxAspect = 3.0f;

    /// <summary>客户区宽高限制。</summary>
    public const int MinClientWidth = 560;
    public const int MaxClientWidth = 780;
    public const int MinClientHeight = 430;

    /// <summary>基准宽高比（无图时使用）。</summary>
    public static float BaseAspect => BaseClientSize.Width / (float)BaseClientSize.Height;

    /// <summary>把框选区域（客户区坐标）换算成归一化裁剪区；没画框或框太小则返回 null（= 用整图）。</summary>
    public static double[]? ToNormalizedCrop(Rectangle selection, RectangleF imageRect)
    {
        if (imageRect.Width <= 1 || imageRect.Height <= 1 || selection.IsEmpty) return null;
        var left = Math.Max(selection.Left, imageRect.Left);
        var top = Math.Max(selection.Top, imageRect.Top);
        var right = Math.Min(selection.Right, imageRect.Right);
        var bottom = Math.Min(selection.Bottom, imageRect.Bottom);
        var width = right - left;
        var height = bottom - top;
        if (width < 8 || height < 8) return null;
        return
        [
            Math.Round((left - imageRect.Left) / imageRect.Width, 4),
            Math.Round((top - imageRect.Top) / imageRect.Height, 4),
            Math.Round(width / imageRect.Width, 4),
            Math.Round(height / imageRect.Height, 4)
        ];
    }

    /// <summary>归一化裁剪区 → 像素矩形（已按图幅夹紧）；无裁剪或参数非法时返回整图。</summary>
    public static Rectangle ToPixelCrop(double[]? crop, int imageWidth, int imageHeight)
    {
        var full = new Rectangle(0, 0, Math.Max(1, imageWidth), Math.Max(1, imageHeight));
        if (crop is not { Length: 4 } || imageWidth <= 0 || imageHeight <= 0) return full;
        var x = (int)Math.Round(Math.Clamp(crop[0], 0, 1) * imageWidth);
        var y = (int)Math.Round(Math.Clamp(crop[1], 0, 1) * imageHeight);
        var w = (int)Math.Round(Math.Clamp(crop[2], 0.001, 1) * imageWidth);
        var h = (int)Math.Round(Math.Clamp(crop[3], 0.001, 1) * imageHeight);
        x = Math.Clamp(x, 0, imageWidth - 1);
        y = Math.Clamp(y, 0, imageHeight - 1);
        w = Math.Clamp(w, 1, imageWidth - x);
        h = Math.Clamp(h, 1, imageHeight - y);
        return new Rectangle(x, y, w, h);
    }

    /// <summary>背景图（含裁剪区）的宽高比；参数非法时返回基准比例。</summary>
    public static float Aspect(int imageWidth, int imageHeight, double[]? crop)
    {
        if (imageWidth <= 0 || imageHeight <= 0) return BaseAspect;
        var rect = ToPixelCrop(crop, imageWidth, imageHeight);
        return rect.Width / (float)rect.Height;
    }

    /// <summary>内容区最大高度（屏幕工作区高度减余量，再夹到 430~780）。</summary>
    public static int MaxClientHeight(int workingAreaHeight)
        => Math.Clamp(workingAreaHeight - 150, MinClientHeight, 780);

    /// <summary>
    /// 按背景图比例算目标客户区尺寸：以基准宽 620 起算，高度不足 430 时抬高宽度；
    /// 再按宽 560~780 / 高 430~maxHeight 收敛（比例极端时无法同时满足，优先保住尺寸范围）。
    /// </summary>
    public static Size DesiredClientSize(float aspect, int maxHeight)
    {
        var a = Math.Clamp(aspect, MinAspect, MaxAspect);
        float w = BaseClientSize.Width;
        float h = w / a;
        if (h > maxHeight) { h = maxHeight; w = h * a; }
        else if (h < MinClientHeight) { h = MinClientHeight; w = h * a; }
        if (w > MaxClientWidth) { w = MaxClientWidth; h = w / a; }
        else if (w < MinClientWidth) { w = MinClientWidth; h = w / a; }
        if (h > maxHeight) h = maxHeight;
        if (h < MinClientHeight) h = MinClientHeight;
        return new Size((int)Math.Round(w), (int)Math.Round(h));
    }
}
