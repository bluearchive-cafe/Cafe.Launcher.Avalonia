using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// 解码整窗铺满的背景图：按目标物理像素框钳制输出尺寸，避免超大原图驻留后由
/// 渲染器每帧缩采样到窗口尺寸（大图 UI 卡顿的根因）。小于目标框的图允许放大，
/// 渲染时 Stretch 本就会做同样的缩放，视觉结果不变。
/// </summary>
public static class BackgroundImageDecoder
{
    /// <summary>宽度钳制下限：窗口再小也不把壁纸解码得更小，避免窗口放大后明显模糊。</summary>
    public const int MinDecodeWidthPixels = 1280;

    /// <summary>钳制上限：即便 8K 显示器也限制驻留位图的最大绘制成本。</summary>
    public const int MaxDecodeSidePixels = 4096;

    /// <summary>未附加窗口时（无头测试、启动早期）使用的默认目标框。</summary>
    public static readonly PixelSize FallbackTarget = new(1920, 1080);

    /// <summary>
    /// 计算给定物理尺寸对应的解码目标框。与原 spec（目标边钳制 [1280, 4096]）的
    /// 有意偏差：下限只约束宽度——矮窗口不应把壁纸强行放大到 1280 高；高度仅设上限。
    /// </summary>
    public static PixelSize GetTargetBox(PixelSize targetPhysicalSize)
    {
        return new PixelSize(
            Math.Clamp(targetPhysicalSize.Width, MinDecodeWidthPixels, MaxDecodeSidePixels),
            Math.Clamp(targetPhysicalSize.Height, 1, MaxDecodeSidePixels));
    }

    public static Bitmap Decode(string path, PixelSize targetPhysicalSize)
    {
        using var stream = File.OpenRead(path);
        return Decode(stream, targetPhysicalSize);
    }

    public static Bitmap Decode(Stream stream, PixelSize targetPhysicalSize)
    {
        var target = GetTargetBox(targetPhysicalSize);

        var bitmap = Bitmap.DecodeToWidth(stream, target.Width, BitmapInterpolationMode.HighQuality);
        if (bitmap.PixelSize.Height <= target.Height)
        {
            return bitmap;
        }

        // 极端竖图按宽解码后高度仍超框：对结果二次缩放到钳制框内。
        // 不能复用同一个流做第二次解码——流被首个 codec 消费后再解码的输出尺寸不可靠。
        var heightRatio = target.Height / (double)bitmap.PixelSize.Height;
        var scaled = bitmap.CreateScaledBitmap(
            new PixelSize(
                Math.Max(1, (int)Math.Round(bitmap.PixelSize.Width * heightRatio)),
                target.Height),
            BitmapInterpolationMode.HighQuality);
        bitmap.Dispose();
        return scaled;
    }
}
