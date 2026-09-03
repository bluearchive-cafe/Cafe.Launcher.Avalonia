using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Cafe.Launcher.Avalonia.Composition;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary>
/// 壁纸主题取色异步化回归：提取在后台线程执行，palette 经代数校验后落定；
/// 陈旧任务（壁纸已再次切换）不得覆盖较新的色板决策。
/// </summary>
public sealed class ThemeColorExtractionAsyncHeadlessTests
{
    [AvaloniaFact]
    public async Task RefreshThemeColorPaletteAsync_WhenBackgroundBitmapPresent_FillsPalette()
    {
        using var context = CreateAppearanceContext();
        using var bitmap = WriteSolidImage(context.TempDir, "seed.png", Brushes.IndianRed, 96, 64);
        context.Appearance.GetBackgroundBitmap = () => bitmap;

        await context.Appearance.RefreshThemeColorPaletteFromCurrentBackgroundAsync(markDirty: false);

        Assert.True(
            context.Appearance.ThemeColorPaletteItems.Count > 0,
            "Palette should be filled after extraction completes.");
    }

    [AvaloniaFact]
    public async Task RefreshThemeColorPaletteAsync_WhenBitmapIsNull_ClearsPalette()
    {
        using var context = CreateAppearanceContext();
        context.Appearance.GetBackgroundBitmap = () => null;

        await context.Appearance.RefreshThemeColorPaletteFromCurrentBackgroundAsync(markDirty: false);

        Assert.Empty(context.Appearance.ThemeColorPaletteItems);
    }

    [AvaloniaFact]
    public async Task PendingThemeRefresh_WhenStaleExtractionLands_DoesNotOverrideNewerDecision()
    {
        using var context = CreateAppearanceContext();
        using var bitmap = WriteSolidImage(context.TempDir, "stale.png", Brushes.IndianRed, 96, 64);
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
        using var context = CreateBackgroundContext();
        var wallpaperPath = Path.Combine(context.TempDir, "wallpaper.png");
        WriteSolidImageFile(wallpaperPath, Brushes.IndianRed, 320, 200);

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

    private static TestContext CreateAppearanceContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var services = new ServiceCollection();
        services.AddLauncherServices();
        services.AddSingleton(_ => new UnifiedLogger(Path.Combine(tempDir, "logs")));
        var provider = services.BuildServiceProvider();
        return new TestContext(tempDir, provider);
    }

    private static TestContext CreateBackgroundContext() => CreateAppearanceContext();

    private static Bitmap WriteSolidImage(
        string tempDir,
        string fileName,
        IBrush brush,
        int width,
        int height)
    {
        var path = Path.Combine(tempDir, fileName);
        WriteSolidImageFile(path, brush, width, height);
        return new Bitmap(path);
    }

    private static void WriteSolidImageFile(string path, IBrush brush, int width, int height)
    {
        var border = new Border { Width = width, Height = height, Background = brush };
        border.Measure(new Size(width, height));
        border.Arrange(new Rect(0, 0, width, height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(border);
        using var stream = File.Create(path);
        bitmap.Save(stream, new PngBitmapEncoderOptions());
    }

    private sealed class TestContext : IDisposable
    {
        public TestContext(string tempDir, ServiceProvider provider)
        {
            TempDir = tempDir;
            Provider = provider;
            Appearance = provider.GetRequiredService<SettingsViewModel>().Appearance;
            ViewModel = provider.GetRequiredService<BackgroundViewModel>();
        }

        public string TempDir { get; }

        public ServiceProvider Provider { get; }

        public SettingsAppearanceViewModel Appearance { get; }

        public BackgroundViewModel ViewModel { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            Provider.Dispose();
        }
    }
}
