using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Cafe.Launcher.UI.Models;
using Xunit;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.Features.Settings;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.HeadlessTests;

/// <summary>
/// 壁纸主题取色异步化回归：提取在后台线程执行，palette 经代数校验后落定；
/// 陈旧任务（壁纸已再次切换）不得覆盖较新的色板决策。
/// </summary>
public sealed class ThemeColorExtractionAsyncHeadlessTests
{
    [AvaloniaTheory]
    [InlineData("red-video-blue-cover.mp4", true)]
    [InlineData("red-video-blue-jpeg-cover.mp4", true)]
    [InlineData("first-frame.mp4", false)]
    public async Task UpdateBackgroundImageAsync_WhenVideoSelected_ExtractsCoverOrFirstFrame(string fileName, bool hasCover)
    {
        if (!hasCover) Assert.SkipUnless(OperatingSystem.IsWindows(), "Uses the bundled Windows decoder.");
        using var context = HeadlessTestHost.CreateContext();
        var path = hasCover
            ? TestRepository.FromRepositoryRoot($"tests/Cafe.Launcher.HeadlessTests/Fixtures/VideoThumbnails/{fileName}")
            : context.Directory.Sub(fileName);
        if (!hasCover) HeadlessTestHost.WriteTwoFrameVideo(path);
        context.Appearance.GetBackgroundBitmapAsync = context.ViewModel.GetBackgroundBitmapAsync;

        await context.ViewModel.UpdateBackgroundImageAsync(new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Custom,
            CustomBackgroundPath = path,
            ThemeColorMode = ThemeColorModes.Wallpaper
        }, null, CancellationToken.None);
        await context.Appearance.PendingThemeRefresh.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotEmpty(context.Appearance.ThemeColorPaletteItems);
        Assert.All(context.Appearance.ThemeColorPaletteItems, item =>
        {
            var seed = Color.Parse(item.ColorHex);
            Assert.True(hasCover ? seed.B > 200 && seed.R < 60 : seed.R > 200 && seed.B < 60,
                $"Expected {(hasCover ? "blue cover" : "red first frame")} palette, got {seed}.");
        });
        var source = await context.ViewModel.GetBackgroundBitmapAsync(CancellationToken.None);
        Assert.Same(context.ViewModel.BackgroundPreviewImage, source);
        Assert.NotSame(context.ViewModel.BackgroundImageSource, source);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshThemeColorPaletteFromCurrentBackgroundAsync_WhenVideoArtworkPending_SaveWaitsForArtworkOrFallback(bool unavailable)
    {
        using var context = HeadlessTestHost.CreateContext();
        using var previous = HeadlessTestHost.WriteSolidPngBitmap(context.Directory.Path, "previous.png", Brushes.Red, 64, 64);
        context.Appearance.GetBackgroundBitmapAsync = _ => Task.FromResult<Bitmap?>(previous);
        await context.Appearance.RefreshThemeColorPaletteFromCurrentBackgroundAsync(markDirty: false);
        var previousSeed = Assert.Single(context.Appearance.ThemeColorPaletteItems).ColorHex;
        var completion = new TaskCompletionSource<IImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var background = new BackgroundViewModel(
            context.Provider.GetRequiredService<ImageCacheService>(), new LocalDiagnostics(),
            context.Provider.GetRequiredService<SettingsViewModel>(),
            videoThumbnailLoader: (_, _) => completion.Task);
        context.Appearance.GetBackgroundBitmapAsync = background.GetBackgroundBitmapAsync;
        var path = context.Directory.Sub("pending.mp4");
        await File.WriteAllBytesAsync(path, []);
        var settings = new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Custom,
            CustomBackgroundPath = path,
            ThemeColorMode = ThemeColorModes.Wallpaper
        };
        context.Appearance.Editor.Current.ThemeColorMode = ThemeColorModes.Wallpaper;
        await background.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        var saving = context.Appearance.EnsureThemePaletteReadyForSaveAsync();
        Assert.False(saving.IsCompleted);
        Assert.Equal(previousSeed, Assert.Single(context.Appearance.ThemeColorPaletteItems).ColorHex);

        var artwork = unavailable ? null : HeadlessTestHost.WriteSolidPngBitmap(context.Directory.Path, "cover.png", Brushes.Blue, 64, 64);
        completion.SetResult(artwork);
        await saving;

