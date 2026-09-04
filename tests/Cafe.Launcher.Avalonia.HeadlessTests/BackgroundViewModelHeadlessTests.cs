using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
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
/// 大尺寸自定义背景图回归：背景 ViewModel 必须按窗口目标尺寸加载，且仅在窗口显著
/// 扩大后重解码，避免原图持续缩采样导致卡顿或已解码图片被过度放大导致模糊。
/// </summary>
public sealed class BackgroundViewModelHeadlessTests
{
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
            Assert.True(decoded.PixelSize.Width >= 1300, $"Width {decoded.PixelSize.Width} does not cover 1300.");
            Assert.True(decoded.PixelSize.Height >= 754, $"Height {decoded.PixelSize.Height} does not cover 754.");
            Assert.True(decoded.PixelSize.Width <= BackgroundImageDecoder.MaxDecodeSidePixels);
            Assert.True(decoded.PixelSize.Height <= BackgroundImageDecoder.MaxDecodeSidePixels);
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
            var viewModel = CreateResizeAwareViewModel(context, metrics);
            try
            {
                var settings = CreateCustomBackgroundSettings(wallpaperPath);
                await viewModel.UpdateBackgroundImageAsync(settings, snapshot: null, CancellationToken.None);
                var initial = Assert.IsType<Bitmap>(viewModel.BackgroundImageSource);
                var initialHeight = initial.PixelSize.Height;
                Assert.True(initial.PixelSize.Width >= 1300, $"Initial width {initial.PixelSize.Width} does not cover 1300.");
                Assert.True(initial.PixelSize.Height >= 754, $"Initial height {initial.PixelSize.Height} does not cover 754.");

                // 首次解码发生在小窗口；窗口随后显著变大必须按新目标重解码，
                // 否则驻留位图被放大采样显示为模糊。
                metrics.ResizeTo(new PixelSize(2600, 1500));
                await WaitUntilAsync(
                    () => viewModel.BackgroundImageSource is Bitmap reloaded
                        && reloaded.PixelSize.Height > initialHeight,
                    TimeSpan.FromSeconds(5));

                var reloaded = (Bitmap)viewModel.BackgroundImageSource!;
                Assert.True(reloaded.PixelSize.Width >= 2600, $"Reloaded width {reloaded.PixelSize.Width} does not cover 2600.");
                Assert.True(reloaded.PixelSize.Height >= 1500, $"Reloaded height {reloaded.PixelSize.Height} does not cover 1500.");
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
            var viewModel = CreateResizeAwareViewModel(context, metrics);
            try
            {
                var settings = CreateCustomBackgroundSettings(wallpaperPath);
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
            var viewModel = CreateResizeAwareViewModel(context, metrics);
            try
            {
                var settings = CreateCustomBackgroundSettings(wallpaperPath);
                await viewModel.UpdateBackgroundImageAsync(settings, snapshot: null, CancellationToken.None);
                var initial = Assert.IsType<Bitmap>(viewModel.BackgroundImageSource);
                var initialWidth = initial.PixelSize.Width;
                // 初始目标框很矮：按宽解码已能覆盖该窗口，并保留 1.25× 清晰度余量。
                Assert.Equal(1625, initialWidth);

                // 纯高度增长（宽度不变，DPI 升高同理）也必须触发重解码：只比较宽度会漏检。
                // 注意换图后旧位图会被释放，比较只能用提前捕获的宽度值。
                metrics.ResizeTo(new PixelSize(1300, 1500));
                await WaitUntilAsync(
                    () => viewModel.BackgroundImageSource is Bitmap reloaded
                        && reloaded.PixelSize.Width > initialWidth,
                    TimeSpan.FromSeconds(5));

                var reloaded = (Bitmap)viewModel.BackgroundImageSource!;
                Assert.True(reloaded.PixelSize.Width >= 1300, $"Width {reloaded.PixelSize.Width} does not cover 1300.");
                Assert.True(reloaded.PixelSize.Height >= 1500, $"Height {reloaded.PixelSize.Height} does not cover 1500.");
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
    public async Task UpdateBackgroundImageAsync_WhenAspectFlipsAtEqualArea_ReloadsAtLargerTarget()
    {
        var originalDebounce = BackgroundViewModel.ResizeReloadDebounce;
        BackgroundViewModel.ResizeReloadDebounce = TimeSpan.FromMilliseconds(50);
        try
        {
            using var context = HeadlessTestHost.CreateContext();
            var metrics = new MutableWindowMetrics(new PixelSize(1300, 2000));
            var wallpaperPath = Path.Combine(context.TempDir, "aspect-flip-wallpaper.png");
            HeadlessTestHost.WriteSolidPng(wallpaperPath, Brushes.DarkSlateBlue, 3000, 1600);
            var viewModel = CreateResizeAwareViewModel(context, metrics);
            try
            {
                var settings = CreateCustomBackgroundSettings(wallpaperPath);
                await viewModel.UpdateBackgroundImageAsync(settings, snapshot: null, CancellationToken.None);
                var initial = Assert.IsType<Bitmap>(viewModel.BackgroundImageSource);

                // 竖长→横宽：目标框面积不变，按面积比较会漏检；按维度比较宽度增长
                // 超过阈值，必须触发重解码。
                metrics.ResizeTo(new PixelSize(2000, 1300));
                await WaitUntilAsync(
                    () => !ReferenceEquals(viewModel.BackgroundImageSource, initial),
                    TimeSpan.FromSeconds(5));

                var reloaded = (Bitmap)viewModel.BackgroundImageSource!;
                Assert.True(reloaded.PixelSize.Width >= 2000, $"Reloaded width {reloaded.PixelSize.Width} does not cover 2000.");
                Assert.True(reloaded.PixelSize.Height >= 1300, $"Reloaded height {reloaded.PixelSize.Height} does not cover 1300.");
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
    public async Task UpdateBackgroundImageAsync_WhenWindowShrinksBeforeDecodeFinishes_DecodesAtRequestedTarget()
    {
        using var context = HeadlessTestHost.CreateContext();
        var metrics = new MutableWindowMetrics(new PixelSize(2600, 1500));
        var wallpaperPath = Path.Combine(context.TempDir, "shrink-midflight-wallpaper.png");
        HeadlessTestHost.WriteSolidPng(wallpaperPath, Brushes.DarkSlateBlue, 3000, 1600);
        var decodeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDecode = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestedTargets = new List<PixelSize>();
        var viewModel = new BackgroundViewModel(
            context.Provider.GetRequiredService<ImageCacheService>(),
            context.Provider.GetRequiredService<LocalDiagnostics>(),
            _ => { },
            (path, target) =>
            {
                lock (requestedTargets)
                {
                    requestedTargets.Add(target);
                }

                decodeStarted.TrySetResult();
                Assert.True(releaseDecode.Task.Wait(TimeSpan.FromSeconds(5)), "Timed out waiting to release decode.");
                return BackgroundImageDecoder.Decode(path, target);
            },
            () => null,
            metrics);
        try
        {
            var settings = CreateCustomBackgroundSettings(wallpaperPath);
            var load = viewModel.UpdateBackgroundImageAsync(settings, snapshot: null, CancellationToken.None);
            await decodeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // 解码进行中窗口缩小：解码与 lastDecodeTarget 记录必须共用发起时的
            // 同一尺寸快照，不能改成解码时刻的实时窗口尺寸。
            metrics.ResizeTo(new PixelSize(1300, 754));
            releaseDecode.TrySetResult();
            await load;

            PixelSize requested;
            lock (requestedTargets)
            {
                Assert.Single(requestedTargets);
                requested = requestedTargets[0];
            }

            Assert.Equal(new PixelSize(2600, 1500), requested);
            var decoded = Assert.IsType<Bitmap>(viewModel.BackgroundImageSource);
            Assert.True(decoded.PixelSize.Width >= 2600, $"Width {decoded.PixelSize.Width} does not cover 2600.");
            Assert.True(decoded.PixelSize.Height >= 1500, $"Height {decoded.PixelSize.Height} does not cover 1500.");
        }
        finally
        {
            releaseDecode.TrySetResult();
            viewModel.Dispose();
            File.Delete(wallpaperPath);
        }
    }

    [AvaloniaFact]
    public async Task UpdateBackgroundImageAsync_WhenFolderWallpaperReloadsForResize_ReusesResolvedImage()
    {
        var originalDebounce = BackgroundViewModel.ResizeReloadDebounce;
        BackgroundViewModel.ResizeReloadDebounce = TimeSpan.FromMilliseconds(50);
        try
        {
            using var context = HeadlessTestHost.CreateContext();
            var metrics = new MutableWindowMetrics(new PixelSize(1300, 754));
            var wallpaperFolder = Path.Combine(context.TempDir, "wallpapers");
            Directory.CreateDirectory(wallpaperFolder);
            HeadlessTestHost.WriteSolidPng(
                Path.Combine(wallpaperFolder, "first.png"),
                Brushes.DarkSlateBlue,
                3000,
                1600);
            HeadlessTestHost.WriteSolidPng(
                Path.Combine(wallpaperFolder, "second.png"),
                Brushes.IndianRed,
                3000,
                1600);
            var loadedPaths = new List<string>();
            var viewModel = new BackgroundViewModel(
                context.Provider.GetRequiredService<ImageCacheService>(),
                context.Provider.GetRequiredService<LocalDiagnostics>(),
                _ => { },
                (path, target) =>
                {
                    lock (loadedPaths)
                    {
                        loadedPaths.Add(path);
                    }

                    return BackgroundImageDecoder.Decode(path, target);
                },
                () => null,
                metrics);
            try
            {
                var settings = CreateCustomBackgroundSettings(wallpaperFolder);
                await viewModel.UpdateBackgroundImageAsync(settings, snapshot: null, CancellationToken.None);
                metrics.ResizeTo(new PixelSize(2600, 1500));
                await WaitUntilAsync(
                    () =>
                    {
                        lock (loadedPaths)
                        {
                            return loadedPaths.Count >= 2;
                        }
                    },
                    TimeSpan.FromSeconds(5));

                string[] paths;
                lock (loadedPaths)
                {
                    paths = loadedPaths.ToArray();
                }

                Assert.Equal(paths[0], paths[1]);
            }
            finally
            {
                viewModel.Dispose();
            }
        }
        finally
        {
            BackgroundViewModel.ResizeReloadDebounce = originalDebounce;
        }
    }

    [AvaloniaFact]
    public async Task UpdateBackgroundImageAsync_WhenOlderDecodeFinishesLast_DoesNotReplaceNewerWallpaper()
    {
        using var context = HeadlessTestHost.CreateContext();
        var firstPath = Path.Combine(context.TempDir, "first-wallpaper.png");
        var secondPath = Path.Combine(context.TempDir, "second-wallpaper.png");
        HeadlessTestHost.WriteSolidPng(firstPath, Brushes.DarkSlateBlue, 320, 200);
        HeadlessTestHost.WriteSolidPng(secondPath, Brushes.IndianRed, 320, 200);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new ManualResetEventSlim(initialState: false);
        Bitmap? secondBitmap = null;
        var viewModel = new BackgroundViewModel(
            context.Provider.GetRequiredService<ImageCacheService>(),
            context.Provider.GetRequiredService<LocalDiagnostics>(),
            _ => { },
            (path, target) =>
            {
                if (string.Equals(path, firstPath, StringComparison.Ordinal))
                {
                    firstStarted.TrySetResult();
                    Assert.True(releaseFirst.Wait(TimeSpan.FromSeconds(5)), "Timed out waiting to release first decode.");
                }

                var bitmap = BackgroundImageDecoder.Decode(path, target);
                if (string.Equals(path, secondPath, StringComparison.Ordinal))
                {
                    secondBitmap = bitmap;
                }

                return bitmap;
            },
            () => null);
        try
        {
            var firstSettings = CreateCustomBackgroundSettings(firstPath);
            var secondSettings = CreateCustomBackgroundSettings(secondPath);

            var firstLoad = viewModel.UpdateBackgroundImageAsync(
                firstSettings,
                snapshot: null,
                CancellationToken.None);
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await viewModel.UpdateBackgroundImageAsync(
                secondSettings,
                snapshot: null,
                CancellationToken.None);
            releaseFirst.Set();
            await firstLoad;

            Assert.Same(secondBitmap, viewModel.BackgroundImageSource);
        }
        finally
        {
            releaseFirst.Set();
            releaseFirst.Dispose();
            viewModel.Dispose();
        }
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

    private static LauncherSettings CreateCustomBackgroundSettings(string path) => new()
    {
        BackgroundSource = BackgroundSources.Custom,
        CustomBackgroundPath = path,
        ThemeColorMode = ThemeColorModes.Default
    };

    private static BackgroundViewModel CreateResizeAwareViewModel(
        LauncherHeadlessContext context,
        IWindowMetricsService metrics) =>
        new(
            context.Provider.GetRequiredService<ImageCacheService>(),
            context.Provider.GetRequiredService<LocalDiagnostics>(),
            _ => { },
            (path, target) => BackgroundImageDecoder.Decode(path, target),
            () => null,
            metrics);

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
