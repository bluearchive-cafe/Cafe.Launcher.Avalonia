using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Cafe.Launcher.Avalonia.Models;
using Xunit;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary>
/// 壁纸主题取色异步化回归：提取在后台线程执行，palette 经代数校验后落定；
/// 陈旧任务（壁纸已再次切换）不得覆盖较新的色板决策。
/// </summary>
public sealed class ThemeColorExtractionAsyncHeadlessTests
{
    [AvaloniaFact]
    public async Task RefreshThemeColorPaletteFromCurrentBackgroundAsync_WhenBackgroundBitmapPresent_FillsPalette()
    {
        using var context = HeadlessTestHost.CreateContext();
        using var bitmap = HeadlessTestHost.WriteSolidPngBitmap(
            context.TempDir, "seed.png", Brushes.IndianRed, 96, 64);
        context.Appearance.GetBackgroundBitmap = () => bitmap;

        await context.Appearance.RefreshThemeColorPaletteFromCurrentBackgroundAsync(markDirty: false);

        Assert.True(
            context.Appearance.ThemeColorPaletteItems.Count > 0,
            "Palette should be filled after extraction completes.");
    }

    [AvaloniaFact]
    public async Task RefreshThemeColorPaletteFromCurrentBackgroundAsync_WhenBitmapIsNull_ClearsPalette()
    {
        using var context = HeadlessTestHost.CreateContext();
        context.Appearance.GetBackgroundBitmap = () => null;

        await context.Appearance.RefreshThemeColorPaletteFromCurrentBackgroundAsync(markDirty: false);

        Assert.Empty(context.Appearance.ThemeColorPaletteItems);
    }

    [AvaloniaFact]
    public async Task RefreshThemeColorPaletteFromCurrentBackgroundAsync_WhenSourceAccessorThrows_DoesNotLeakException()
    {
        using var context = HeadlessTestHost.CreateContext();
        context.Appearance.GetBackgroundBitmap = () =>
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
            context.TempDir, "stale.png", Brushes.IndianRed, 96, 64);
        context.Appearance.GetBackgroundBitmap = () => bitmap;

        // 第一轮提取不等待；随后“壁纸已清空”的更新决策先行落定并递增代数。
        var stale = context.Appearance.RefreshThemeColorPaletteFromCurrentBackgroundAsync(markDirty: false);
        context.Appearance.GetBackgroundBitmap = () => null;
        await context.Appearance.RefreshThemeColorPaletteFromCurrentBackgroundAsync(markDirty: false);
        await stale;

        // 无论旧任务在清空之前还是之后落地，最终色板都必须是空（新决策优先）。
        Assert.Empty(context.Appearance.ThemeColorPaletteItems);
    }

    [AvaloniaFact]
    public async Task UpdateBackgroundImageAsync_WhenWallpaperTheme_WiresExtractionAndFillsPalette()
    {
        using var context = HeadlessTestHost.CreateContext();
        var wallpaperPath = Path.Combine(context.TempDir, "wallpaper.png");
        HeadlessTestHost.WriteSolidPng(wallpaperPath, Brushes.IndianRed, 320, 200);

        // 与 ShellLifecycle 相同的取色来源接线。
        context.Appearance.GetBackgroundBitmap = () => context.ViewModel.GetBackgroundBitmap();
        try
        {
            var settings = new LauncherSettings
            {
                BackgroundSource = BackgroundSources.Custom,
                CustomBackgroundPath = wallpaperPath,
                ThemeColorMode = ThemeColorModes.Wallpaper
            };
            await context.ViewModel.UpdateBackgroundImageAsync(settings, snapshot: null, CancellationToken.None);
            await context.Appearance.PendingThemeRefresh;

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