        var source = await background.GetBackgroundBitmapAsync(CancellationToken.None);
        Assert.Same(unavailable ? background.BackgroundImageSource : artwork, source);
        var expected = ThemeColorExtractionService.ExtractPalette(source!, settings.ThemeColorExtractionAlgorithm);
        Assert.Equal(ThemeColorExtractionService.ToColorHex(expected[0]), context.Appearance.ThemeColorPaletteItems[0].ColorHex);
    }

    [AvaloniaFact]
    public async Task RefreshThemeColorPaletteFromCurrentBackgroundAsync_WhenVideoChangesDuringCapture_DiscardsOldPalette()
    {
        using var context = HeadlessTestHost.CreateContext();
        var firstPath = context.Directory.Sub("first.mp4");
        var secondPath = context.Directory.Sub("second.mp4");
        await File.WriteAllBytesAsync(firstPath, []);
        await File.WriteAllBytesAsync(secondPath, []);
        var first = new TaskCompletionSource<IImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = HeadlessTestHost.WriteSolidPngBitmap(context.Directory.Path, "blue.png", Brushes.Blue, 64, 64);
        using var background = new BackgroundViewModel(
            context.Provider.GetRequiredService<ImageCacheService>(), new LocalDiagnostics(),
            context.Provider.GetRequiredService<SettingsViewModel>(),
            videoThumbnailLoader: (path, _) => path == firstPath ? first.Task : Task.FromResult<IImage?>(second));
        context.Appearance.GetBackgroundBitmapAsync = background.GetBackgroundBitmapAsync;
        var settings = new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Custom,
            CustomBackgroundPath = firstPath,
            ThemeColorMode = ThemeColorModes.Wallpaper
        };
        await background.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        var oldCapture = background.PendingVideoThumbnail;
        var oldPalette = context.Appearance.PendingThemeRefresh;
        settings.CustomBackgroundPath = secondPath;
        await background.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        await context.Appearance.PendingThemeRefresh.WaitAsync(TimeSpan.FromSeconds(5));
        await oldPalette;
        var newSeed = Assert.Single(context.Appearance.ThemeColorPaletteItems).ColorHex;
        Assert.Equal(Colors.Blue, Color.Parse(newSeed));

        first.SetResult(HeadlessTestHost.WriteSolidPngBitmap(context.Directory.Path, "red.png", Brushes.Red, 64, 64));
        await oldCapture;
        Assert.Equal(newSeed, Assert.Single(context.Appearance.ThemeColorPaletteItems).ColorHex);
    }

    [AvaloniaFact]
    public async Task RefreshThemeColorPaletteFromCurrentBackgroundAsync_WhenBackgroundBitmapPresent_FillsPalette()
    {
        using var context = HeadlessTestHost.CreateContext();
        using var bitmap = HeadlessTestHost.WriteSolidPngBitmap(
            context.Directory.Path, "seed.png", Brushes.IndianRed, 96, 64);
        context.Appearance.GetBackgroundBitmapAsync = _ => Task.FromResult<Bitmap?>(bitmap);

        await context.Appearance.RefreshThemeColorPaletteFromCurrentBackgroundAsync(markDirty: false);

        Assert.True(
            context.Appearance.ThemeColorPaletteItems.Count > 0,
            "Palette should be filled after extraction completes.");
    }

    [AvaloniaFact]
    public async Task RefreshThemeColorPaletteFromCurrentBackgroundAsync_WhenBitmapIsNull_ClearsPalette()
    {
        using var context = HeadlessTestHost.CreateContext();
        context.Appearance.GetBackgroundBitmapAsync = _ => Task.FromResult<Bitmap?>(null);

        await context.Appearance.RefreshThemeColorPaletteFromCurrentBackgroundAsync(markDirty: false);

        Assert.Empty(context.Appearance.ThemeColorPaletteItems);
    }

    [AvaloniaFact]
    public async Task RefreshThemeColorPaletteFromCurrentBackgroundAsync_WhenSourceAccessorThrows_DoesNotLeakException()
    {
        using var context = HeadlessTestHost.CreateContext();
        context.Appearance.GetBackgroundBitmapAsync = _ =>
            throw new InvalidOperationException("simulated source failure");

        var exception = await Record.ExceptionAsync(() =>
            context.Appearance.RefreshThemeColorPaletteFromCurrentBackgroundAsync(markDirty: false));

        Assert.Null(exception);
    }

    [AvaloniaFact]
    public async Task RefreshThemeColorPaletteFromCurrentBackgroundAsync_WhenStaleExtractionLands_DoesNotOverrideNewerDecision()
    {
        using var context = HeadlessTestHost.CreateContext();
        using var bitmap = HeadlessTestHost.WriteSolidPngBitmap(
            context.Directory.Path, "stale.png", Brushes.IndianRed, 96, 64);
        context.Appearance.GetBackgroundBitmapAsync = _ => Task.FromResult<Bitmap?>(bitmap);

        // 第一轮提取不等待；随后“壁纸已清空”的更新决策先行落定并递增代数。
        var stale = context.Appearance.RefreshThemeColorPaletteFromCurrentBackgroundAsync(markDirty: false);
        context.Appearance.GetBackgroundBitmapAsync = _ => Task.FromResult<Bitmap?>(null);
        await context.Appearance.RefreshThemeColorPaletteFromCurrentBackgroundAsync(markDirty: false);
        await stale;

        // 无论旧任务在清空之前还是之后落地，最终色板都必须是空（新决策优先）。
        Assert.Empty(context.Appearance.ThemeColorPaletteItems);
    }

    [AvaloniaFact]
    public async Task UpdateBackgroundImageAsync_WhenWallpaperTheme_WiresExtractionAndFillsPalette()
    {
        using var context = HeadlessTestHost.CreateContext();
        var wallpaperPath = Path.Combine(context.Directory.Path, "wallpaper.png");
        HeadlessTestHost.WriteSolidPng(wallpaperPath, Brushes.IndianRed, 320, 200);

        // 与 ShellLifecycle 相同的取色来源接线。
        context.Appearance.GetBackgroundBitmapAsync = context.ViewModel.GetBackgroundBitmapAsync;
        try
        {
            var settings = new LauncherSettings
            {
                BackgroundSource = BackgroundSources.Custom,
                CustomBackgroundPath = wallpaperPath,
                ThemeColorMode = ThemeColorModes.Wallpaper
            };
            await context.ViewModel.UpdateBackgroundImageAsync(settings, snapshot: null, CancellationToken.None);
            await context.Appearance.PendingThemeRefresh.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(
                context.Appearance.ThemeColorPaletteItems.Count > 0,
                "Wallpaper swap should eventually fill the theme palette from the decoded background.");
        }
        finally
        {
            File.Delete(wallpaperPath);
        }
    }
}
