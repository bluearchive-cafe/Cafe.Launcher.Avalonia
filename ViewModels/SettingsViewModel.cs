using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly LauncherSettingsService settingsService;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly ImageCacheService? imageCacheService;
    private readonly LauncherUpdateService launcherUpdateService;
    private readonly DialogsViewModel dialogs;
    private readonly ISettingsEditor editor;
    private bool disposed;

    // Coordination delegates — set by parent after construction.
    public Func<LauncherStatusSnapshot?>? GetSnapshot { get; set; }
    public Func<string, Task<string?>>? PickGameFolderAsync { get; set; }
    public Func<Task<string?>>? PickBackgroundImageAsync { get; set; }
    public Func<Task<string?>>? PickBackgroundFolderAsync { get; set; }
    public Func<LauncherSettings, Task>? ApplyLanguageAndTheme { get; set; }

    // Events — parent subscribes to these.
    public event Func<Task>? SettingsSaved;
    public event Action? CloseRequested;

    /// <summary>
    /// The settings state editor. XAML binds to <c>Editor.Current.*</c> for
    /// setting values, and to ViewModel properties for option collections and UI state.
    /// </summary>
    public ISettingsEditor Editor => editor;
    public SettingsOptionsViewModel Options { get; }
    public SettingsAppearanceViewModel Appearance { get; }

    public SettingsViewModel(
        LauncherSettingsService settingsService,
        LocalizationService localizer,
        ToastService toastService,
        ImageCacheService? imageCacheService,
        LauncherUpdateService launcherUpdateService,
        DialogsViewModel dialogs,
        SettingsOptionsViewModel options,
        SettingsAppearanceViewModel appearance)
    {
        this.settingsService = settingsService;
        this.localizer = localizer;
        this.toastService = toastService;
        this.imageCacheService = imageCacheService;
        this.launcherUpdateService = launcherUpdateService;
        this.dialogs = dialogs;
        editor = appearance.Editor;
        Options = options;
        Appearance = appearance;
        editor.PropertyChanged += OnEditorPropertyChanged;
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

    // ── Public API for parent VM ──────────────────────────────────────────

    /// <summary>Called by parent when settings panel opens or discards changes.</summary>
    public void LoadFromSnapshot(LauncherSettings settings)
    {
        var currentWallpaperPalette = settings.ThemeColorMode == ThemeColorModes.Wallpaper
            ? Appearance.GetThemeColorPaletteHexes()
            : [];
        var currentWallpaperPaletteIndex = Appearance.SelectedThemeColorPaletteIndex;

        editor.ApplySnapshot(settings);
        Appearance.Load(settings);

        if (editor.Current.ThemeColorMode == ThemeColorModes.Wallpaper)
        {
            if (currentWallpaperPalette.Count > 0)
            {
                var snapshot = editor.GetSnapshot();
                snapshot.ThemeColorPalette = currentWallpaperPalette;
                snapshot.SelectedThemeColorPaletteIndex = currentWallpaperPaletteIndex;
                Appearance.Load(snapshot);
            }
            else
            {
                Appearance.RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
            }
        }

    }

    /// <summary>Called by parent ApplyLanguage to refresh display names.</summary>
    public void RefreshOptionDisplayNames()
    {
        Options.RefreshDisplayNames();
    }

    public void ApplyLauncherSettings(LauncherSettings settings, string localGamePath)
    {
        editor.ApplySnapshot(settings);
        var snapshot = editor.GetSnapshot();
        snapshot.GamePath = localGamePath;
        editor.ApplySnapshot(snapshot);
        Appearance.Load(snapshot);
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
            toastService.ShowSuccess(localizer.F("launcherUpToDate", BuildInfo.LauncherVersion));
            return;
        }

        dialogs.ShowUpdateAvailable(result.LatestVersion, result.Files);
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (editor.Current.ThemeColorMode == ThemeColorModes.Wallpaper
            && Appearance.ThemeColorPaletteItems.Count == 0)
        {
            Appearance.RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
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
            s.ThemeColorPalette = Appearance.GetThemeColorPaletteHexes();
            s.SelectedThemeColorPaletteIndex = Appearance.SelectedThemeColorPaletteIndex;
        });

        // Assemble the settings to save from the editor's current state.
        var settings = editor.GetSnapshot();
        await settingsService.SaveAsync(settings);

        if (ApplyLanguageAndTheme is not null)
            await ApplyLanguageAndTheme(settings);
        else
            Appearance.ApplyThemeColor(
                settings.ThemeColorMode,
                SettingsAppearanceViewModel.ParseColorOrDefault(settings.CustomThemeColor));

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
            Appearance.ApplyThemeColor(
                settings.ThemeColorMode,
                SettingsAppearanceViewModel.ParseColorOrDefault(settings.CustomThemeColor));

        editor.ApplySnapshot(settings);
        Appearance.Load(settings);
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
        editor.Current.BackgroundSource = BackgroundSources.Custom;
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
        editor.Current.BackgroundSource = BackgroundSources.Custom;
        await SaveSettingsAsync();
        toastService.ShowSuccess(localizer.T("backgroundSet"));
    }

    [RelayCommand]
    private async Task ClearBackgroundAsync()
    {
        editor.Current.CustomBackgroundPath = "";
        editor.Current.BackgroundSource = BackgroundSources.Bundled;
        await SaveSettingsAsync();
        toastService.ShowSuccess(localizer.T("backgroundCleared"));
    }

    [RelayCommand]
    private void DiscardSettingsChanges()
    {
        IsUnsavedChangesVisible = false;
        editor.Discard();
        Appearance.Load(editor.Current);
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void KeepEditingSettings()
    {
        IsUnsavedChangesVisible = false;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        editor.PropertyChanged -= OnEditorPropertyChanged;
        Appearance.Dispose();
    }
}
