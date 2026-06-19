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

public partial class SettingsViewModel : ViewModelBase
{
    private readonly LauncherSettingsService settingsService;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly ImageCacheService? imageCacheService;
    private readonly ExternalLinkService? externalLinkService;
    private readonly DiskSpaceService diskSpaceService;
    private readonly LauncherUpdateService launcherUpdateService;
    private bool suppressSettingsDirty;

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

    public SettingsViewModel(
        LauncherSettingsService settingsService,
        LocalizationService localizer,
        ToastService toastService,
        ImageCacheService? imageCacheService,
        ExternalLinkService? externalLinkService,
        DiskSpaceService diskSpaceService,
        LauncherUpdateService launcherUpdateService)
    {
        this.settingsService = settingsService;
        this.localizer = localizer;
        this.toastService = toastService;
        this.imageCacheService = imageCacheService;
        this.externalLinkService = externalLinkService;
        this.diskSpaceService = diskSpaceService;
        this.launcherUpdateService = launcherUpdateService;
    }

    // ── Settings values (11) ─────────────────────────────────────────────

    [ObservableProperty]
    private string selectedGamePath = "";

    [ObservableProperty]
    private string selectedLaunchCheckMode = LaunchCheckModes.LocalManifest;

    [ObservableProperty]
    private string selectedProxyMode = ProxyModes.Direct;

    [ObservableProperty]
    private string selectedPatchUrlGroup = PatchUrlGroups.Official;

    [ObservableProperty]
    private string selectedCloseBehavior = CloseBehaviors.Minimize;

    [ObservableProperty]
    private string selectedLanguage = LauncherLanguages.Auto;

    [ObservableProperty]
    private string selectedThemeMode = ThemeModes.System;

    [ObservableProperty]
    private string selectedDownloadSpeedLimit = DownloadSpeedLimits.Unlimited;

    [ObservableProperty]
    private bool toastNotificationsEnabled = true;

    [ObservableProperty]
    private bool showRemoteContentCard = true;

    [ObservableProperty]
    private string selectedUpdateChannel = UpdateChannels.Stable;

    // ── Settings UI state (2) ────────────────────────────────────────────

    // I1: Dirty tracking for unsaved settings changes
    [ObservableProperty]
    private bool isSettingsDirty;

    // M4: Unsaved changes confirmation dialog
    [ObservableProperty]
    private bool isUnsavedChangesVisible;

    // ── Background (4) ────────────────────────────────────────────────────

    [ObservableProperty]
    private string customBackgroundPath = "";

    [ObservableProperty]
    private bool isCustomBackground;

    [ObservableProperty]
    private string selectedBackgroundSource = BackgroundSources.Bundled;

    [ObservableProperty]
    private string selectedBackgroundFit = BackgroundFits.UniformToFill;

    [ObservableProperty]
    private Color selectedBackgroundFillColor = Colors.Black;

    [ObservableProperty]
    private IBrush backgroundFillColorPreviewBrush = new SolidColorBrush(Colors.Black);

    [ObservableProperty]
    private bool isCustomBackgroundSelected;

    [ObservableProperty]
    private bool isBackgroundFitSelected;

    // ── Theme colour state (7) ────────────────────────────────────────────

    [ObservableProperty]
    private string selectedThemeColorMode = ThemeColorModes.Default;

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
        var oldSuppress = suppressSettingsDirty;
        suppressSettingsDirty = true;
        try
        {
            var currentWallpaperPalette = settings.ThemeColorMode == ThemeColorModes.Wallpaper
                ? GetThemeColorPaletteHexes()
                : [];
            var currentWallpaperPaletteIndex = SelectedThemeColorPaletteIndex;
            SelectedGamePath = settings.GamePath;
            SelectedLaunchCheckMode = settings.LaunchCheckMode;
            SelectedProxyMode = settings.ProxyMode;
            SelectedPatchUrlGroup = settings.PatchUrlGroup;
            SelectedCloseBehavior = settings.CloseBehavior;
            SelectedLanguage = settings.Language;
            SelectedThemeMode = settings.ThemeMode;
            LoadThemeColorState(settings);
            SelectedDownloadSpeedLimit = settings.DownloadSpeedLimit;
            ToastNotificationsEnabled = settings.ToastNotificationsEnabled;
            ShowRemoteContentCard = settings.ShowRemoteContentCard;
            SelectedUpdateChannel = settings.UpdateChannel;
            CustomBackgroundPath = settings.CustomBackgroundPath;
            IsCustomBackground = !string.IsNullOrWhiteSpace(settings.CustomBackgroundPath);
            SelectedBackgroundSource = settings.BackgroundSource;
            SelectedBackgroundFit = settings.BackgroundFit;
            IsBackgroundFitSelected = settings.BackgroundFit == BackgroundFits.Uniform;
            SelectedBackgroundFillColor = ParseColorOrDefault(settings.BackgroundFillColor);
            IsCustomBackgroundSelected = settings.BackgroundSource == BackgroundSources.Custom;
            if (SelectedThemeColorMode == ThemeColorModes.Wallpaper)
            {
                if (currentWallpaperPalette.Count > 0)
                {
                    ReplaceThemeColorPalette(currentWallpaperPalette, currentWallpaperPaletteIndex, markDirty: false);
                }
                else
                {
                    RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
                }
            }
        }
        finally
        {
            suppressSettingsDirty = oldSuppress;
        }

