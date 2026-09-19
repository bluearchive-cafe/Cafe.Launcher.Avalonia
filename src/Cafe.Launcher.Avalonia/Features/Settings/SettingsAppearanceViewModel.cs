using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Features.Settings;

public partial class SettingsAppearanceViewModel : ViewModelBase, IDisposable
{
    private readonly ISettingsEditor editor;
    private readonly ThemeApplier themeApplier;
    private readonly IPlatformSettings? platformSettings;
    private readonly LocalDiagnostics? diagnostics;
    private readonly bool showHiddenSettings;
    private bool suppressEditorUpdates;
    private bool disposed;
    private readonly LatestRefresh themePaletteRefresh = new();

    public SettingsAppearanceViewModel(
        ISettingsEditor editor,
        ThemeApplier themeApplier,
        LocalDiagnostics? diagnostics = null,
        bool showHiddenSettings = false)
    {
        this.editor = editor;
        this.themeApplier = themeApplier;
        this.diagnostics = diagnostics;
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
            SelectedCustomThemeColor = ColorUtils.ParseColorOrDefault(settings.CustomThemeColor);
            IsCustomThemeColorSelected = settings.ThemeColorMode == ThemeColorModes.Custom;
            IsWallpaperThemeColorSelected = settings.ThemeColorMode == ThemeColorModes.Wallpaper;
            IsThemeColorExtractionAlgorithmVisible = IsWallpaperThemeColorSelected;
            IsSeedFollowingNeutralStrategySelected =
                settings.NeutralColorStrategy == NeutralColorStrategies.SeedFollowing;
            IsCustomBackground = !string.IsNullOrWhiteSpace(settings.CustomBackgroundPath);
            IsBackgroundFitSelected = settings.BackgroundFit == BackgroundFits.Uniform;
            SelectedBackgroundFillColor = ColorUtils.ParseColorOrDefault(settings.BackgroundFillColor);
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

    /// <summary>最近一次取色任务；保存流程用它等待当前壁纸色板落定，测试也可观察。</summary>
    internal Task PendingThemeRefresh => themePaletteRefresh.Pending;

    /// <summary>
    /// 等待最新取色任务落定的总预算；超时按当前色板继续，取色本身降采样到 64px 后
    /// 量化属快速有界操作，该上限只防御理论上无界的刷新链。测试可调小。
    /// </summary>
    internal static TimeSpan ThemeRefreshSettleTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// 从当前壁纸重新提取主题色板。提取（含整幅源图降采样与量化）在线程池执行，
    /// 结果经代数校验后回到 UI 线程应用；期间壁纸可能再次切换并释放旧位图，
    /// 陈旧结果与已释放位图都被静默丢弃，不会覆盖较新的色板。
    /// </summary>
    public Task RefreshThemeColorPaletteFromCurrentBackgroundAsync(
        bool markDirty,
        bool applySchemeAfter = false)
    {
        if (disposed)
        {
            return Task.CompletedTask;
        }

        themePaletteRefresh.Run(null, token => RefreshThemeColorPaletteSafelyAsync(markDirty, applySchemeAfter, token));
        return themePaletteRefresh.Pending;
    }

    /// <summary>
    /// 保存前的壁纸色板落定：仅壁纸取色模式需要——等待最新取色任务；色板仍为空时
    /// （例如刚切到壁纸取色、首次提取尚未产出）再主动提取一次。取色期间会有意保留
    /// 旧色板，因此不能以 Count == 0 判断是否仍在取色——等待与补提取的判据都在
    /// 这一处收拢。
    /// </summary>
    public async Task EnsureThemePaletteReadyForSaveAsync()
    {
        if (editor.Current.ThemeColorMode != ThemeColorModes.Wallpaper)
        {
            return;
        }

        await TaskSettler.WaitAsync(() => themePaletteRefresh.Pending, ThemeRefreshSettleTimeout);
        if (ThemeColorPaletteItems.Count == 0)
        {
            await RefreshThemeColorPaletteFromCurrentBackgroundAsync(markDirty: false);
        }
    }

    private async Task RefreshThemeColorPaletteSafelyAsync(
        bool markDirty,
        bool applySchemeAfter,
        CancellationToken cancellationToken)
    {
        // 「已过期」的判据从代数换成令牌：刷新槽换新或 Dispose 都会取消在飞的令牌。
        try
        {
            var bitmap = GetBackgroundBitmap?.Invoke();
            if (bitmap is null)
            {
                await RunOnUiAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested || disposed)
                    {
                        return;
                    }

                    ReplaceThemeColorPalette([], 0);
                    if (applySchemeAfter)
                    {
                        ApplyResolvedTheme();
                    }
                });
                return;
            }

