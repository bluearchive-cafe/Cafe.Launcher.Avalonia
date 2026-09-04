using Avalonia.Media;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public partial class MainWindowViewModelTests
{
    [Fact]
    public async Task AppearancePreview_WhenSettingChangesAgain_CancelsPreviousPreview()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);
        var firstPreviewStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstPreviewCanceled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        string? appliedPath = null;
        viewModel.Settings.PreviewAppearanceAsync = async (settings, propertyName, cancellationToken) =>
        {
            if (propertyName != nameof(LauncherSettings.CustomBackgroundPath))
            {
                return;
            }

            if (settings.CustomBackgroundPath == "first")
            {
                firstPreviewStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    firstPreviewCanceled.TrySetResult();
                    throw;
                }
            }

            appliedPath = settings.CustomBackgroundPath;
        };

        viewModel.Settings.Editor.Current.CustomBackgroundPath = "first";
        await firstPreviewStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.Settings.Editor.Current.CustomBackgroundPath = "second";
        await viewModel.Settings.PendingAppearancePreview;

        await firstPreviewCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("second", appliedPath);
    }

    [Fact]
    public async Task SaveSettingsAsync_WhenAppearancePreviewIsRunning_WaitsForCurrentPreview()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);
        await viewModel.InitializeAsync();
        var previewStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePreview = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.Settings.PreviewAppearanceAsync = async (_, _, cancellationToken) =>
        {
            previewStarted.TrySetResult();
            await releasePreview.Task.WaitAsync(cancellationToken);
        };

        viewModel.Settings.Editor.Current.CustomBackgroundPath = "pending-wallpaper";
        await previewStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var saveTask = viewModel.Settings.SaveSettingsCommand.ExecuteAsync(null);
        try
        {
            // 预览被 releasePreview 门控、尚未释放，期间保存不可能完成；
            // 持续泵一小段时间，让（若实现有缺陷的）提前完成有机会暴露并即时失败。
            var observationEnd = DateTime.UtcNow.AddMilliseconds(100);
            while (DateTime.UtcNow < observationEnd)
            {
                Assert.False(saveTask.IsCompleted);
                await Task.Delay(10);
            }

            Assert.False(saveTask.IsCompleted);
        }
        finally
        {
            releasePreview.TrySetResult();
        }

        await saveTask;
    }

    [Fact]
    public async Task SaveSettingsAsync_WhenPreviewNeverSettles_CompletesAfterSettleTimeout()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);
        await viewModel.InitializeAsync();
        var originalTimeout = SettingsViewModel.AppearancePreviewSettleTimeout;
        SettingsViewModel.AppearancePreviewSettleTimeout = TimeSpan.FromMilliseconds(50);
        var releasePreview = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.Settings.PreviewAppearanceAsync = async (_, _, cancellationToken) =>
        {
            await releasePreview.Task.WaitAsync(cancellationToken);
        };

        try
        {
            viewModel.Settings.Editor.Current.CustomBackgroundPath = "stuck-preview";
            var saveTask = viewModel.Settings.SaveSettingsCommand.ExecuteAsync(null);

            // 预览永不完成：保存必须在预算超时后自行完成，而不是被无限挂起。
            await saveTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            SettingsViewModel.AppearancePreviewSettleTimeout = originalTimeout;
            releasePreview.TrySetResult();
        }
    }

    [Fact]
    public async Task BackgroundPresentationSettings_ArePreviewedBeforeSave()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);
        viewModel.Settings.Editor.ApplySnapshot(new LauncherSettings());

        viewModel.Settings.Editor.Current.BackgroundFit = BackgroundFits.Uniform;
        viewModel.Settings.Appearance.SelectedBackgroundFillColor =
            Color.FromArgb(0xFF, 0x12, 0x34, 0x56);
        await viewModel.Settings.PendingAppearancePreview;

        Assert.Equal(Stretch.Uniform, viewModel.Background.BackgroundStretch);
        var fill = Assert.IsType<SolidColorBrush>(viewModel.Background.BackgroundFillBrush);
        Assert.Equal(Color.FromArgb(0xFF, 0x12, 0x34, 0x56), fill.Color);
        Assert.True(viewModel.Settings.IsSettingsDirty);
    }

    [Fact]
    public void ExtractPaletteFromBgraBuffer_WhenBackgroundHasMultipleColors_ReturnsAtMostFiveColors()
    {
        var buffer = CreateStripedBgraBuffer(
            12,
            6,
            [
                Color.FromRgb(0xD8, 0x20, 0x38),
                Color.FromRgb(0x20, 0x90, 0x40),
                Color.FromRgb(0x30, 0x50, 0xD8),
                Color.FromRgb(0xE0, 0xA0, 0x20),
                Color.FromRgb(0x90, 0x30, 0xB8),
                Color.FromRgb(0x20, 0xB8, 0xD8)
            ]);

        var palette = ThemeColorExtractionService.ExtractPaletteFromBgraBuffer(buffer, 12, 6, 12 * 4);

        Assert.InRange(palette.Count, 1, 5);
    }

    [Fact]
    public void ExtractPaletteFromBgraBuffer_WhenSaturatedAndGrayExist_PrioritizesSaturatedColor()
    {
        var buffer = CreateStripedBgraBuffer(
            8,
            4,
            [
                Color.FromRgb(0x80, 0x80, 0x80),
                Color.FromRgb(0xD8, 0x20, 0x38)
            ]);

        var palette = ThemeColorExtractionService.ExtractPaletteFromBgraBuffer(buffer, 8, 4, 8 * 4);

        Assert.NotEmpty(palette);
        Assert.True(palette[0].R > palette[0].G);
        Assert.True(palette[0].R > palette[0].B);
    }

    [Fact]
    public void ExtractPaletteFromBgraBuffer_WhenBackgroundHasNoUsableColor_ReturnsEmpty()
    {
        var buffer = CreateSolidBgraBuffer(0x80, 0x80, 0x80, 8, 8, 0xFF);

        var palette = ThemeColorExtractionService.ExtractPaletteFromBgraBuffer(buffer, 8, 8, 8 * 4);

        Assert.Empty(palette);
    }

    [Fact]
    public void ExtractPaletteFromBgraBuffer_WhenBackgroundIsTransparent_ReturnsEmpty()
    {
        var buffer = CreateSolidBgraBuffer(0xD8, 0x20, 0x38, 8, 8, 0x00);

        var palette = ThemeColorExtractionService.ExtractPaletteFromBgraBuffer(buffer, 8, 8, 8 * 4);

        Assert.Empty(palette);
    }

    [Fact]
    public void NormalizeAccentColorForUi_WhenColorIsPaleAndLowSaturation_IncreasesContrast()
    {
        var source = Color.FromRgb(0xC9, 0xCD, 0xD8);

        var normalized = SettingsAppearanceViewModel.NormalizeAccentColorForUi(source);

        Assert.NotEqual(source, normalized);
        Assert.True(GetPerceivedSaturation(normalized) >= 0.22d);
        Assert.True(GetRelativeLuminance(normalized) < GetRelativeLuminance(source));
    }

    [Fact]
    public void NormalizeAccentColorForUi_WhenColorAlreadyHasStrongContrast_KeepsOriginalColor()
    {
        var source = Color.FromRgb(0x20, 0x50, 0xD8);

        var normalized = SettingsAppearanceViewModel.NormalizeAccentColorForUi(source);

        Assert.Equal(source, normalized);
    }

    [Fact]
    public async Task SelectedThemeColorPaletteIndex_WhenSettingsVisible_MarksSettingsDirtyAndUpdatesSelection()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);
        viewModel.Settings.Appearance.ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
        {
            Index = 0,
            ColorHex = "#FFD82038",
            Brush = new SolidColorBrush(Color.FromRgb(0xD8, 0x20, 0x38))
        });
        viewModel.Settings.Appearance.ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
        {
            Index = 1,
            ColorHex = "#FF2050D8",
            Brush = new SolidColorBrush(Color.FromRgb(0x20, 0x50, 0xD8))
        });
        viewModel.Settings.Editor.Current.ThemeColorMode = ThemeColorModes.Wallpaper;
        viewModel.WindowChrome.IsSettingsVisible = true;
        viewModel.Settings.Editor.ApplySnapshot(viewModel.Settings.Editor.Current);

        viewModel.Settings.Appearance.SelectedThemeColorPaletteIndex = 1;

        Assert.True(viewModel.Settings.IsSettingsDirty);
        Assert.False(viewModel.Settings.Appearance.ThemeColorPaletteItems[0].IsSelected);
        Assert.True(viewModel.Settings.Appearance.ThemeColorPaletteItems[1].IsSelected);
        var preview = Assert.IsType<SolidColorBrush>(
            viewModel.Settings.Appearance.ThemeColorPreviewBrush);
        var expectedPrimary = MaterialColorMapper.ToAvaloniaColor(
            MaterialSchemeGenerator.CreateScheme(
                Color.FromRgb(0x20, 0x50, 0xD8),
                ThemeColorVariants.TonalSpot,
                isDark: false).Primary);
        Assert.Equal(expectedPrimary, preview.Color);
    }

    [Fact]
    public void Load_WhenWallpaperPaletteHasSeeds_DisplaysGeneratedPrimaryColors()
    {
        var editor = new SettingsEditor();
        var settings = new LauncherSettings
        {
            ThemeColorMode = ThemeColorModes.Wallpaper,
            ThemeMode = ThemeModes.Light,
            ThemeColorVariant = ThemeColorVariants.Expressive,
            ThemeColorPalette = ["#FFC3A58E"],
            SelectedThemeColorPaletteIndex = 0
        };
        editor.ApplySnapshot(settings);
        using var appearance = new SettingsAppearanceViewModel(editor);

        appearance.Load(settings);

        var displayedBrush = Assert.IsType<SolidColorBrush>(
            Assert.Single(appearance.ThemeColorPaletteItems).Brush);
        var expectedPrimary = MaterialColorMapper.ToAvaloniaColor(
            MaterialSchemeGenerator.CreateScheme(
                Color.FromRgb(0xC3, 0xA5, 0x8E),
                ThemeColorVariants.Expressive,
                isDark: false).Primary);
        Assert.Equal(expectedPrimary, displayedBrush.Color);
    }

    [Fact]
    public void NeutralStrategy_TogglingSeedFollowing_ReflectsHintVisibility() // ADR-010
    {
        var editor = new SettingsEditor();
        var settings = new LauncherSettings
        {
            NeutralColorStrategy = NeutralColorStrategies.BrandBlue
        };
        editor.ApplySnapshot(settings);
        using var appearance = new SettingsAppearanceViewModel(editor);

        appearance.Load(settings);
        Assert.False(appearance.IsSeedFollowingNeutralStrategySelected);

        editor.Current.NeutralColorStrategy = NeutralColorStrategies.SeedFollowing;
        Assert.True(appearance.IsSeedFollowingNeutralStrategySelected);

        editor.Current.NeutralColorStrategy = NeutralColorStrategies.BrandBlue;
        Assert.False(appearance.IsSeedFollowingNeutralStrategySelected);
    }

    [Fact]
    public void ThemeColorMode_SwitchingAwayFromWallpaper_HidesExtractionAlgorithmOption()
    {
        var editor = new SettingsEditor();
        var settings = new LauncherSettings
        {
            ThemeColorMode = ThemeColorModes.Wallpaper
        };
        editor.ApplySnapshot(settings);
        using var appearance = new SettingsAppearanceViewModel(editor);

        appearance.Load(settings);
        Assert.True(appearance.IsThemeColorExtractionAlgorithmVisible);

        editor.Current.ThemeColorMode = ThemeColorModes.Default;
        Assert.False(appearance.IsThemeColorExtractionAlgorithmVisible);

        editor.Current.ThemeColorMode = ThemeColorModes.Wallpaper;
        Assert.True(appearance.IsThemeColorExtractionAlgorithmVisible);
    }

    [Fact]
    public void AppearanceSettings_WhenShowHiddenSettingsEnabled_ShowsConditionalSettings()
    {
        var editor = new SettingsEditor();
        var settings = new LauncherSettings
        {
            ThemeColorMode = ThemeColorModes.Default,
            BackgroundFit = BackgroundFits.Fill,
            BackgroundSource = BackgroundSources.Remote
        };
        editor.ApplySnapshot(settings);
        using var appearance = new SettingsAppearanceViewModel(editor, showHiddenSettings: true);

        appearance.Load(settings);

        Assert.True(appearance.IsThemeColorExtractionAlgorithmSettingsVisible);
        Assert.True(appearance.IsThemeColorPaletteVisible);
        Assert.True(appearance.IsCustomThemeColorPickerVisible);
        Assert.True(appearance.IsBackgroundFillColorVisible);
        Assert.True(appearance.IsCustomBackgroundSettingsVisible);
    }

    private static byte[] CreateSolidBgraBuffer(byte r, byte g, byte b, int width, int height, byte alpha)
    {
        var rowBytes = width * 4;
        var buffer = new byte[rowBytes * height];
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var offset = rowOffset + (x * 4);
                buffer[offset] = b;
                buffer[offset + 1] = g;
                buffer[offset + 2] = r;
                buffer[offset + 3] = alpha;
            }
        }

        return buffer;
    }

    private static byte[] CreateStripedBgraBuffer(int width, int height, IReadOnlyList<Color> colors)
    {
        var rowBytes = width * 4;
        var buffer = new byte[rowBytes * height];
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var color = colors[Math.Min(colors.Count - 1, x * colors.Count / width)];
                var offset = rowOffset + (x * 4);
                buffer[offset] = color.B;
                buffer[offset + 1] = color.G;
                buffer[offset + 2] = color.R;
                buffer[offset + 3] = color.A;
            }
        }

        return buffer;
    }

    private static double GetPerceivedSaturation(Color color)
    {
        var max = Math.Max(color.R, Math.Max(color.G, color.B));
        var min = Math.Min(color.R, Math.Min(color.G, color.B));
        return max == 0 ? 0 : 1d - (min / (double)max);
    }

    private static double GetRelativeLuminance(Color color)
    {
        static double ToLinear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * ToLinear(color.R))
            + (0.7152 * ToLinear(color.G))
            + (0.0722 * ToLinear(color.B));
    }
}
