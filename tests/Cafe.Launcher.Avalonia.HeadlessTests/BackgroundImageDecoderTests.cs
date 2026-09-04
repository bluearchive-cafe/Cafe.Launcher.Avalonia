using System;
using System.IO;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Cafe.Launcher.Avalonia.Helpers;
using Xunit;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary>
/// <see cref="BackgroundImageDecoder"/> 的无头回归：解码结果必须覆盖铺满窗口所需的
/// 像素，且不会越过全局单边像素上限。
/// </summary>
public sealed class BackgroundImageDecoderTests
{
    [AvaloniaFact]
    public void GetTargetBox_WhenBelowGlobalCap_AddsQualityHeadroom()
    {
        var target = BackgroundImageDecoder.GetTargetBox(
            BackgroundImageDecoder.FallbackTarget);

        Assert.Equal(new PixelSize(2400, 1350), target);
    }

    [AvaloniaFact]
    public void GetTargetBox_WhenQualityHeadroomWouldExceedGlobalCap_ClampsProportionally()
    {
        var target = BackgroundImageDecoder.GetTargetBox(new PixelSize(3840, 2160));

        Assert.Equal(new PixelSize(4096, 2304), target);
    }

    [AvaloniaTheory]
    [InlineData(3200, 1800)]
    [InlineData(640, 480)]
    public void Decode_WhenSourceCanCoverFallbackTarget_OutputCoversTargetWithinGlobalCap(
        int width,
        int height)
    {
        var imagePath = CreateImage(width, height);
        try
        {
            using var decoded = BackgroundImageDecoder.Decode(
                imagePath,
                BackgroundImageDecoder.FallbackTarget);

            var target = BackgroundImageDecoder.FallbackTarget;
            Assert.True(decoded.PixelSize.Width >= target.Width);
            Assert.True(decoded.PixelSize.Height >= target.Height);
            Assert.True(decoded.PixelSize.Width <= BackgroundImageDecoder.MaxDecodeSidePixels);
            Assert.True(decoded.PixelSize.Height <= BackgroundImageDecoder.MaxDecodeSidePixels);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [AvaloniaFact]
    public void Decode_WhenFourByThreeSourceFillsSixteenByNineViewport_DecodesEnoughHeight()
    {
        var imagePath = CreateImage(1600, 1200);
        try
        {
            using var decoded = BackgroundImageDecoder.Decode(
                imagePath,
                BackgroundImageDecoder.FallbackTarget);

            // UniformToFill 需要 2400×1800 后裁掉上下；1.25× 质量余量避免渲染时二次放大。
            Assert.Equal(2400, decoded.PixelSize.Width);
            Assert.Equal(1800, decoded.PixelSize.Height);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [AvaloniaFact]
    public void Decode_WhenReadingFromStream_ReopensCopiedEncodingForHeightDecode()
    {
        var imagePath = CreateImage(1600, 1200);
        try
        {
            using var stream = File.OpenRead(imagePath);
            using var decoded = BackgroundImageDecoder.Decode(
                stream,
                BackgroundImageDecoder.FallbackTarget);

            Assert.Equal(2400, decoded.PixelSize.Width);
            Assert.Equal(1800, decoded.PixelSize.Height);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [AvaloniaFact]
    public void Decode_WhenExtremePortraitImageExceedsGlobalCap_ClampsLargestSide()
    {
        var imagePath = CreateImage(1080, 2400);
        try
        {
            using var decoded = BackgroundImageDecoder.Decode(
                imagePath,
                BackgroundImageDecoder.FallbackTarget);

            Assert.Equal(BackgroundImageDecoder.MaxDecodeSidePixels, decoded.PixelSize.Height);
            Assert.True(decoded.PixelSize.Width <= BackgroundImageDecoder.MaxDecodeSidePixels);
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
            $"launcher-decode-{width}x{height}-{Guid.NewGuid():N}.png");
        HeadlessTestHost.WriteSolidPng(imagePath, Brushes.DarkSlateBlue, width, height);
        return imagePath;
    }
}