        IsSettingsDirty = false;
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

    // ── Bulk update ─────────────────────────────────────────────────────

    /// <summary>
    /// Wraps programmatic bulk property updates so they don't trigger dirty
    /// tracking. Follows the same save/restore pattern as
    /// <see cref="LoadThemeColorState"/> and <see cref="ReplaceThemeColorPalette"/>.
    /// </summary>
    public void BulkUpdate(Action<SettingsViewModel> update)
    {
        var previous = suppressSettingsDirty;
        suppressSettingsDirty = true;
        try
        {
            update(this);
        }
        finally
        {
            suppressSettingsDirty = previous;
        }
    }

    /// <summary>
    /// Applies launcher settings from a snapshot — always programmatic,
    /// should never mark dirty.
    /// </summary>
    public void ApplyLauncherSettings(LauncherSettings settings, string localGamePath)
    {
        BulkUpdate(s =>
        {
            s.SelectedGamePath = localGamePath;
            s.SelectedLaunchCheckMode = settings.LaunchCheckMode;
            s.SelectedProxyMode = settings.ProxyMode;
            s.SelectedPatchUrlGroup = settings.PatchUrlGroup;
            s.SelectedCloseBehavior = settings.CloseBehavior;
            s.SelectedLanguage = settings.Language;
            s.SelectedThemeMode = settings.ThemeMode;
            s.LoadThemeColorState(settings);
            s.SelectedDownloadSpeedLimit = settings.DownloadSpeedLimit;
            s.ToastNotificationsEnabled = settings.ToastNotificationsEnabled;
            s.ShowRemoteContentCard = settings.ShowRemoteContentCard;
            s.SelectedUpdateChannel = settings.UpdateChannel;
            s.CustomBackgroundPath = settings.CustomBackgroundPath;
            s.IsCustomBackground = !string.IsNullOrWhiteSpace(settings.CustomBackgroundPath);
            s.SelectedBackgroundSource = settings.BackgroundSource;
            s.SelectedBackgroundFit = settings.BackgroundFit;
            s.IsBackgroundFitSelected = settings.BackgroundFit == BackgroundFits.Uniform;
            s.SelectedBackgroundFillColor = ParseColorOrDefault(settings.BackgroundFillColor);
            s.IsCustomBackgroundSelected = settings.BackgroundSource == BackgroundSources.Custom;
        });
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        launcherUpdateService.SetProxyMode(SelectedProxyMode);
        var result = await launcherUpdateService.CheckForUpdateAsync(SelectedUpdateChannel);

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
        if (SelectedThemeColorMode == ThemeColorModes.Wallpaper && ThemeColorPaletteItems.Count == 0)
        {
            RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
        }

        var settings = await settingsService.ReadAsync();
        var snapshot = GetSnapshot?.Invoke();
        var previousPatchUrlGroup = settings.PatchUrlGroup;
        var shouldPromptRepairAfterSourceChange = snapshot?.IsInstalled == true
            && !string.Equals(previousPatchUrlGroup, SelectedPatchUrlGroup, StringComparison.Ordinal);
        settings.GamePath = SelectedGamePath;
        settings.LaunchCheckMode = SelectedLaunchCheckMode;
        settings.ProxyMode = SelectedProxyMode;
        settings.PatchUrlGroup = SelectedPatchUrlGroup;
        settings.CloseBehavior = SelectedCloseBehavior;
        settings.Language = SelectedLanguage;
        settings.ThemeMode = SelectedThemeMode;
        settings.ThemeColorMode = SelectedThemeColorMode;
        settings.CustomThemeColor = ToColorHex(SelectedCustomThemeColor);
        settings.ThemeColorPalette = GetThemeColorPaletteHexes();
        settings.SelectedThemeColorPaletteIndex = SelectedThemeColorPaletteIndex;
        settings.DownloadSpeedLimit = SelectedDownloadSpeedLimit;
        settings.ToastNotificationsEnabled = ToastNotificationsEnabled;
        settings.ShowRemoteContentCard = ShowRemoteContentCard;
        settings.UpdateChannel = SelectedUpdateChannel;
        settings.CustomBackgroundPath = CustomBackgroundPath;
        settings.BackgroundSource = SelectedBackgroundSource;
        settings.BackgroundFit = SelectedBackgroundFit;
        settings.BackgroundFillColor = ToColorHex(SelectedBackgroundFillColor);
        await settingsService.SaveAsync(settings);

        if (ApplyLanguageAndTheme is not null)
            await ApplyLanguageAndTheme(settings);
        else
            ApplyThemeColor(settings.ThemeColorMode, ParseColorOrDefault(settings.CustomThemeColor));

        IsSettingsDirty = false;
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

        var pickedPath = await PickGameFolderAsync(SelectedGamePath);
        if (string.IsNullOrWhiteSpace(pickedPath))
        {
            return;
        }

        var settings = await settingsService.ReadAsync();
        settings.GamePath = pickedPath;
        settings.LaunchCheckMode = SelectedLaunchCheckMode;
        settings.ProxyMode = SelectedProxyMode;
        settings.PatchUrlGroup = SelectedPatchUrlGroup;
        settings.CloseBehavior = SelectedCloseBehavior;
        settings.Language = SelectedLanguage;
        settings.ThemeMode = SelectedThemeMode;
        settings.ThemeColorMode = SelectedThemeColorMode;
        settings.CustomThemeColor = ToColorHex(SelectedCustomThemeColor);
        settings.ThemeColorPalette = GetThemeColorPaletteHexes();
        settings.SelectedThemeColorPaletteIndex = SelectedThemeColorPaletteIndex;
        await settingsService.SaveAsync(settings);

        if (ApplyLanguageAndTheme is not null)
            await ApplyLanguageAndTheme(settings);
        else
            ApplyThemeColor(settings.ThemeColorMode, ParseColorOrDefault(settings.CustomThemeColor));

        BulkUpdate(s => s.SelectedGamePath = pickedPath);
        IsSettingsDirty = false;
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

        CustomBackgroundPath = pickedPath;
        IsCustomBackground = true;
        SelectedBackgroundSource = BackgroundSources.Custom;
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

        CustomBackgroundPath = pickedPath;
        IsCustomBackground = true;
        SelectedBackgroundSource = BackgroundSources.Custom;
        IsCustomBackgroundSelected = true;
        await SaveSettingsAsync();
        toastService.ShowSuccess(localizer.T("backgroundSet"));
    }

