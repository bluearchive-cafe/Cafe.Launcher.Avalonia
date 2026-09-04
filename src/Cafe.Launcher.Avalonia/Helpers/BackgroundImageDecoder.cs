using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// 解码整窗铺满的背景图：按目标物理像素框和图片宽高比选择覆盖窗口所需的输出尺寸，
/// 避免超大原图驻留后由渲染器每帧缩采样到窗口尺寸（大图 UI 卡顿的根因）。
/// 输出的任意边仍不会超过 <see cref="MaxDecodeSidePixels"/>；极端宽高比在该上限下
/// 无法完全覆盖窗口时，宁可保留上限以控制长期渲染成本。
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

    /// <summary>
    /// 从文件解码壁纸。输出保留原始宽高比，并在全屏铺满时至少覆盖目标框的一边；
    /// 两次解码需要重新打开文件流，避免复用已被 codec 消费的流。
    /// </summary>
    /// <param name="path">图片文件的完整路径。</param>
    /// <param name="targetPhysicalSize">窗口的物理客户区尺寸。</param>
    /// <returns>受全局像素上限约束的解码位图。</returns>
    public static Bitmap Decode(string path, PixelSize targetPhysicalSize) =>
        Decode(static filePath => File.OpenRead(filePath), path, targetPhysicalSize);

    /// <summary>
    /// 从流解码壁纸。方法会从流的当前位置复制编码数据，以便在按宽解码不足以覆盖目标
    /// 高度时使用一条独立流按高重新解码；调用方仍负责传入流的生命周期。
    /// </summary>
    /// <param name="stream">位于图片编码数据起点的可读流。</param>
    /// <param name="targetPhysicalSize">窗口的物理客户区尺寸。</param>
    /// <returns>受全局像素上限约束的解码位图。</returns>
    public static Bitmap Decode(Stream stream, PixelSize targetPhysicalSize)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var encoded = new MemoryStream();
        stream.CopyTo(encoded);
        var bytes = encoded.ToArray();
        return Decode(static data => new MemoryStream(data, writable: false), bytes, targetPhysicalSize);
    }

    private static Bitmap Decode<TState>(
        Func<TState, Stream> openStream,
        TState state,
        PixelSize targetPhysicalSize)
    {
        var target = GetTargetBox(targetPhysicalSize);

        using var widthStream = openStream(state);
        var widthDecoded = Bitmap.DecodeToWidth(
            widthStream,
            target.Width,
            BitmapInterpolationMode.HighQuality);
        if (widthDecoded.PixelSize.Height >= target.Height)
        {
            return ClampLargestSide(widthDecoded);
        }

        widthDecoded.Dispose();
        using var heightStream = openStream(state);
        var heightDecoded = Bitmap.DecodeToHeight(
            heightStream,
            target.Height,
            BitmapInterpolationMode.HighQuality);
        return ClampLargestSide(heightDecoded);
    }

    private static Bitmap ClampLargestSide(Bitmap bitmap)
    {
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
