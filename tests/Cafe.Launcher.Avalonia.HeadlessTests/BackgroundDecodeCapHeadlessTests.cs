using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Cafe.Launcher.Avalonia.Composition;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary>
/// 大尺寸自定义背景图回归：解码必须钳制到目标物理像素框。原实现按原始分辨率解码，
/// 全窗口 Image 每帧把超大位图缩采样到窗口尺寸，导致 UI 持续卡顿。
/// </summary>
public sealed class BackgroundDecodeCapHeadlessTests
{
    [AvaloniaFact]
    public async Task UpdateBackgroundImageAsync_WhenCustomImageExceedsDecodeTarget_DecodesToTargetBox()
    {
        using var context = CreateBackgroundContext();
        var viewModel = context.ViewModel;

        var wallpaperPath = Path.Combine(context.TempDir, "large-wallpaper.png");
        WriteTestImage(wallpaperPath, 3000, 1600);
        try
        {
            var settings = new LauncherSettings
            {
                BackgroundSource = BackgroundSources.Custom,
                CustomBackgroundPath = wallpaperPath,
                ThemeColorMode = ThemeColorModes.Default
            };
            await viewModel.UpdateBackgroundImageAsync(settings, snapshot: null, CancellationToken.None);

            var decoded = Assert.IsType<Bitmap>(viewModel.BackgroundImageSource);
            var target = BackgroundImageDecoder.FallbackTarget;
            Assert.True(
                decoded.PixelSize.Width <= target.Width,
                $"Decoded width {decoded.PixelSize.Width} exceeds target {target.Width}.");
            Assert.True(
                decoded.PixelSize.Height <= target.Height,
                $"Decoded height {decoded.PixelSize.Height} exceeds target {target.Height}.");
        }
        finally
        {
            File.Delete(wallpaperPath);
        }
    }

    [AvaloniaFact]
    public async Task UpdateBackgroundImageAsync_WhenCustomImageIsSmallerThanTarget_LoadsImage()
    {
        using var context = CreateBackgroundContext();
        var viewModel = context.ViewModel;

        var wallpaperPath = Path.Combine(context.TempDir, "small-wallpaper.png");
        WriteTestImage(wallpaperPath, 640, 480);
        try
        {
            var settings = new LauncherSettings
            {
                BackgroundSource = BackgroundSources.Custom,
                CustomBackgroundPath = wallpaperPath,
                ThemeColorMode = ThemeColorModes.Default
            };
            await viewModel.UpdateBackgroundImageAsync(settings, snapshot: null, CancellationToken.None);

            Assert.IsType<Bitmap>(viewModel.BackgroundImageSource);
        }
        finally
        {
            File.Delete(wallpaperPath);
        }
    }

    [AvaloniaTheory]
    [InlineData(3200, 1800)]
    [InlineData(1080, 2400)]
    [InlineData(640, 480)]
    public void Decode_WhenSourceIsLandscapePortraitOrSmall_OutputFitsTargetBox(int width, int height)
    {
        var imagePath = Path.Combine(
            Path.GetTempPath(),
            $"launcher-decode-{width}x{height}-{Guid.NewGuid():N}.png");
        try
        {
            WriteTestImage(imagePath, width, height);

            using var decoded = BackgroundImageDecoder.Decode(
                imagePath,
                BackgroundImageDecoder.FallbackTarget);

            var target = BackgroundImageDecoder.FallbackTarget;
            Assert.True(
                decoded.PixelSize.Width <= target.Width,
                $"Decoded width {decoded.PixelSize.Width} exceeds target {target.Width} for {width}x{height}.");
            Assert.True(
                decoded.PixelSize.Height <= target.Height,
                $"Decoded height {decoded.PixelSize.Height} exceeds target {target.Height} for {width}x{height}.");
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [AvaloniaFact]
    public async Task UpdateBackgroundImageAsync_WhenMetricsProvideSmallerTarget_DecodesToInjectedTarget()
    {
        using var context = CreateBackgroundContext();
        var wallpaperPath = Path.Combine(context.TempDir, "large-wallpaper.png");
        WriteTestImage(wallpaperPath, 3000, 1600);
        var viewModel = new BackgroundViewModel(
            context.Provider.GetRequiredService<ImageCacheService>(),
            context.Provider.GetRequiredService<LocalDiagnostics>(),
            _ => { },
            new FixedWindowMetrics(new PixelSize(1300, 754)));
        try
        {
            var settings = new LauncherSettings
            {
                BackgroundSource = BackgroundSources.Custom,
                CustomBackgroundPath = wallpaperPath,
                ThemeColorMode = ThemeColorModes.Default
            };
            await viewModel.UpdateBackgroundImageAsync(settings, snapshot: null, CancellationToken.None);

            var decoded = Assert.IsType<Bitmap>(viewModel.BackgroundImageSource);
            Assert.True(decoded.PixelSize.Width <= 1300, $"Width {decoded.PixelSize.Width} exceeds 1300.");
            Assert.True(decoded.PixelSize.Height <= 754, $"Height {decoded.PixelSize.Height} exceeds 754.");
        }
        finally
        {
            File.Delete(wallpaperPath);
        }
    }

    [AvaloniaFact]
    public void WindowMetricsService_WithoutAttachedWindow_ReturnsFallbackAndRecoversAfterDetach()
    {
        var service = new WindowMetricsService();
        Assert.Equal(BackgroundImageDecoder.FallbackTarget, service.GetPhysicalClientSize());

        var window = new Window();
        service.Attach(window);
        var attachedSize = service.GetPhysicalClientSize();
        Assert.True(attachedSize.Width >= 1 && attachedSize.Height >= 1);

        service.Detach(window);
        Assert.Equal(BackgroundImageDecoder.FallbackTarget, service.GetPhysicalClientSize());
    }

    private static BackgroundTestContext CreateBackgroundContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var services = new ServiceCollection();
        services.AddLauncherServices();
        services.AddSingleton(_ => new UnifiedLogger(Path.Combine(tempDir, "logs")));
        var provider = services.BuildServiceProvider();
        return new BackgroundTestContext(tempDir, provider);
    }

    private static void WriteTestImage(string path, int width, int height)
    {
        var border = new Border { Width = width, Height = height, Background = Brushes.DarkSlateBlue };
        border.Measure(new Size(width, height));
        border.Arrange(new Rect(0, 0, width, height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(border);
        using var stream = File.Create(path);
        bitmap.Save(stream, new PngBitmapEncoderOptions());
    }

    private sealed class BackgroundTestContext : IDisposable
    {
        public BackgroundTestContext(string tempDir, ServiceProvider provider)
        {
            TempDir = tempDir;
            Provider = provider;
            ViewModel = provider.GetRequiredService<BackgroundViewModel>();
        }

        public string TempDir { get; }

        public ServiceProvider Provider { get; }

        public BackgroundViewModel ViewModel { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            Provider.Dispose();
        }
    }

    private sealed class FixedWindowMetrics(PixelSize size) : IWindowMetricsService
    {
        public PixelSize GetPhysicalClientSize() => size;
    }
}