    [RelayCommand]
    private async Task ClearBackgroundAsync()
    {
        CustomBackgroundPath = "";
        IsCustomBackground = false;
        SelectedBackgroundSource = BackgroundSources.Bundled;
        IsCustomBackgroundSelected = false;
        await SaveSettingsAsync();
        toastService.ShowSuccess(localizer.T("backgroundCleared"));
    }

    [RelayCommand]
    private void DiscardSettingsChanges()
    {
        IsUnsavedChangesVisible = false;
        IsSettingsDirty = false;
        var snapshot = GetSnapshot?.Invoke();
        if (snapshot is { } s)
        {
            LoadFromSnapshot(s.Settings);
        }

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
        if (SelectedThemeColorMode == ThemeColorModes.Wallpaper)
        {
            ApplyThemeColor(SelectedThemeColorMode, SelectedCustomThemeColor);
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

    private void MarkSettingsDirtyIfVisible()
    {
        if (suppressSettingsDirty)
            return;

        if (!IsSettingsDirty)
            IsSettingsDirty = true;
    }

    partial void OnSelectedLaunchCheckModeChanged(string value) => MarkSettingsDirtyIfVisible();
    partial void OnSelectedProxyModeChanged(string value) => MarkSettingsDirtyIfVisible();
    partial void OnSelectedPatchUrlGroupChanged(string value) => MarkSettingsDirtyIfVisible();
    partial void OnSelectedCloseBehaviorChanged(string value) => MarkSettingsDirtyIfVisible();
    partial void OnSelectedLanguageChanged(string value) => MarkSettingsDirtyIfVisible();
    partial void OnSelectedDownloadSpeedLimitChanged(string value) => MarkSettingsDirtyIfVisible();
    partial void OnSelectedGamePathChanged(string value) => MarkSettingsDirtyIfVisible();
    partial void OnToastNotificationsEnabledChanged(bool value) => MarkSettingsDirtyIfVisible();

    partial void OnSelectedThemeModeChanged(string value)
    {
        MarkSettingsDirtyIfVisible();
    }

    partial void OnSelectedThemeColorModeChanged(string value)
    {
        IsCustomThemeColorSelected = value == ThemeColorModes.Custom;
        IsWallpaperThemeColorSelected = value == ThemeColorModes.Wallpaper;
        if (IsWallpaperThemeColorSelected && ThemeColorPaletteItems.Count == 0)
        {
            RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
        }

        UpdateThemeColorPreview();
        MarkSettingsDirtyIfVisible();
    }

    partial void OnSelectedCustomThemeColorChanged(Color value)
    {
        UpdateThemeColorPreview();
        MarkSettingsDirtyIfVisible();
    }

    partial void OnSelectedThemeColorPaletteIndexChanged(int value)
    {
        UpdateThemeColorPaletteSelection();
        UpdateThemeColorPreview();
        if (SelectedThemeColorMode == ThemeColorModes.Wallpaper)
        {
            ApplyThemeColor(SelectedThemeColorMode, SelectedCustomThemeColor);
        }

        MarkSettingsDirtyIfVisible();
    }

    partial void OnSelectedBackgroundSourceChanged(string value)
    {
        IsCustomBackgroundSelected = value == BackgroundSources.Custom;
        MarkSettingsDirtyIfVisible();
    }

    partial void OnSelectedBackgroundFitChanged(string value)
    {
        IsBackgroundFitSelected = value == BackgroundFits.Uniform;
        MarkSettingsDirtyIfVisible();
    }

    partial void OnSelectedBackgroundFillColorChanged(Color value)
    {
        BackgroundFillColorPreviewBrush = new SolidColorBrush(value);
        MarkSettingsDirtyIfVisible();
    }

    partial void OnShowRemoteContentCardChanged(bool value)
    {
        MarkSettingsDirtyIfVisible();
    }

    partial void OnSelectedUpdateChannelChanged(string value)
    {
        MarkSettingsDirtyIfVisible();
    }

    // ── Theme colour helpers ──────────────────────────────────────────────

    public void LoadThemeColorState(LauncherSettings settings)
    {
        var oldSuppressSettingsDirty = suppressSettingsDirty;
        suppressSettingsDirty = true;
        try
        {
            var color = ParseColorOrDefault(settings.CustomThemeColor);
            SelectedThemeColorMode = settings.ThemeColorMode;
            SelectedCustomThemeColor = color;
            IsCustomThemeColorSelected = settings.ThemeColorMode == ThemeColorModes.Custom;
            IsWallpaperThemeColorSelected = settings.ThemeColorMode == ThemeColorModes.Wallpaper;
            ReplaceThemeColorPalette(settings.ThemeColorPalette, settings.SelectedThemeColorPaletteIndex, markDirty: false);
        }
        finally
        {
            suppressSettingsDirty = oldSuppressSettingsDirty;
        }
    }

    public void RefreshThemeColorPaletteFromCurrentBackground(bool markDirty)
    {
        var bitmap = GetBackgroundBitmap?.Invoke();
        if (bitmap is null)
        {
            ReplaceThemeColorPalette([], 0, markDirty);
            return;
        }

        var colors = ThemeColorExtractionService.ExtractPalette(bitmap)
            .Select(ThemeColorExtractionService.ToColorHex)
            .ToArray();
        var selectedIndex = SelectedThemeColorPaletteIndex < colors.Length
            ? SelectedThemeColorPaletteIndex
            : 0;
        ReplaceThemeColorPalette(colors, selectedIndex, markDirty);
    }

    private void ReplaceThemeColorPalette(IEnumerable<string> colors, int selectedIndex, bool markDirty)
    {
        var normalizedColors = colors
            .Select(ParseThemeColorPaletteColor)
            .OfType<Color>()
            .Select(ThemeColorExtractionService.ToColorHex)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var oldColors = ThemeColorPaletteItems.Select(item => item.ColorHex).ToArray();
        var oldSelectedIndex = SelectedThemeColorPaletteIndex;
        var oldSuppressSettingsDirty = suppressSettingsDirty;
        suppressSettingsDirty = true;
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
            suppressSettingsDirty = oldSuppressSettingsDirty;
        }

        UpdateThemeColorPreview();
        if (markDirty
            && (!oldColors.SequenceEqual(normalizedColors, StringComparer.Ordinal)
                || oldSelectedIndex != SelectedThemeColorPaletteIndex))
        {
            MarkSettingsDirtyIfVisible();
        }
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
        var color = ResolveThemeColor(SelectedThemeColorMode, SelectedCustomThemeColor);
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
}
