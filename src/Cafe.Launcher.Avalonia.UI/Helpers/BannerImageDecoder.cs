using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// 解码运营横幅图。显示策略是 UniformToFill 全宽、常规尺寸图不在解码端改动，
/// 因此没有壁纸解码的窗口目标框；但仍对驻留位图的最大边设上限——极端大图
/// （如 8K 运营素材）不设上限时解码后驻留可达上百 MB。小于上限的原图按原样
/// 解码，绝不放大（Bitmap.DecodeToWidth 总是缩放到请求宽度，会把小图放大，
/// 故此处以原尺寸解码后再等比缩小）。
/// </summary>
public static class BannerImageDecoder
{
    /// <summary>钳制上限：与壁纸解码共用同一驻留成本预算。</summary>
    public const int MaxDecodeSidePixels = BackgroundImageDecoder.MaxDecodeSidePixels;

    /// <summary>
    /// 从编码字节解码横幅位图，输出任意边不超过 <see cref="MaxDecodeSidePixels"/>、
    /// 保留原始宽高比。
    /// </summary>
    public static Bitmap Decode(byte[] encodedBytes)
    {
        ArgumentNullException.ThrowIfNull(encodedBytes);

        using var stream = new MemoryStream(encodedBytes, writable: false);
        var bitmap = new Bitmap(stream);
        var largestSide = Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        if (largestSide <= MaxDecodeSidePixels)
        {
            return bitmap;
        }

        var scale = MaxDecodeSidePixels / (double)largestSide;
        var scaled = bitmap.CreateScaledBitmap(
            new PixelSize(
                Math.Max(1, (int)Math.Round(bitmap.PixelSize.Width * scale)),
                Math.Max(1, (int)Math.Round(bitmap.PixelSize.Height * scale))),
            BitmapInterpolationMode.HighQuality);
        bitmap.Dispose();
        return scaled;
    }
}
