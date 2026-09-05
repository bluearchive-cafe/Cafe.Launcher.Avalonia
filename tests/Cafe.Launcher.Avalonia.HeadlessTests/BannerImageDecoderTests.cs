using System;
using System.IO;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Cafe.Launcher.Avalonia.Helpers;
using Xunit;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary>
/// <see cref="BannerImageDecoder"/> 的无头回归：常规横幅按原样解码（不放大），
/// 超大图任意边不越过驻留上限并保留宽高比。
/// </summary>
public sealed class BannerImageDecoderTests
{
    [AvaloniaTheory]
    [InlineData(4400, 2500, 4096, 2327)]
    [InlineData(2500, 4400, 2327, 4096)]
    public void Decode_WhenSourceExceedsGlobalCap_ClampsLargestSidePreservingAspect(
        int sourceWidth,
        int sourceHeight,
        int expectedWidth,
        int expectedHeight)
    {
        var imagePath = CreateImage(sourceWidth, sourceHeight);
        try
        {
            using var decoded = BannerImageDecoder.Decode(File.ReadAllBytes(imagePath));

            Assert.Equal(BannerImageDecoder.MaxDecodeSidePixels, Math.Max(
                decoded.PixelSize.Width,
                decoded.PixelSize.Height));
            // 编码/采样存在 ±1 像素舍入，钳制边之外按比例容差断言。
            Assert.True(Math.Abs(decoded.PixelSize.Width - expectedWidth) <= 1);
            Assert.True(Math.Abs(decoded.PixelSize.Height - expectedHeight) <= 1);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [AvaloniaFact]
    public void Decode_WhenSourceBelowGlobalCap_KeepsIntrinsicSizeWithoutUpscaling()
    {
        var imagePath = CreateImage(800, 450);
        try
        {
            using var decoded = BannerImageDecoder.Decode(File.ReadAllBytes(imagePath));

            Assert.Equal(800, decoded.PixelSize.Width);
            Assert.Equal(450, decoded.PixelSize.Height);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    private static string CreateImage(int width, int height)
    {
        var imagePath = Path.Combine(
            Path.GetTempPath(),
            $"banner-decode-{width}x{height}-{Guid.NewGuid():N}.png");
        HeadlessTestHost.WriteSolidPng(imagePath, Brushes.DarkSlateBlue, width, height);
        return imagePath;
    }
}