            var algorithm = editor.Current.ThemeColorExtractionAlgorithm;
            var colors = await Task.Run(() => ThemeColorExtractionService.ExtractPalette(
                bitmap,
                algorithm));

            if (cancellationToken.IsCancellationRequested || disposed)
            {
                return;
            }

            var hexes = colors.Select(ThemeColorExtractionService.ToColorHex).ToArray();
            var selectedIndex = SelectedThemeColorPaletteIndex < hexes.Length
                ? SelectedThemeColorPaletteIndex
                : 0;
            await RunOnUiAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested || disposed)
                {
                    return;
                }

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
        catch (ObjectDisposedException)
        {
            // 提取期间壁纸再次切换会释放旧位图；丢弃本轮即可。
        }
        catch (Exception ex)
        {
            // 覆盖图片读取、后台提取、UI 调度与结果应用的完整边界，保证所有
            // fire-and-forget 调用都不会泄漏未观察异常。
            _ = diagnostics?.WarningAsync(
                "ThemeColor",
                $"Theme color refresh failed: {ex.Message}",
                CancellationToken.None);
        }
    }

    private static async Task RunOnUiAsync(Action action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
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

    /// <summary>
    /// 从设置快照应用外观：主题模式与主题色。三处调用点此前各写一遍同一对语句
    /// （<see cref="ApplyTheme"/> 加一次 <see cref="ApplyThemeColor"/>，后者还要就地解析自定义色）。
    /// </summary>
    /// <remarks>
    /// 接缝内的 <c>ShellLifecycle.ApplySnapshotAsync</c> 刻意不走这里：它在两次应用之间更新背景图，
    /// 顺序是有意的，不能合并成一个调用。
    /// </remarks>
    public void ApplyFrom(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ApplyTheme(settings.ThemeMode);
        ApplyThemeColor(settings.ThemeColorMode, ColorUtils.ParseColorOrDefault(settings.CustomThemeColor));
    }

    /// <summary>
    /// 应用主题模式。落色与系统变体订阅由 <see cref="Services.ThemeApplier"/> 承担；本方法留在
    /// VM 上是因为壳层把「应用这份快照的外观」当作设置外观的公开入口，
    /// 而 <c>ShellLifecycle.ApplySnapshotAsync</c> 需要在模式与主题色之间插入背景图更新。
    /// </summary>
    public void ApplyTheme(string themeMode) => themeApplier.ApplyThemeMode(themeMode);

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
        themeApplier.ApplyScheme(
            color,
            editor.Current.ThemeColorVariant,
            ThemeApplier.IsDarkTheme(editor.Current.ThemeMode),
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
        PushToEditor(settings => settings.CustomThemeColor = ThemeColorExtractionService.ToColorHex(value));
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
        PushToEditor(settings => settings.BackgroundFillColor = ThemeColorExtractionService.ToColorHex(value));
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
                var color = ColorUtils.ParseColorOrDefault(normalizedColors[i]);
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
        return GetGeneratedPrimaryColor(seed, ThemeApplier.IsDarkTheme(editor.Current.ThemeMode));
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
        themeApplier.ApplyScheme(
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
            ThemeColorModes.System => ThemeApplier.GetSystemAccentColor(),
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
        themePaletteRefresh.Cancel();
        editor.CurrentPropertyChanged -= OnCurrentSettingChanged;
        if (platformSettings is not null)
        {
            platformSettings.ColorValuesChanged -= OnPlatformColorValuesChanged;
        }
    }
}
