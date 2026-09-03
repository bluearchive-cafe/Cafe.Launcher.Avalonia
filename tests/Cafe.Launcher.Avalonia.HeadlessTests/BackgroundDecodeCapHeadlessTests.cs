using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
        using var context = HeadlessTestHost.CreateContext();
        var viewModel = context.ViewModel;

        var wallpaperPath = Path.Combine(context.TempDir, "large-wallpaper.png");
        HeadlessTestHost.WriteSolidPng(wallpaperPath, Brushes.DarkSlateBlue, 3000, 1600);
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
        using var context = HeadlessTestHost.CreateContext();
        var viewModel = context.ViewModel;

        var wallpaperPath = Path.Combine(context.TempDir, "small-wallpaper.png");
        HeadlessTestHost.WriteSolidPng(wallpaperPath, Brushes.DarkSlateBlue, 640, 480);
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
            HeadlessTestHost.WriteSolidPng(imagePath, Brushes.DarkSlateBlue, width, height);

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
        using var context = HeadlessTestHost.CreateContext();
        var wallpaperPath = Path.Combine(context.TempDir, "large-wallpaper.png");
        HeadlessTestHost.WriteSolidPng(wallpaperPath, Brushes.DarkSlateBlue, 3000, 1600);
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
    public async Task UpdateBackgroundImageAsync_WhenWindowGrowsAfterLoad_ReloadsAtLargerTarget()
    {
        var originalDebounce = BackgroundViewModel.ResizeReloadDebounce;
        BackgroundViewModel.ResizeReloadDebounce = TimeSpan.FromMilliseconds(50);
        try
        {
            using var context = HeadlessTestHost.CreateContext();
            var metrics = new MutableWindowMetrics(new PixelSize(1300, 754));
            var wallpaperPath = Path.Combine(context.TempDir, "grow-wallpaper.png");
            HeadlessTestHost.WriteSolidPng(wallpaperPath, Brushes.DarkSlateBlue, 3000, 1600);
            var viewModel = new BackgroundViewModel(
                context.Provider.GetRequiredService<ImageCacheService>(),
                context.Provider.GetRequiredService<LocalDiagnostics>(),
                _ => { },
                path => BackgroundImageDecoder.Decode(path, metrics.GetPhysicalClientSize()),
                () => null,
                metrics);
            try
            {
                var settings = new LauncherSettings
                {
                    BackgroundSource = BackgroundSources.Custom,
                    CustomBackgroundPath = wallpaperPath,
                    ThemeColorMode = ThemeColorModes.Default
                };
                await viewModel.UpdateBackgroundImageAsync(settings, snapshot: null, CancellationToken.None);
                var initial = Assert.IsType<Bitmap>(viewModel.BackgroundImageSource);
                Assert.True(initial.PixelSize.Width <= 1300, $"Initial width {initial.PixelSize.Width} exceeds 1300.");

                // 首次解码发生在小窗口；窗口随后显著变大必须按新目标重解码，
                // 否则驻留位图被放大采样显示为模糊。
                metrics.ResizeTo(new PixelSize(2600, 1500));
                await WaitUntilAsync(
                    () => viewModel.BackgroundImageSource is Bitmap reloaded
                        && reloaded.PixelSize.Width > 1300,
                    TimeSpan.FromSeconds(5));

                var reloaded = (Bitmap)viewModel.BackgroundImageSource!;
                Assert.True(reloaded.PixelSize.Width <= 2600, $"Reloaded width {reloaded.PixelSize.Width} exceeds 2600.");
            }
            finally
            {
                viewModel.Dispose();
                File.Delete(wallpaperPath);
            }
        }
        finally
        {
            BackgroundViewModel.ResizeReloadDebounce = originalDebounce;
        }
    }

    [AvaloniaFact]
    public async Task UpdateBackgroundImageAsync_WhenWindowShrinksAfterLoad_DoesNotReload()
    {
        var originalDebounce = BackgroundViewModel.ResizeReloadDebounce;
        BackgroundViewModel.ResizeReloadDebounce = TimeSpan.FromMilliseconds(50);
        try
        {
            using var context = HeadlessTestHost.CreateContext();
            var metrics = new MutableWindowMetrics(new PixelSize(1300, 754));
            var wallpaperPath = Path.Combine(context.TempDir, "shrink-wallpaper.png");
            HeadlessTestHost.WriteSolidPng(wallpaperPath, Brushes.DarkSlateBlue, 3000, 1600);
            var viewModel = new BackgroundViewModel(
                context.Provider.GetRequiredService<ImageCacheService>(),
                context.Provider.GetRequiredService<LocalDiagnostics>(),
                _ => { },
                path => BackgroundImageDecoder.Decode(path, metrics.GetPhysicalClientSize()),
                () => null,
                metrics);
            try
            {
                var settings = new LauncherSettings
                {
                    BackgroundSource = BackgroundSources.Custom,
                    CustomBackgroundPath = wallpaperPath,
                    ThemeColorMode = ThemeColorModes.Default
                };
                await viewModel.UpdateBackgroundImageAsync(settings, snapshot: null, CancellationToken.None);
                var initial = Assert.IsType<Bitmap>(viewModel.BackgroundImageSource);

                // 窗口变小：现有位图已足够清晰，不应触发重解码。
                metrics.ResizeTo(new PixelSize(1000, 600));
                await Task.Delay(300);

                Assert.Same(initial, viewModel.BackgroundImageSource);
            }
            finally
            {
                viewModel.Dispose();
                File.Delete(wallpaperPath);
            }
        }
        finally
        {
            BackgroundViewModel.ResizeReloadDebounce = originalDebounce;
        }
    }

    [AvaloniaFact]
    public async Task UpdateBackgroundImageAsync_WhenWindowGrowsInHeightOnly_ReloadsAtLargerTarget()
    {
        var originalDebounce = BackgroundViewModel.ResizeReloadDebounce;
        BackgroundViewModel.ResizeReloadDebounce = TimeSpan.FromMilliseconds(50);
        try
        {
            using var context = HeadlessTestHost.CreateContext();
            var metrics = new MutableWindowMetrics(new PixelSize(1300, 400));
            var wallpaperPath = Path.Combine(context.TempDir, "grow-height-wallpaper.png");
            HeadlessTestHost.WriteSolidPng(wallpaperPath, Brushes.DarkSlateBlue, 3000, 1600);
            var viewModel = new BackgroundViewModel(
                context.Provider.GetRequiredService<ImageCacheService>(),
                context.Provider.GetRequiredService<LocalDiagnostics>(),
                _ => { },
                path => BackgroundImageDecoder.Decode(path, metrics.GetPhysicalClientSize()),
                () => null,
                metrics);
            try
            {
                var settings = new LauncherSettings
                {
                    BackgroundSource = BackgroundSources.Custom,
                    CustomBackgroundPath = wallpaperPath,
                    ThemeColorMode = ThemeColorModes.Default
                };
                await viewModel.UpdateBackgroundImageAsync(settings, snapshot: null, CancellationToken.None);
                var initial = Assert.IsType<Bitmap>(viewModel.BackgroundImageSource);
                var initialWidth = initial.PixelSize.Width;
                // 3000×1600 源按宽解码到 1300×693，超高 400 后二次缩放，宽度必然小于 1300。
                Assert.True(
                    initialWidth < 1300,
                    $"Initial width {initialWidth} should be height-capped below 1300.");

                // 纯高度增长（宽度不变，DPI 升高同理）也必须触发重解码：只比较宽度会漏检。
                // 注意换图后旧位图会被释放，比较只能用提前捕获的宽度值。
                metrics.ResizeTo(new PixelSize(1300, 1500));
                await WaitUntilAsync(
                    () => viewModel.BackgroundImageSource is Bitmap reloaded
                        && reloaded.PixelSize.Width > initialWidth,
                    TimeSpan.FromSeconds(5));

                var reloaded = (Bitmap)viewModel.BackgroundImageSource!;
                Assert.Equal(1300, reloaded.PixelSize.Width);
                Assert.True(reloaded.PixelSize.Height <= 1500, $"Height {reloaded.PixelSize.Height} exceeds 1500.");
            }
            finally
            {
                viewModel.Dispose();
                File.Delete(wallpaperPath);
            }
        }
        finally
        {
            BackgroundViewModel.ResizeReloadDebounce = originalDebounce;
        }
    }

    [AvaloniaFact]
    public void WindowMetricsService_WithoutAttachedWindow_ReturnsFallbackAndRecoversAfterDetach()
    {
        var service = new WindowMetricsService();
        Assert.Equal(BackgroundImageDecoder.FallbackTarget, service.GetPhysicalClientSize());

        // 窗口构造后未布局也有默认 ClientSize（headless 下 1024×768）：
        // 快照应反映窗口当前实际物理尺寸（这正是"启动时快照 ≠ 最终窗口"的来源，
        // 由 PhysicalSizeChanged 事件驱动的按需重解码兜底）。
        var window = new Window();
        service.Attach(window);
        var attachedSize = service.GetPhysicalClientSize();
        Assert.Equal(
            (int)Math.Ceiling(window.ClientSize.Width * window.RenderScaling),
            attachedSize.Width);
        Assert.Equal(
            (int)Math.Ceiling(window.ClientSize.Height * window.RenderScaling),
            attachedSize.Height);

        service.Detach(window);
        Assert.Equal(BackgroundImageDecoder.FallbackTarget, service.GetPhysicalClientSize());
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.True(condition(), "Condition was not met within the timeout.");
    }

    private sealed class FixedWindowMetrics(PixelSize size) : IWindowMetricsService
    {
        // 固定尺寸 fake 永不触发尺寸变化。
        public event Action? PhysicalSizeChanged
        {
            add { }
            remove { }
        }

        public PixelSize GetPhysicalClientSize() => size;
    }

    private sealed class MutableWindowMetrics : IWindowMetricsService
    {
        private PixelSize size;

        public MutableWindowMetrics(PixelSize initialSize)
        {
            size = initialSize;
        }

        public event Action? PhysicalSizeChanged;

        public PixelSize GetPhysicalClientSize() => size;

        public void ResizeTo(PixelSize newSize)
        {
            size = newSize;
            PhysicalSizeChanged?.Invoke();
        }
    }
}
