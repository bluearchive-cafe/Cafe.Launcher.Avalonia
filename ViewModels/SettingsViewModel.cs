using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly LauncherSettingsService settingsService;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly ImageCacheService? imageCacheService;
    private readonly ExternalLinkService? externalLinkService;
    private readonly DiskSpaceService diskSpaceService;
    private readonly LauncherUpdateService launcherUpdateService;
    private readonly ISettingsEditor editor;
    private bool suppressDirtyTracking;
    private bool disposed;

    // Coordination delegates — set by parent after construction.
    public Func<LauncherStatusSnapshot?>? GetSnapshot { get; set; }
    public Func<string, Task<string?>>? PickGameFolderAsync { get; set; }
    public Func<Task<string?>>? PickBackgroundImageAsync { get; set; }
    public Func<Task<string?>>? PickBackgroundFolderAsync { get; set; }
    public Func<LauncherSettings, Task>? ApplyLanguageAndTheme { get; set; }
    public Func<Bitmap?>? GetBackgroundBitmap { get; set; }

    // Events — parent subscribes to these.
    public event Func<Task>? SettingsSaved;
    public event Action? CloseRequested;

    /// <summary>
    /// The settings state editor. XAML binds to <c>Editor.Current.*</c> for
    /// setting values, and to ViewModel properties for option collections and UI state.
    /// </summary>
    public ISettingsEditor Editor => editor;

    public SettingsViewModel(
        LauncherSettingsService settingsService,
        LocalizationService localizer,
        ToastService toastService,
        ImageCacheService? imageCacheService,
        ExternalLinkService? externalLinkService,
        DiskSpaceService diskSpaceService,
        LauncherUpdateService launcherUpdateService,
        ISettingsEditor editor)
    {
        this.settingsService = settingsService;
        this.localizer = localizer;
        this.toastService = toastService;
        this.imageCacheService = imageCacheService;
        this.externalLinkService = externalLinkService;
        this.diskSpaceService = diskSpaceService;
        this.launcherUpdateService = launcherUpdateService;
        this.editor = editor;
        editor.PropertyChanged += OnEditorPropertyChanged;
        editor.CurrentPropertyChanged += OnCurrentSettingChanged;
    }

    private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ISettingsEditor.IsDirty))
        {
            OnPropertyChanged(nameof(IsSettingsDirty));
        }
    }

    // ── Settings UI state ────────────────────────────────────────────────

    public bool IsSettingsDirty => editor.IsDirty;

    [ObservableProperty]
    private bool isUnsavedChangesVisible;

    // ── Background UI projections ────────────────────────────────────────

    [ObservableProperty]
    private bool isCustomBackground;

    [ObservableProperty]
    private Color selectedBackgroundFillColor = Colors.Black;

    [ObservableProperty]
    private IBrush backgroundFillColorPreviewBrush = new SolidColorBrush(Colors.Black);

    [ObservableProperty]
    private bool isCustomBackgroundSelected;

    [ObservableProperty]
    private bool isBackgroundFitSelected;

    // ── Theme colour UI projections ──────────────────────────────────────

    [ObservableProperty]
    private Color selectedCustomThemeColor = Color.Parse(LauncherConstants.DefaultThemeColor);

    [ObservableProperty]
    private IBrush themeColorPreviewBrush = new SolidColorBrush(Color.Parse(LauncherConstants.DefaultThemeColor));

    [ObservableProperty]
    private bool isCustomThemeColorSelected;

    [ObservableProperty]
    private bool isWallpaperThemeColorSelected;

    [ObservableProperty]
    private int selectedThemeColorPaletteIndex;

    // ── Option collections (9) ────────────────────────────────────────────

    public ObservableCollection<SettingOption> BackgroundSourceOptions { get; } =
    [
        new() { Code = BackgroundSources.Bundled },
        new() { Code = BackgroundSources.Remote },
        new() { Code = BackgroundSources.Custom }
    ];

    public ObservableCollection<SettingOption> BackgroundFitOptions { get; } =
    [
        new() { Code = BackgroundFits.Fill },
        new() { Code = BackgroundFits.Uniform },
        new() { Code = BackgroundFits.UniformToFill }
    ];

    public ObservableCollection<SettingOption> ThemeColorOptions { get; } =
    [
        new() { Code = ThemeColorModes.Default },
        new() { Code = ThemeColorModes.System },
        new() { Code = ThemeColorModes.Wallpaper },
        new() { Code = ThemeColorModes.Custom }
    ];

    public ObservableCollection<SettingOption> LaunchCheckModeOptions { get; } =
    [
        new() { Code = LaunchCheckModes.LocalManifest },
        new() { Code = LaunchCheckModes.RemoteManifest },
        new() { Code = LaunchCheckModes.None }
    ];

    public ObservableCollection<SettingOption> ProxyModeOptions { get; } =
    [
        new() { Code = ProxyModes.Direct },
        new() { Code = ProxyModes.System }
    ];

    public ObservableCollection<SettingOption> PatchUrlGroupOptions { get; } =
    [
        new() { Code = PatchUrlGroups.Official },
        new() { Code = PatchUrlGroups.Cafe }
    ];

    public ObservableCollection<SettingOption> DownloadSpeedLimitOptions { get; } =
    [
        new() { Code = DownloadSpeedLimits.Unlimited },
        new() { Code = DownloadSpeedLimits.Speed1MBs },
        new() { Code = DownloadSpeedLimits.Speed5MBs },
        new() { Code = DownloadSpeedLimits.Speed10MBs },
        new() { Code = DownloadSpeedLimits.Speed25MBs },
        new() { Code = DownloadSpeedLimits.Speed50MBs }
    ];

    public ObservableCollection<SettingOption> CloseBehaviorOptions { get; } =
    [
        new() { Code = CloseBehaviors.Minimize },
        new() { Code = CloseBehaviors.Exit }
    ];

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } = LocalizationService.GetLanguageOptions();

    public ObservableCollection<SettingOption> UpdateChannelOptions { get; } =
    [
        new() { Code = UpdateChannels.Stable },
        new() { Code = UpdateChannels.Beta }
    ];

    public ObservableCollection<ThemeOption> ThemeOptions { get; } =
    [
        new() { Code = ThemeModes.System },
        new() { Code = ThemeModes.Light },
        new() { Code = ThemeModes.Dark }
    ];

    public ObservableCollection<ThemeColorPaletteItem> ThemeColorPaletteItems { get; } = [];

    // ── Public API for parent VM ──────────────────────────────────────────

    /// <summary>Called by parent when settings panel opens or discards changes.</summary>
    public void LoadFromSnapshot(LauncherSettings settings)
    {
        var currentWallpaperPalette = settings.ThemeColorMode == ThemeColorModes.Wallpaper
            ? GetThemeColorPaletteHexes()
            : [];
        var currentWallpaperPaletteIndex = editor.Current.SelectedThemeColorPaletteIndex;

        editor.ApplySnapshot(settings);
        RefreshUiProjections();

        if (editor.Current.ThemeColorMode == ThemeColorModes.Wallpaper)
        {
            if (currentWallpaperPalette.Count > 0)
            {
                ReplaceThemeColorPalette(currentWallpaperPalette, currentWallpaperPaletteIndex);
            }
            else
            {
                RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
            }
        }

    }

    /// <summary>Called by parent ApplyLanguage to refresh display names.</summary>
    public void RefreshOptionDisplayNames()
    {
        RefreshThemeOptions();
        RefreshThemeColorOptions();
        RefreshLaunchCheckModeOptions();
        RefreshProxyModeOptions();
        RefreshPatchUrlGroupOptions();
        RefreshCloseBehaviorOptions();
        RefreshDownloadSpeedLimitOptions();
        RefreshBackgroundSourceOptions();
        RefreshBackgroundFitOptions();
        RefreshUpdateChannelOptions();
    }

    /// <summary>Called by parent to re-apply theme/colour after language change.</summary>
    public void ApplyLanguageAndThemeState(LauncherSettings settings)
    {
        ApplyTheme(settings.ThemeMode);
        ApplyThemeColor(settings.ThemeColorMode, ParseColorOrDefault(settings.CustomThemeColor));
    }

    // ── Editor ↔ ViewModel property sync ─────────────────────────────────

    /// <summary>
    /// Copies all values from <see cref="Editor"/>.<see cref="ISettingsEditor.Current"/>
    /// into the ViewModel's observable properties. Called after
    /// <see cref="ISettingsEditor.ApplySnapshot"/> to keep the legacy property surface
    /// in sync for XAML bindings. Supresses dirty tracking during the sync.
    /// </summary>
    private void RefreshUiProjections()
    {
        var previous = suppressDirtyTracking;
        suppressDirtyTracking = true;
        try
        {
            var s = editor.Current;
            SelectedCustomThemeColor = ParseColorOrDefault(s.CustomThemeColor);
            IsCustomThemeColorSelected = s.ThemeColorMode == ThemeColorModes.Custom;
            IsWallpaperThemeColorSelected = s.ThemeColorMode == ThemeColorModes.Wallpaper;
            SelectedThemeColorPaletteIndex = s.SelectedThemeColorPaletteIndex;
            IsCustomBackground = !string.IsNullOrWhiteSpace(s.CustomBackgroundPath);
            IsBackgroundFitSelected = s.BackgroundFit == BackgroundFits.Uniform;
            SelectedBackgroundFillColor = ParseColorOrDefault(s.BackgroundFillColor);
            IsCustomBackgroundSelected = s.BackgroundSource == BackgroundSources.Custom;
            ReplaceThemeColorPalette(s.ThemeColorPalette, s.SelectedThemeColorPaletteIndex);
        }
        finally
        {
            suppressDirtyTracking = previous;
        }
    }

    public void ApplyLauncherSettings(LauncherSettings settings, string localGamePath)
    {
        editor.ApplySnapshot(settings);
        var snapshot = editor.GetSnapshot();
        snapshot.GamePath = localGamePath;
        editor.ApplySnapshot(snapshot);
        RefreshUiProjections();
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        var result = await launcherUpdateService.CheckForUpdateAsync(
            editor.Current.UpdateChannel,
            editor.Current.ProxyMode);

        if (!result.IsSuccessful)
        {
            toastService.ShowError(localizer.T("launcherUpdateCheckFailed"));
            return;
        }

        if (!result.IsUpdateAvailable)
        {
            toastService.ShowSuccess(localizer.F("launcherUpToDate", LauncherConstants.LauncherVersion));
            return;
        }

        toastService.ShowWarning(localizer.F("launcherUpdateAvailable", result.LatestVersion));
        externalLinkService?.Open(result.ReleaseUrl);
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (editor.Current.ThemeColorMode == ThemeColorModes.Wallpaper && ThemeColorPaletteItems.Count == 0)
        {
            RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
        }

        var previousSettings = await settingsService.ReadAsync();
        var snapshot = GetSnapshot?.Invoke();
        var previousPatchUrlGroup = previousSettings.PatchUrlGroup;
        var shouldPromptRepairAfterSourceChange = snapshot?.IsInstalled == true
            && !string.Equals(previousPatchUrlGroup, editor.Current.PatchUrlGroup, StringComparison.Ordinal);

        // Sync palette state (held in ViewModel's ObservableCollection) to the editor
        // before building the save snapshot.
        editor.Commit(s =>
        {
            s.ThemeColorPalette = GetThemeColorPaletteHexes();
            s.SelectedThemeColorPaletteIndex = SelectedThemeColorPaletteIndex;
        });

        // Assemble the settings to save from the editor's current state.
        var settings = editor.GetSnapshot();
        await settingsService.SaveAsync(settings);

        if (ApplyLanguageAndTheme is not null)
            await ApplyLanguageAndTheme(settings);
        else
            ApplyThemeColor(settings.ThemeColorMode, ParseColorOrDefault(settings.CustomThemeColor));

        editor.ApplySnapshot(settings);
        toastService.ShowSuccess(localizer.T("settingsSaved"));

        // Fire event so parent can refresh and show repair prompt if needed.
        if (SettingsSaved is not null)
            await SettingsSaved.Invoke();

        if (shouldPromptRepairAfterSourceChange)
        {
            // The repair prompt is shown by the parent VM; we just fire SettingsSaved
            // and let RefreshAsync handle it.
        }
    }

    [RelayCommand]
    private async Task ChooseGamePathAsync()
    {
        if (PickGameFolderAsync is null)
        {
            toastService.ShowWarning(localizer.T("folderPickerUnavailable"));
            return;
        }

        var pickedPath = await PickGameFolderAsync(editor.Current.GamePath);
        if (string.IsNullOrWhiteSpace(pickedPath))
        {
            return;
        }

        // Assemble from editor state (single source of truth).
        var settings = editor.GetSnapshot();
        settings.GamePath = pickedPath;
        await settingsService.SaveAsync(settings);

        if (ApplyLanguageAndTheme is not null)
            await ApplyLanguageAndTheme(settings);
        else
            ApplyThemeColor(settings.ThemeColorMode, ParseColorOrDefault(settings.CustomThemeColor));

        editor.ApplySnapshot(settings);
        RefreshUiProjections();
        toastService.ShowSuccess(localizer.F("pathSaved", pickedPath));
        if (SettingsSaved is not null)
            await SettingsSaved.Invoke();
    }

    [RelayCommand]
    private async Task ChooseBackgroundImageAsync()
    {
        if (PickBackgroundImageAsync is null)
            return;

        var pickedPath = await PickBackgroundImageAsync();
        if (string.IsNullOrWhiteSpace(pickedPath))
            return;

        editor.Current.CustomBackgroundPath = pickedPath;
        IsCustomBackground = true;
        editor.Current.BackgroundSource = BackgroundSources.Custom;
        IsCustomBackgroundSelected = true;
        await SaveSettingsAsync();
        toastService.ShowSuccess(localizer.T("backgroundSet"));
    }

    [RelayCommand]
    private async Task ChooseBackgroundFolderAsync()
    {
        if (PickBackgroundFolderAsync is null)
            return;

        var pickedPath = await PickBackgroundFolderAsync();
        if (string.IsNullOrWhiteSpace(pickedPath))
            return;

        editor.Current.CustomBackgroundPath = pickedPath;
        IsCustomBackground = true;
        editor.Current.BackgroundSource = BackgroundSources.Custom;
        IsCustomBackgroundSelected = true;
        await SaveSettingsAsync();
        toastService.ShowSuccess(localizer.T("backgroundSet"));
    }

    [RelayCommand]
    private async Task ClearBackgroundAsync()
    {
        editor.Current.CustomBackgroundPath = "";
        IsCustomBackground = false;
        editor.Current.BackgroundSource = BackgroundSources.Bundled;
        IsCustomBackgroundSelected = false;
        await SaveSettingsAsync();
        toastService.ShowSuccess(localizer.T("backgroundCleared"));
    }

    [RelayCommand]
    private void DiscardSettingsChanges()
    {
        IsUnsavedChangesVisible = false;
        editor.Discard();
        RefreshUiProjections();
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void KeepEditingSettings()
    {
        IsUnsavedChangesVisible = false;
    }

    [RelayCommand]
    private void RefreshThemeColorPalette()
    {
        RefreshThemeColorPaletteFromCurrentBackground(markDirty: true);
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

        SelectedThemeColorPaletteIndex = Math.Clamp(index, 0, ThemeColorPaletteItems.Count - 1);
    }

    // ── Dirty tracking ────────────────────────────────────────────────────

    private void PushToEditor(Action<LauncherSettings> apply)
    {
        if (suppressDirtyTracking)
            return;

        editor.Commit(apply);
    }

    private void OnCurrentSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherSettings.ThemeColorMode))
        {
            var value = editor.Current.ThemeColorMode;
            IsCustomThemeColorSelected = value == ThemeColorModes.Custom;
            IsWallpaperThemeColorSelected = value == ThemeColorModes.Wallpaper;
            if (IsWallpaperThemeColorSelected && ThemeColorPaletteItems.Count == 0)
            {
                RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
            }

            UpdateThemeColorPreview();
            return;
        }

        if (e.PropertyName == nameof(LauncherSettings.BackgroundSource))
        {
            IsCustomBackgroundSelected = editor.Current.BackgroundSource == BackgroundSources.Custom;
            return;
        }

        if (e.PropertyName == nameof(LauncherSettings.BackgroundFit))
        {
            IsBackgroundFitSelected = editor.Current.BackgroundFit == BackgroundFits.Uniform;
        }
    }

    partial void OnSelectedCustomThemeColorChanged(Color value)
    {
        PushToEditor(s => s.CustomThemeColor = ToColorHex(value));
        UpdateThemeColorPreview();
    }

    partial void OnSelectedThemeColorPaletteIndexChanged(int value)
    {
        PushToEditor(s => s.SelectedThemeColorPaletteIndex = value);
        UpdateThemeColorPaletteSelection();
        UpdateThemeColorPreview();
        if (editor.Current.ThemeColorMode == ThemeColorModes.Wallpaper)
        {
            ApplyThemeColor(editor.Current.ThemeColorMode, SelectedCustomThemeColor);
        }
    }

    partial void OnSelectedBackgroundFillColorChanged(Color value)
    {
        PushToEditor(s => s.BackgroundFillColor = ToColorHex(value));
        BackgroundFillColorPreviewBrush = new SolidColorBrush(value);
    }

    // ── Theme colour helpers ──────────────────────────────────────────────

    public void LoadThemeColorState(LauncherSettings settings)
    {
        var oldSuppress = suppressDirtyTracking;
        suppressDirtyTracking = true;
        try
        {
            var color = ParseColorOrDefault(settings.CustomThemeColor);
            SelectedCustomThemeColor = color;
            IsCustomThemeColorSelected = settings.ThemeColorMode == ThemeColorModes.Custom;
            IsWallpaperThemeColorSelected = settings.ThemeColorMode == ThemeColorModes.Wallpaper;
            ReplaceThemeColorPalette(settings.ThemeColorPalette, settings.SelectedThemeColorPaletteIndex);
        }
        finally
        {
            suppressDirtyTracking = oldSuppress;
        }
    }

    public void RefreshThemeColorPaletteFromCurrentBackground(bool markDirty)
    {
        var bitmap = GetBackgroundBitmap?.Invoke();
        if (bitmap is null)
        {
            ReplaceThemeColorPalette([], 0);
            return;
        }

        var colors = ThemeColorExtractionService.ExtractPalette(bitmap)
            .Select(ThemeColorExtractionService.ToColorHex)
            .ToArray();
        var selectedIndex = SelectedThemeColorPaletteIndex < colors.Length
            ? SelectedThemeColorPaletteIndex
            : 0;
        ReplaceThemeColorPalette(colors, selectedIndex);
        if (markDirty)
        {
            PushToEditor(s =>
            {
                s.ThemeColorPalette = GetThemeColorPaletteHexes();
                s.SelectedThemeColorPaletteIndex = SelectedThemeColorPaletteIndex;
            });
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
        var oldSuppress = suppressDirtyTracking;
        suppressDirtyTracking = true;
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
                    Brush = new SolidColorBrush(color)
                });
            }

            SelectedThemeColorPaletteIndex = normalizedColors.Length == 0
                ? 0
                : Math.Clamp(selectedIndex, 0, normalizedColors.Length - 1);
            UpdateThemeColorPaletteSelection();
        }
        finally
        {
            suppressDirtyTracking = oldSuppress;
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

    private List<string> GetThemeColorPaletteHexes() =>
        ThemeColorPaletteItems.Select(item => item.ColorHex).ToList();

    private void UpdateThemeColorPreview()
    {
        var color = ResolveThemeColor(editor.Current.ThemeColorMode, SelectedCustomThemeColor);
        ThemeColorPreviewBrush = new SolidColorBrush(color);
    }

    internal void ApplyThemeColor(string themeColorMode, Color customColor)
    {
        if (themeColorMode == ThemeColorModes.Wallpaper && ThemeColorPaletteItems.Count == 0)
        {
            RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
        }

        var color = ResolveThemeColor(themeColorMode, customColor);
        ThemeColorPreviewBrush = new SolidColorBrush(color);
        ApplyAccentBrushes(color);
    }

    private Color ResolveThemeColor(string themeColorMode, Color customColor)
    {
        return themeColorMode switch
        {
            ThemeColorModes.System => GetSystemAccentColor(),
            ThemeColorModes.Custom => customColor,
            ThemeColorModes.Wallpaper => ResolveThemeColorFromPalette() ?? Color.Parse(LauncherConstants.DefaultThemeColor),
            _ => Color.Parse(LauncherConstants.DefaultThemeColor)
        };
    }

    private Color? ResolveThemeColorFromPalette()
    {
        if (ThemeColorPaletteItems.Count == 0) return null;
        var selectedIndex = Math.Clamp(SelectedThemeColorPaletteIndex, 0, ThemeColorPaletteItems.Count - 1);
        return ParseThemeColorPaletteColor(ThemeColorPaletteItems[selectedIndex].ColorHex);
    }

    // ── Static theme / colour helpers ─────────────────────────────────────

    internal static void ApplyTheme(string themeMode)
    {
        var themeVariant = themeMode switch
        {
            ThemeModes.Light => ThemeVariant.Light,
            ThemeModes.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = themeVariant;
        }
    }

    internal static Color GetSystemAccentColor()
    {
        if (Application.Current?.TryGetResource("SystemAccentColor", ThemeVariant.Default, out var value) == true
            && value is Color color)
        {
            return color;
        }

        return Color.Parse(LauncherConstants.DefaultThemeColor);
    }

    internal static void ApplyAccentBrushes(Color color)
    {
        if (Application.Current is not { } application) return;

        SetBrush(application, "LauncherAccentBrush", color);
        SetBrush(application, "LauncherAccentHoverBrush", AdjustColor(color, 1.15));
        SetBrush(application, "LauncherAccentPressedBrush", AdjustColor(color, 0.85));
        SetBrush(application, "LauncherAccentSoftBrush", Color.FromArgb(0x24, color.R, color.G, color.B));
        SetBrush(application, "LauncherAccentBorderBrush", Color.FromArgb(0x80, color.R, color.G, color.B));
        SetBrush(application, "LauncherFocusRingBrush", Color.FromArgb(0x99, color.R, color.G, color.B));
        SetBrush(application, "LauncherCarouselDotActiveBrush", color);
        SetBrush(application, "LauncherToastInfoBrush", color);
        SetBrush(application, "LauncherOnAccentBrush", GetReadableOnAccentColor(color));
    }

    private static void SetBrush(Application application, string key, Color color)
    {
        if (application.Resources.TryGetResource(key, ThemeVariant.Default, out var value)
            && value is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        application.Resources[key] = new SolidColorBrush(color);
    }

    internal static Color AdjustColor(Color color, double factor)
    {
        static byte Adjust(byte value, double factor) =>
            (byte)Math.Clamp((int)Math.Round(value * factor), 0, 255);

        return Color.FromArgb(color.A, Adjust(color.R, factor), Adjust(color.G, factor), Adjust(color.B, factor));
    }

    internal static Color GetReadableOnAccentColor(Color color)
    {
        var luminance = (0.2126 * SrgbToLinear(color.R / 255d))
            + (0.7152 * SrgbToLinear(color.G / 255d))
            + (0.0722 * SrgbToLinear(color.B / 255d));
        return luminance > 0.45 ? Color.FromRgb(0x12, 0x18, 0x20) : Colors.White;
    }

    private static double SrgbToLinear(double value) =>
        value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    internal static string ToColorHex(Color color) =>
        ThemeColorExtractionService.ToColorHex(color);

    internal static Color ParseColorOrDefault(string? value) =>
        Color.TryParse(value, out var color) ? color : Color.Parse(LauncherConstants.DefaultThemeColor);

    internal static Color? ParseThemeColorPaletteColor(string? value) =>
        Color.TryParse(value, out var color) ? Color.FromArgb(0xFF, color.R, color.G, color.B) : null;

    // ── Display-name resolvers ────────────────────────────────────────────

    internal string ResolveLanguageDisplayName(string language)
    {
        return LanguageOptions.FirstOrDefault(option => option.Code == language)?.DisplayName
            ?? LanguageOptions.First(option => option.Code == LauncherLanguages.Auto).DisplayName;
    }

    internal string ResolveThemeDisplayName(string themeMode)
    {
        return ThemeOptions.FirstOrDefault(option => option.Code == themeMode)?.DisplayName
            ?? localizer.T("themeSystem");
    }

    internal string ResolveLaunchCheckDisplayName(string launchCheckMode)
    {
        return launchCheckMode switch
        {
            LaunchCheckModes.RemoteManifest => localizer.T("statusLaunchCheckRemote"),
            LaunchCheckModes.None => localizer.T("statusLaunchCheckNone"),
            _ => localizer.T("statusLaunchCheckLocal")
        };
    }

    internal string ResolveDiskSpaceText(string gamePath, string? requiredSize)
    {
        var required = string.IsNullOrWhiteSpace(requiredSize) ? "--" : requiredSize.Replace(" ", "", StringComparison.Ordinal);
        var availableBytes = diskSpaceService.GetAvailableBytes(gamePath);
        var available = availableBytes.HasValue ? FileSizeFormatter.Format(availableBytes.Value) : "--";
        return localizer.F("diskSpace", required, available);
    }

    // ── Option collection refresh helpers ─────────────────────────────────

    private void RefreshThemeOptions()
    {
        foreach (var option in ThemeOptions)
        {
            option.DisplayName = option.Code switch
            {
                ThemeModes.Light => localizer.T("themeLight"),
                ThemeModes.Dark => localizer.T("themeDark"),
                _ => localizer.T("themeSystem")
            };
        }
    }

    internal void RefreshThemeColorOptions()
    {
        foreach (var option in ThemeColorOptions)
        {
            option.DisplayName = option.Code switch
            {
                ThemeColorModes.System => localizer.T("themeColorSystem"),
                ThemeColorModes.Wallpaper => localizer.T("themeColorWallpaper"),
                ThemeColorModes.Custom => localizer.T("themeColorCustom"),
                _ => localizer.T("themeColorDefault")
            };
        }
    }

    private void RefreshLaunchCheckModeOptions()
    {
        foreach (var option in LaunchCheckModeOptions)
        {
            option.DisplayName = option.Code switch
            {
                LaunchCheckModes.RemoteManifest => localizer.T("launchCheckRemoteManifest"),
                LaunchCheckModes.None => localizer.T("launchCheckNone"),
                _ => localizer.T("launchCheckLocalManifest")
            };
        }
    }

    private void RefreshProxyModeOptions()
    {
        foreach (var option in ProxyModeOptions)
        {
            option.DisplayName = option.Code switch
            {
                ProxyModes.System => localizer.T("proxySystem"),
                _ => localizer.T("proxyDirect")
            };
        }
    }

    private void RefreshPatchUrlGroupOptions()
    {
        foreach (var option in PatchUrlGroupOptions)
        {
            option.DisplayName = option.Code switch
            {
                PatchUrlGroups.Cafe => localizer.T("downloadSourceCafe"),
                _ => localizer.T("downloadSourceOfficial")
            };
        }
    }

    private void RefreshCloseBehaviorOptions()
    {
        foreach (var option in CloseBehaviorOptions)
        {
            option.DisplayName = option.Code switch
            {
                CloseBehaviors.Exit => localizer.T("closeBehaviorExit"),
                _ => localizer.T("closeBehaviorMinimize")
            };
        }
    }

    private void RefreshDownloadSpeedLimitOptions()
    {
        foreach (var option in DownloadSpeedLimitOptions)
        {
            option.DisplayName = option.Code switch
            {
                DownloadSpeedLimits.Speed1MBs => localizer.T("speed1MBs"),
                DownloadSpeedLimits.Speed5MBs => localizer.T("speed5MBs"),
                DownloadSpeedLimits.Speed10MBs => localizer.T("speed10MBs"),
                DownloadSpeedLimits.Speed25MBs => localizer.T("speed25MBs"),
                DownloadSpeedLimits.Speed50MBs => localizer.T("speed50MBs"),
                _ => localizer.T("speedUnlimited")
            };
        }
    }

    private void RefreshBackgroundSourceOptions()
    {
        foreach (var option in BackgroundSourceOptions)
        {
            option.DisplayName = option.Code switch
            {
                BackgroundSources.Remote => localizer.T("backgroundSourceRemote"),
                BackgroundSources.Custom => localizer.T("backgroundSourceCustom"),
                _ => localizer.T("backgroundSourceBundled")
            };
        }
    }

    private void RefreshBackgroundFitOptions()
    {
        foreach (var option in BackgroundFitOptions)
        {
            option.DisplayName = option.Code switch
            {
                BackgroundFits.Fill => localizer.T("backgroundFitFill"),
                BackgroundFits.Uniform => localizer.T("backgroundFitUniform"),
                _ => localizer.T("backgroundFitUniformToFill")
            };
        }
    }

    private void RefreshUpdateChannelOptions()
    {
        foreach (var option in UpdateChannelOptions)
        {
            option.DisplayName = option.Code switch
            {
                UpdateChannels.Beta => localizer.T("updateChannelBeta"),
                _ => localizer.T("updateChannelStable")
            };
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        editor.PropertyChanged -= OnEditorPropertyChanged;
        editor.CurrentPropertyChanged -= OnCurrentSettingChanged;
    }
}
