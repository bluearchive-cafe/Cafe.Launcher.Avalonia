using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Features.Settings;

public partial class SettingsAppearanceViewModel : ViewModelBase, IDisposable
{
    private readonly ISettingsEditor editor;
    private readonly IPlatformSettings? platformSettings;
    private readonly bool showHiddenSettings;
    private bool suppressEditorUpdates;
    private bool disposed;
    private int themeRefreshGeneration;
    private Task? inFlightThemeRefresh;

    public SettingsAppearanceViewModel(ISettingsEditor editor, bool showHiddenSettings = false)
    {
        this.editor = editor;
        this.showHiddenSettings = showHiddenSettings;
        editor.CurrentPropertyChanged += OnCurrentSettingChanged;
        platformSettings = Application.Current?.PlatformSettings;
        if (platformSettings is not null)
        {
            platformSettings.ColorValuesChanged += OnPlatformColorValuesChanged;
        }
    }

    public ISettingsEditor Editor => editor;
    public Func<Bitmap?>? GetBackgroundBitmap { get; set; }

    [ObservableProperty]
    private bool isCustomBackground;

    [ObservableProperty]
    private Color selectedBackgroundFillColor = Colors.Black;

    [ObservableProperty]
    private IBrush backgroundFillColorPreviewBrush = new SolidColorBrush(Colors.Black);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomBackgroundSettingsVisible))]
    private bool isCustomBackgroundSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBackgroundFillColorVisible))]
    private bool isBackgroundFitSelected;

    [ObservableProperty]
    private Color selectedCustomThemeColor = Color.Parse(LauncherConstants.DefaultThemeColor);

    [ObservableProperty]
    private IBrush themeColorPreviewBrush =
        new SolidColorBrush(Color.Parse(LauncherConstants.DefaultThemeColor));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomThemeColorPickerVisible))]
    private bool isCustomThemeColorSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThemeColorPaletteVisible))]
    private bool isWallpaperThemeColorSelected;

    // 取色算法仅作用于壁纸取色，其余主题色来源下该行不生效，直接隐藏。
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThemeColorExtractionAlgorithmSettingsVisible))]
    private bool isThemeColorExtractionAlgorithmVisible;

    [ObservableProperty]
    private bool isSeedFollowingNeutralStrategySelected;

    public bool IsThemeColorExtractionAlgorithmSettingsVisible =>
        showHiddenSettings || IsThemeColorExtractionAlgorithmVisible;

    public bool IsThemeColorPaletteVisible =>
        showHiddenSettings || IsWallpaperThemeColorSelected;

    public bool IsCustomThemeColorPickerVisible =>
        showHiddenSettings || IsCustomThemeColorSelected;

    public bool IsBackgroundFillColorVisible =>
        showHiddenSettings || IsBackgroundFitSelected;

    public bool IsCustomBackgroundSettingsVisible =>
        showHiddenSettings || IsCustomBackgroundSelected;

    [ObservableProperty]
    private int selectedThemeColorPaletteIndex;

    public ObservableCollection<ThemeColorPaletteItem> ThemeColorPaletteItems { get; } = [];

    public void Load(LauncherSettings settings)
    {
        var previous = suppressEditorUpdates;
        suppressEditorUpdates = true;
        try
        {
            SelectedCustomThemeColor = ParseColorOrDefault(settings.CustomThemeColor);
            IsCustomThemeColorSelected = settings.ThemeColorMode == ThemeColorModes.Custom;
            IsWallpaperThemeColorSelected = settings.ThemeColorMode == ThemeColorModes.Wallpaper;
            IsThemeColorExtractionAlgorithmVisible = IsWallpaperThemeColorSelected;
            IsSeedFollowingNeutralStrategySelected =
                settings.NeutralColorStrategy == NeutralColorStrategies.SeedFollowing;
            IsCustomBackground = !string.IsNullOrWhiteSpace(settings.CustomBackgroundPath);
            IsBackgroundFitSelected = settings.BackgroundFit == BackgroundFits.Uniform;
            SelectedBackgroundFillColor = ParseColorOrDefault(settings.BackgroundFillColor);
            IsCustomBackgroundSelected = settings.BackgroundSource == BackgroundSources.Custom;
            ReplaceThemeColorPalette(
                settings.ThemeColorPalette,
                settings.SelectedThemeColorPaletteIndex);
        }
        finally
        {
            suppressEditorUpdates = previous;
        }
    }

    /// <summary>最近一次在途取色任务；供调用方与测试等待取色落定。</summary>
    internal Task PendingThemeRefresh => inFlightThemeRefresh ?? Task.CompletedTask;

    /// <summary>
    /// 从当前壁纸重新提取主题色板。提取（含整幅源图降采样与量化）在线程池执行，
    /// 结果经代数校验后回到 UI 线程应用；期间壁纸可能再次切换并释放旧位图，
    /// 陈旧结果与已释放位图都被静默丢弃，不会覆盖较新的色板。
    /// </summary>
    public Task RefreshThemeColorPaletteFromCurrentBackgroundAsync(
        bool markDirty,
        bool applySchemeAfter = false)
    {
        var bitmap = GetBackgroundBitmap?.Invoke();
        var generation = Interlocked.Increment(ref themeRefreshGeneration);
        var refresh = ApplyExtractedPaletteAsync(bitmap, generation, markDirty, applySchemeAfter);
        inFlightThemeRefresh = refresh;
        return refresh;
    }

    private async Task ApplyExtractedPaletteAsync(
        Bitmap? bitmap,
        int generation,
        bool markDirty,
        bool applySchemeAfter)
    {
        if (bitmap is null)
        {
            ReplaceThemeColorPalette([], 0);
            if (applySchemeAfter)
            {
                ApplyResolvedTheme();
            }

            return;
        }

        IReadOnlyList<Color> colors;
        try
        {
            colors = await Task.Run(() => ThemeColorExtractionService.ExtractPalette(
                bitmap,
                editor.Current.ThemeColorExtractionAlgorithm));
        }
        catch (ObjectDisposedException)
        {
            // 提取期间壁纸再次切换会释放旧位图；丢弃本轮即可。
            return;
        }
        catch (Exception ex)
        {
            // 提取失败不允许打断调用方（含 fire-and-forget）：保留现有色板。
            Debug.WriteLine($"Theme color extraction failed: {ex.Message}");
            return;
        }

        if (generation != Volatile.Read(ref themeRefreshGeneration))
        {
            return;
        }

        var hexes = colors.Select(ThemeColorExtractionService.ToColorHex).ToArray();
        var selectedIndex = SelectedThemeColorPaletteIndex < hexes.Length
            ? SelectedThemeColorPaletteIndex
            : 0;
        await RunOnUiAsync(() =>
        {
            ReplaceThemeColorPalette(hexes, selectedIndex);
            if (markDirty)
            {
                editor.Commit(settings =>
                {
                    settings.ThemeColorPalette = GetThemeColorPaletteHexes();
                    settings.SelectedThemeColorPaletteIndex = SelectedThemeColorPaletteIndex;
                });
            }

            if (applySchemeAfter)
            {
                // 直接落色而非走 ApplyThemeColor：空结果（如纯透明图）不会再次触发提取。
                ApplyResolvedTheme();
            }
        });
    }

    private static async Task RunOnUiAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(action);
        }
    }

    public List<string> GetThemeColorPaletteHexes() =>
        ThemeColorPaletteItems.Select(item => item.ColorHex).ToList();

    public void ApplyThemeColor(string themeColorMode, Color customColor)
    {
        if (themeColorMode == ThemeColorModes.Wallpaper && ThemeColorPaletteItems.Count == 0)
        {
            // 色板尚未提取：后台取色完成后经 applySchemeAfter 落色；
            // 此处直接返回，避免先落一次默认色再跳变。
            _ = RefreshThemeColorPaletteFromCurrentBackgroundAsync(
                markDirty: false,
                applySchemeAfter: true);
            return;
        }

        ApplyResolvedTheme();
    }

    private void ApplyResolvedTheme()
    {
        var color = ResolveThemeColor(
            editor.Current.ThemeColorMode,
            SelectedCustomThemeColor);
        ApplyScheme(
            color,
            editor.Current.ThemeColorVariant,
            IsDarkTheme(editor.Current.ThemeMode),
            editor.Current.NeutralColorStrategy);
        RefreshThemeColorPaletteBrushes();
        UpdateThemeColorPreview();
    }

    [RelayCommand]
    private async Task RefreshThemeColorPaletteAsync()
    {
        await RefreshThemeColorPaletteFromCurrentBackgroundAsync(markDirty: true);
        if (editor.Current.ThemeColorMode == ThemeColorModes.Wallpaper)
        {
            ApplyThemeColor(editor.Current.ThemeColorMode, SelectedCustomThemeColor);
        }
    }

    [RelayCommand]
    private void SelectThemeColorPalette(int index)
    {
        if (ThemeColorPaletteItems.Count == 0)
        {
            return;
        }

        SelectedThemeColorPaletteIndex = Math.Clamp(
            index,
            0,
            ThemeColorPaletteItems.Count - 1);
    }

    private void OnCurrentSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherSettings.ThemeColorMode))
        {
            var value = editor.Current.ThemeColorMode;
            IsCustomThemeColorSelected = value == ThemeColorModes.Custom;
            IsWallpaperThemeColorSelected = value == ThemeColorModes.Wallpaper;
            IsThemeColorExtractionAlgorithmVisible = IsWallpaperThemeColorSelected;

            // ADR-009: 变更即预览 — mode changes repaint the main window immediately.
            // 壁纸模式且色板缺失时由 ApplyThemeColor 触发后台提取，完成后自动落色。
            ApplyThemeColor(value, SelectedCustomThemeColor);
            return;
        }

        if (e.PropertyName == nameof(LauncherSettings.ThemeColorExtractionAlgorithm))
        {
            if (editor.Current.ThemeColorMode == ThemeColorModes.Wallpaper)
            {
                // ADR-009: 算法变更需要按新算法重新提取；提取完成后自动落色，
                // 保证选中的色板索引未变时配色仍被刷新。
                _ = RefreshThemeColorPaletteFromCurrentBackgroundAsync(
                    markDirty: true,
                    applySchemeAfter: true);
            }

            UpdateThemeColorPreview();
            return;
        }

        if (e.PropertyName == nameof(LauncherSettings.ThemeMode))
        {
            RefreshThemeColorPaletteBrushes();
            UpdateThemeColorPreview();
            return;
        }

        if (e.PropertyName is nameof(LauncherSettings.ThemeColorVariant)
            or nameof(LauncherSettings.NeutralColorStrategy))
        {
            IsSeedFollowingNeutralStrategySelected =
                editor.Current.NeutralColorStrategy == NeutralColorStrategies.SeedFollowing;
            // ADR-009: 变更即预览 — variant/strategy changes repaint the main
            // window immediately (ApplyThemeColor applies the scheme and refreshes
            // the palette swatches and preview chip).
            ApplyThemeColor(editor.Current.ThemeColorMode, SelectedCustomThemeColor);
            return;
        }

        if (e.PropertyName == nameof(LauncherSettings.BackgroundSource))
        {
            IsCustomBackgroundSelected =
                editor.Current.BackgroundSource == BackgroundSources.Custom;
            IsCustomBackground =
                !string.IsNullOrWhiteSpace(editor.Current.CustomBackgroundPath);
            return;
        }

        if (e.PropertyName == nameof(LauncherSettings.CustomBackgroundPath))
        {
            IsCustomBackground =
                !string.IsNullOrWhiteSpace(editor.Current.CustomBackgroundPath);
            return;
        }

        if (e.PropertyName == nameof(LauncherSettings.BackgroundFit))
        {
            IsBackgroundFitSelected = editor.Current.BackgroundFit == BackgroundFits.Uniform;
        }
    }

    partial void OnSelectedCustomThemeColorChanged(Color value)
    {
        PushToEditor(settings => settings.CustomThemeColor = ToColorHex(value));
        UpdateThemeColorPreview();
    }

    partial void OnSelectedThemeColorPaletteIndexChanged(int value)
    {
        PushToEditor(settings => settings.SelectedThemeColorPaletteIndex = value);
        UpdateThemeColorPaletteSelection();
        UpdateThemeColorPreview();
        if (editor.Current.ThemeColorMode == ThemeColorModes.Wallpaper)
        {
            ApplyThemeColor(editor.Current.ThemeColorMode, SelectedCustomThemeColor);
        }
    }

    partial void OnSelectedBackgroundFillColorChanged(Color value)
    {
        PushToEditor(settings => settings.BackgroundFillColor = ToColorHex(value));
        BackgroundFillColorPreviewBrush = new SolidColorBrush(value);
    }

    private void PushToEditor(Action<LauncherSettings> apply)
    {
        if (!suppressEditorUpdates)
        {
            editor.Commit(apply);
        }
    }

    private void ReplaceThemeColorPalette(IEnumerable<string> colors, int selectedIndex)
    {
        var normalizedColors = colors
            .Select(ParseThemeColorPaletteColor)
            .OfType<Color>()
            .Select(ThemeColorExtractionService.ToColorHex)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var previous = suppressEditorUpdates;
        suppressEditorUpdates = true;
        try
        {
            ThemeColorPaletteItems.Clear();
            for (var i = 0; i < normalizedColors.Length; i++)
            {
                var color = ParseColorOrDefault(normalizedColors[i]);
                ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
                {
                    Index = i,
                    ColorHex = normalizedColors[i],
                    Brush = new SolidColorBrush(GetGeneratedPrimaryColor(color))
                });
            }

            SelectedThemeColorPaletteIndex = normalizedColors.Length == 0
                ? 0
                : Math.Clamp(selectedIndex, 0, normalizedColors.Length - 1);
            UpdateThemeColorPaletteSelection();
        }
        finally
        {
            suppressEditorUpdates = previous;
        }

        UpdateThemeColorPreview();
    }

    private void UpdateThemeColorPaletteSelection()
    {
        for (var i = 0; i < ThemeColorPaletteItems.Count; i++)
        {
            ThemeColorPaletteItems[i].IsSelected = i == SelectedThemeColorPaletteIndex;
        }
    }

    private void RefreshThemeColorPaletteBrushes(bool? isDark = null)
    {
        foreach (var item in ThemeColorPaletteItems)
        {
            var seed = ParseThemeColorPaletteColor(item.ColorHex);
            if (seed is { } color)
            {
                item.Brush = new SolidColorBrush(
                    isDark is { } value
                        ? GetGeneratedPrimaryColor(color, value)
                        : GetGeneratedPrimaryColor(color));
            }
        }
    }

    private void UpdateThemeColorPreview()
    {
        var color = ResolveThemeColor(
            editor.Current.ThemeColorMode,
            SelectedCustomThemeColor);
        ThemeColorPreviewBrush = new SolidColorBrush(GetGeneratedPrimaryColor(color));
    }

    private Color GetGeneratedPrimaryColor(Color seed)
    {
        return GetGeneratedPrimaryColor(seed, IsDarkTheme(editor.Current.ThemeMode));
    }

    private Color GetGeneratedPrimaryColor(Color seed, bool isDark)
    {
        var scheme = MaterialSchemeGenerator.CreateScheme(
            seed,
            editor.Current.ThemeColorVariant,
            isDark);
        return MaterialColorMapper.ToAvaloniaColor(scheme.Primary);
    }

    private void OnPlatformColorValuesChanged(object? sender, PlatformColorValues values)
    {
        ApplyPlatformColorValues(values);
    }

    internal void ApplyPlatformColorValues(PlatformColorValues values)
    {
        if (editor.Current.ThemeColorMode != ThemeColorModes.System)
        {
            return;
        }

        var isDark = editor.Current.ThemeMode == ThemeModes.Dark
            || (editor.Current.ThemeMode == ThemeModes.System
                && values.ThemeVariant == PlatformThemeVariant.Dark);
        ApplyScheme(
            values.AccentColor1,
            editor.Current.ThemeColorVariant,
            isDark,
            editor.Current.NeutralColorStrategy);
        RefreshThemeColorPaletteBrushes(isDark);
        ThemeColorPreviewBrush = new SolidColorBrush(
            GetGeneratedPrimaryColor(values.AccentColor1, isDark));
    }

    private Color ResolveThemeColor(string themeColorMode, Color customColor) =>
        themeColorMode switch
        {
            ThemeColorModes.System => GetSystemAccentColor(),
            ThemeColorModes.Custom => customColor,
            ThemeColorModes.Wallpaper =>
                ResolveThemeColorFromPalette()
                ?? Color.Parse(LauncherConstants.DefaultThemeColor),
            _ => Color.Parse(LauncherConstants.DefaultThemeColor)
        };

    private Color? ResolveThemeColorFromPalette()
    {
        if (ThemeColorPaletteItems.Count == 0)
        {
            return null;
        }

        var selectedIndex = Math.Clamp(
            SelectedThemeColorPaletteIndex,
            0,
            ThemeColorPaletteItems.Count - 1);
        return ParseThemeColorPaletteColor(ThemeColorPaletteItems[selectedIndex].ColorHex);
    }

    public static void ApplyTheme(string themeMode)
    {
        var themeVariant = themeMode switch
        {
            ThemeModes.Light => ThemeVariant.Light,
            ThemeModes.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        if (Application.Current is { } application)
        {
            EnsureThemeSubscription(application);
            lastThemeMode = themeMode;
            application.RequestedThemeVariant = themeVariant;

            // M3: scheme roles are theme-dependent; re-apply the last scheme so a
            // theme-mode switch updates primary/secondary/tertiary and (optionally)
            // surface roles without requiring a separate colour edit.
            if (lastSchemeApplied)
            {
                ApplyScheme(
                    lastSchemeSeed,
                    lastSchemeVariant,
                    IsDarkTheme(themeMode),
                    lastSchemeStrategy);
            }
        }
    }

    internal static Color GetSystemAccentColor()
    {
        if (Application.Current?.TryGetResource(
                "SystemAccentColor",
                ThemeVariant.Default,
                out var value) == true
            && value is Color color)
        {
            return color;
        }

        return Color.Parse(LauncherConstants.DefaultThemeColor);
    }

    private static bool lastSchemeApplied;
    private static string lastThemeMode = ThemeModes.System;
    private static Color lastSchemeSeed = Color.Parse(LauncherConstants.DefaultThemeColor);
    private static string lastSchemeVariant = ThemeColorVariants.TonalSpot;
    private static string lastSchemeStrategy = NeutralColorStrategies.BrandBlue;

    /// <summary>
    /// Applies the M3 dynamic scheme derived from <paramref name="seed"/> onto the
    /// <c>Launcher.Color.*</c> brush keys (spec §3.4). Replaces the pre-M3
    /// <c>ApplyAccentBrushes</c>; the previous accent-family override remains a
    /// subset of <see cref="Services.MaterialSchemeGenerator.BuildRoleBrushes"/>.
    /// </summary>
    internal static void ApplyScheme(
        Color seed,
        string variant = ThemeColorVariants.TonalSpot,
        bool isDark = false,
        string neutralStrategy = NeutralColorStrategies.BrandBlue)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        var scheme = MaterialSchemeGenerator.CreateScheme(seed, variant, isDark);
        var roleBrushes = MaterialSchemeGenerator.BuildRoleBrushes(
            scheme,
            seedFollowingNeutrals: neutralStrategy == NeutralColorStrategies.SeedFollowing,
            isDark: isDark);
        foreach (var (key, brush) in roleBrushes)
        {
            SetBrush(application, key, brush.Color);
        }

        lastSchemeApplied = true;
        lastSchemeSeed = seed;
        lastSchemeVariant = variant;
        lastSchemeStrategy = neutralStrategy;
    }

    private static Application? themeApplication;

    private static void EnsureThemeSubscription(Application application)
    {
        if (ReferenceEquals(themeApplication, application))
        {
            return;
        }

        if (themeApplication is not null)
        {
            themeApplication.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        }

        themeApplication = application;
        themeApplication.ActualThemeVariantChanged += OnActualThemeVariantChanged;
    }

    private static void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        if (lastThemeMode != ThemeModes.System || !lastSchemeApplied)
        {
            return;
        }

        ApplyScheme(
            lastSchemeSeed,
            lastSchemeVariant,
            IsDarkTheme(ThemeModes.System),
            lastSchemeStrategy);
    }

    /// <summary>Resolves whether the effective theme is dark for a theme mode.</summary>
    internal static bool IsDarkTheme(string themeMode) =>
        themeMode == ThemeModes.Dark
        || (themeMode == ThemeModes.System
            && Application.Current is { } application
            && application.ActualThemeVariant == ThemeVariant.Dark);

    private static void SetBrush(Application application, string key, Color color)
    {
        // Mutate in place where a brush already exists (root or per-theme
        // dictionaries), so {DynamicResource} consumers observe the change.
        bool mutated = false;
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            if (application.Resources.TryGetResource(key, variant, out var themed)
                && themed is SolidColorBrush themedBrush)
            {
                themedBrush.Color = color;
                mutated = true;
            }
        }

        if (mutated)
        {
            return;
        }

        if (application.Resources.TryGetResource(
                key,
                ThemeVariant.Default,
                out var value)
            && value is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        application.Resources[key] = new SolidColorBrush(color);
    }

    internal static Color NormalizeAccentColorForUi(Color color) =>
        ColorUtils.NormalizeAccentColorForUi(color);

    internal static Color GetReadableOnAccentColor(Color color) =>
        ColorUtils.GetReadableOnAccentColor(color);

    internal static Color AdjustColor(Color color, double factor) =>
        ColorUtils.AdjustColor(color, factor);

    public static string ToColorHex(Color color) =>
        ThemeColorExtractionService.ToColorHex(color);

    public static Color ParseColorOrDefault(string? value) =>
        Color.TryParse(value, out var color)
            ? color
            : Color.Parse(LauncherConstants.DefaultThemeColor);

    internal static Color? ParseThemeColorPaletteColor(string? value) =>
        Color.TryParse(value, out var color)
            ? Color.FromArgb(0xFF, color.R, color.G, color.B)
            : null;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        editor.CurrentPropertyChanged -= OnCurrentSettingChanged;
        if (platformSettings is not null)
        {
            platformSettings.ColorValuesChanged -= OnPlatformColorValuesChanged;
        }
    }
}
