using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.GameRuntime;
using Cafe.Launcher.Avalonia.ViewModels;
using Serilog.Events;

namespace Cafe.Launcher.Avalonia.Features.Settings;

public partial class SettingsViewModel : ViewModelBase, IDisposable, IModalContentViewModel
{
    private readonly LauncherSettingsService settingsService;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly LauncherUpdateService launcherUpdateService;
    private readonly DialogsViewModel dialogs;
    private readonly ISettingsEditor editor;
    private readonly UnifiedLogger unifiedLogger;
    private readonly GameInstallationPath gameInstallationPath;
    private readonly IErrorHandlingService errorHandling;
    private readonly IGameRuntime gameRuntime;
    private readonly IFilePickerService filePickerService;
    private CancellationTokenSource? appearancePreviewCts;
    private Task appearancePreviewTask = Task.CompletedTask;
    private CancellationTokenSource? gameRuntimeStatusCts;
    private Task gameRuntimeStatusRefreshTask = Task.CompletedTask;
    private IReadOnlyList<GameRuntimeStatusEntry>? gameRuntimeStatusEntries;
    private bool disposed;

    // Coordination delegates — set by parent after construction.
    public Func<LauncherSettings, Task>? ApplyLanguageAndTheme { get; set; }
    public Func<LauncherSettings, string?, CancellationToken, Task>? PreviewAppearanceAsync { get; set; }

    // Events — parent subscribes to these.
    public event Func<Task>? SettingsSaved;

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
        LauncherUpdateService launcherUpdateService,
        DialogsViewModel dialogs,
        UnifiedLogger unifiedLogger,
        GameInstallationPath gameInstallationPath,
        SettingsOptionsViewModel options,
        SettingsAppearanceViewModel appearance,
        IErrorHandlingService errorHandling,
        IGameRuntime gameRuntime,
        IFilePickerService filePickerService)
    {
        this.settingsService = settingsService;
        this.localizer = localizer;
        this.toastService = toastService;
        this.launcherUpdateService = launcherUpdateService;
        this.dialogs = dialogs;
        this.unifiedLogger = unifiedLogger;
        this.gameInstallationPath = gameInstallationPath;
        editor = appearance.Editor;
        Options = options;
        Appearance = appearance;
        this.errorHandling = errorHandling;
        this.gameRuntime = gameRuntime;
        this.filePickerService = filePickerService;
        editor.PropertyChanged += OnEditorPropertyChanged;
        editor.CurrentPropertyChanged += OnCurrentSettingChanged;
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ISettingsEditor.IsDirty))
        {
            OnPropertyChanged(nameof(IsSettingsDirty));
            OnPropertyChanged(nameof(CanSaveSettings));
            SaveSettingsCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(ISettingsEditor.Current))
        {
            OnPropertyChanged(nameof(IsGameRuntimeRunnerPathEnabled));
        }
    }

    private void OnCurrentSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameRuntimeSettings.Runner))
        {
            OnPropertyChanged(nameof(IsGameRuntimeRunnerPathEnabled));
        }

        if (!IsAppearanceSetting(e.PropertyName))
        {
            return;
        }

        RequestAppearancePreview(e.PropertyName);
    }

    // ── Settings UI state ────────────────────────────────────────────────

    public bool IsSettingsDirty => editor.IsDirty;

    public bool CanSaveSettings => IsSettingsDirty && !IsSaving;

    /// <summary>A custom executable path is only applied to an explicitly selected runner.</summary>
    public bool IsGameRuntimeRunnerPathEnabled =>
        editor.Current.GameRuntime.Runner is GameRuntimeRunners.Umu or GameRuntimeRunners.Wine;

    [ObservableProperty]
    private bool isUnsavedChangesVisible;

    [ObservableProperty]
    private bool isSaving;

    /// <summary>
    /// Multi-line per-runner availability summary for the Linux runtime section,
    /// e.g. "UMU / Proton: Available · /usr/bin/umu-run · 1.4.4".
    /// </summary>
    [ObservableProperty]
    private string gameRuntimeStatusSummary = string.Empty;

    internal Task? PendingGameRuntimeStatusRefresh => gameRuntimeStatusRefreshTask;

    private string selectedCategory = SettingsCategoryCodes.General;

    public string SelectedCategory
    {
        get => selectedCategory;
        set
        {
            if (!SetProperty(ref selectedCategory, SettingsCategoryCodes.Normalize(value)))
            {
                return;
            }

            OnPropertyChanged(nameof(IsGeneralCategorySelected));
            OnPropertyChanged(nameof(IsGameCategorySelected));
            OnPropertyChanged(nameof(IsDownloadNetworkCategorySelected));
            OnPropertyChanged(nameof(IsAppearanceCategorySelected));
            OnPropertyChanged(nameof(IsAdvancedCategorySelected));
            OnPropertyChanged(nameof(IsAboutCategorySelected));
            if (selectedCategory == SettingsCategoryCodes.Game)
            {
                RefreshGameRuntimeStatus();
            }
        }
    }

    public bool IsGeneralCategorySelected => SelectedCategory == SettingsCategoryCodes.General;
    public bool IsGameCategorySelected => SelectedCategory == SettingsCategoryCodes.Game;
    public bool IsDownloadNetworkCategorySelected => SelectedCategory == SettingsCategoryCodes.DownloadNetwork;
    public bool IsAppearanceCategorySelected => SelectedCategory == SettingsCategoryCodes.Appearance;
    public bool IsAdvancedCategorySelected => SelectedCategory == SettingsCategoryCodes.Advanced;
    public bool IsAboutCategorySelected => SelectedCategory == SettingsCategoryCodes.About;

    internal Task PendingAppearancePreview => appearancePreviewTask;

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

        RefreshGameRuntimeStatus();
    }

    /// <summary>Called by parent ApplyLanguage to refresh display names.</summary>
    public void RefreshOptionDisplayNames()
    {
        Options.RefreshDisplayNames();
        if (gameRuntimeStatusEntries is not null)
        {
            GameRuntimeStatusSummary = BuildGameRuntimeStatusSummary(gameRuntimeStatusEntries);
        }
    }

    /// <summary>Loads a persisted settings snapshot into the active edit session.</summary>
    public void ApplyLauncherSettings(LauncherSettings settings)
    {
        editor.ApplySnapshot(settings);
        var snapshot = editor.GetSnapshot();
        Appearance.Load(snapshot);
        ApplyLogLevel(snapshot.LogLevel);
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        var savedSettings = editor.GetSavedSnapshot();
        var result = await launcherUpdateService.CheckForUpdateAsync(
            savedSettings.UpdateChannel,
            savedSettings.ProxyMode);

        if (!result.IsSuccessful)
        {
            var operationMessage = localizer.T(LocalizationKeys.LauncherUpdateCheckFailed);
            var message = result.FailureException is not null
                ? ErrorHandlingService.FormatToastMessage(operationMessage, result.FailureException)
                : string.IsNullOrWhiteSpace(result.FailureMessage)
                    ? operationMessage
                    : $"{operationMessage}：{result.FailureMessage}";
            toastService.ShowError(message);
            return;
        }

        if (!result.IsUpdateAvailable)
        {
            toastService.ShowSuccess(localizer.F(LocalizationKeys.LauncherUpdateUpToDate, BuildInfo.LauncherVersion));
            return;
        }

        dialogs.ShowUpdateAvailable(result.LatestVersion, result.Files);
    }

    /// <summary>Opens the shared launcher-settings reset confirmation (shell performs the reset).</summary>
    [RelayCommand]
    private void RequestResetSettings() => dialogs.ShowSettingsResetConfirmation();

    [RelayCommand(CanExecute = nameof(CanSaveSettings))]
    private async Task SaveSettingsAsync()
    {
        CancelAppearancePreview();
        IsSaving = true;
        try
        {
            if (editor.Current.ThemeColorMode == ThemeColorModes.Wallpaper
                && Appearance.ThemeColorPaletteItems.Count == 0)
            {
                Appearance.RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
            }

            editor.Commit(s =>
            {
                s.ThemeColorPalette = Appearance.GetThemeColorPaletteHexes();
                s.SelectedThemeColorPaletteIndex = Appearance.SelectedThemeColorPaletteIndex;
            });

            var settings = editor.GetSnapshot();
            await settingsService.SaveAsync(settings);
            ApplyLogLevel(settings.LogLevel);

            if (ApplyLanguageAndTheme is not null)
                await ApplyLanguageAndTheme(settings);
            else
                Appearance.ApplyThemeColor(
                    settings.ThemeColorMode,
                    SettingsAppearanceViewModel.ParseColorOrDefault(settings.CustomThemeColor));

            editor.ApplySnapshot(settings);
            toastService.ShowSuccess(localizer.T(LocalizationKeys.SettingsSaved));
            RefreshGameRuntimeStatus();

            await AsyncEvent.InvokeSequentiallyAsync(SettingsSaved);
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Settings save failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(LocalizationKeys.SettingsSaveFailed, exception.Message) });
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task ChooseGamePathAsync()
    {
        var pickedPath = await filePickerService.PickFolderAsync(
            localizer.T(LocalizationKeys.ChooseInstallFolder),
            editor.Current.GamePath);
        if (string.IsNullOrWhiteSpace(pickedPath))
        {
            return;
        }

        // Normalise: append YostarGames/BlueArchive_JP subdirectory if missing,
        // matching the original Yostar launcher behaviour.
        editor.Current.GamePath = gameInstallationPath.NormalizeGamePath(pickedPath);
    }

    [RelayCommand]
    private async Task ChangePersistedGamePathAsync()
    {
        var settings = await settingsService.ReadAsync();
        await PickAndPersistGamePathAsync(settings.GamePath);
    }

    [RelayCommand]
    private async Task SelectInstalledGameAsync()
    {
        var startPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await PickAndPersistGamePathAsync(startPath);
    }

    private async Task PickAndPersistGamePathAsync(string startPath)
    {
        var pickedPath = await filePickerService.PickFolderAsync(
            localizer.T(LocalizationKeys.ChooseInstallFolder),
            startPath);
        if (string.IsNullOrWhiteSpace(pickedPath))
        {
            return;
        }

        // Normalise: append YostarGames/BlueArchive_JP if missing.
        pickedPath = gameInstallationPath.NormalizeGamePath(pickedPath);

        try
        {
            var settings = await settingsService.ReadAsync();
            settings.GamePath = pickedPath;
            await settingsService.SaveAsync(settings);
            editor.ApplySnapshot(settings);
            toastService.ShowSuccess(localizer.T(LocalizationKeys.GamePathUpdated));

            await AsyncEvent.InvokeSequentiallyAsync(SettingsSaved);
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Settings game path update failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(LocalizationKeys.GamePathUpdateFailed, exception.Message) });
        }
    }

    [RelayCommand]
    private async Task ChooseBackgroundImageAsync()
    {
        var pickedPath = await filePickerService.PickImageFileAsync(
            localizer.T(LocalizationKeys.ChooseBackgroundImageTitle));
        if (string.IsNullOrWhiteSpace(pickedPath))
            return;

        editor.Current.CustomBackgroundPath = pickedPath;
        editor.Current.BackgroundSource = BackgroundSources.Custom;
    }

    [RelayCommand]
    private async Task ChooseBackgroundFolderAsync()
    {
        var pickedPath = await filePickerService.PickFolderAsync(
            localizer.T(LocalizationKeys.ChooseBackgroundFolderTitle));
        if (string.IsNullOrWhiteSpace(pickedPath))
            return;

        editor.Current.CustomBackgroundPath = pickedPath;
        editor.Current.BackgroundSource = BackgroundSources.Custom;
    }

    [RelayCommand]
    private void ClearBackground()
    {
        editor.Current.CustomBackgroundPath = "";
        editor.Current.BackgroundSource = BackgroundSources.Bundled;
    }

    // ── Game runtime status (Linux section) ───────────────────────────────

    /// <summary>
    /// Kicks off a fire-and-forget availability refresh for the runtime status row.
    /// Runs the real version probes, so it must stay off the save/open critical path.
    /// </summary>
    public void RefreshGameRuntimeStatus()
    {
        if (disposed)
        {
            return;
        }

        gameRuntimeStatusCts?.Cancel();
        gameRuntimeStatusCts?.Dispose();
        gameRuntimeStatusCts = new CancellationTokenSource();
        var cancellationToken = gameRuntimeStatusCts.Token;
        gameRuntimeStatusRefreshTask = RefreshGameRuntimeStatusAsync(cancellationToken);
    }

    private async Task RefreshGameRuntimeStatusAsync(CancellationToken cancellationToken)
    {
        GameRuntimeStatusSummary = localizer.T(LocalizationKeys.GameRuntimeStatusChecking);
        try
        {
            var runtimeConfiguration = GameRuntimeConfiguration.FromSettings(editor.GetSnapshot().GameRuntime);
            var entries = await gameRuntime
                .GetStatusesAsync(runtimeConfiguration, cancellationToken)
                .ConfigureAwait(true);
            gameRuntimeStatusEntries = entries;
            GameRuntimeStatusSummary = BuildGameRuntimeStatusSummary(entries);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // Status is informational; launch diagnostics remain the authoritative
            // runtime failure report, so a refresh failure only degrades this row.
            Debug.WriteLine($"Settings: game runtime status refresh failed: {exception.Message}");
        }
    }

    internal string BuildGameRuntimeStatusSummary(IReadOnlyList<GameRuntimeStatusEntry> entries)
    {
        var visibleRunnerIds = Options.GameRuntimeRunner.Select(option => option.Code).ToHashSet();
        return string.Join(
            Environment.NewLine,
            entries.Where(entry => visibleRunnerIds.Contains(entry.RunnerId))
                .Select(FormatGameRuntimeStatusEntry));
    }

    private string FormatGameRuntimeStatusEntry(GameRuntimeStatusEntry entry)
    {
        var name = entry.RunnerId switch
        {
            GameRuntimeRunners.Umu => localizer.T(LocalizationKeys.GameRuntimeRunnerUmu),
            GameRuntimeRunners.Wine => localizer.T(LocalizationKeys.GameRuntimeRunnerWine),
            GameRuntimeRunners.Native => localizer.T(LocalizationKeys.GameRuntimeRunnerNative),
            _ => entry.RunnerId
        };
        var status = entry.Availability.Status switch
        {
            GameRunnerAvailabilityStatus.Available => localizer.T(LocalizationKeys.GameRuntimeStatusAvailable),
            GameRunnerAvailabilityStatus.NotFound => localizer.T(LocalizationKeys.GameRuntimeStatusNotFound),
            GameRunnerAvailabilityStatus.Broken => localizer.T(LocalizationKeys.GameRuntimeStatusBroken),
            _ => localizer.T(LocalizationKeys.GameRuntimeStatusUnsupported)
        };

        var path = entry.Availability.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return localizer.F(LocalizationKeys.GameRuntimeStatusEntryFormat, name, status);
        }

        var detail = string.IsNullOrWhiteSpace(entry.Availability.Version)
            ? path
            : localizer.F(LocalizationKeys.GameRuntimeStatusDetailFormat, path, entry.Availability.Version);
        return localizer.F(LocalizationKeys.GameRuntimeStatusEntryDetailFormat, name, status, detail);
    }

    public async Task DiscardChangesAsync()
    {
        IsUnsavedChangesVisible = false;
        CancelAppearancePreview();
        editor.Discard();
        Appearance.Load(editor.Current);
        appearancePreviewCts = new CancellationTokenSource();
        appearancePreviewTask = PreviewCurrentAppearanceAsync(null, appearancePreviewCts.Token);
        await appearancePreviewTask;
    }

    public void KeepEditing()
    {
        IsUnsavedChangesVisible = false;
    }

    private void RequestAppearancePreview(string? propertyName)
    {
        CancelAppearancePreview();
        appearancePreviewCts = new CancellationTokenSource();
        appearancePreviewTask = PreviewCurrentAppearanceAsync(propertyName, appearancePreviewCts.Token);
    }

    private void CancelAppearancePreview()
    {
        appearancePreviewCts?.Cancel();
        appearancePreviewCts?.Dispose();
        appearancePreviewCts = null;
    }

    private async Task PreviewCurrentAppearanceAsync(
        string? propertyName,
        CancellationToken cancellationToken)
    {
        if (PreviewAppearanceAsync is null)
        {
            return;
        }

        try
        {
            await PreviewAppearanceAsync(editor.GetSnapshot(), propertyName, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Settings appearance preview failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(LocalizationKeys.AppearancePreviewFailed, exception.Message) });
        }
    }

    private static bool IsAppearanceSetting(string? propertyName) =>
        propertyName is nameof(LauncherSettings.ThemeMode)
            or nameof(LauncherSettings.ThemeColorMode)
            or nameof(LauncherSettings.ThemeColorExtractionAlgorithm)
            or nameof(LauncherSettings.ThemeColorVariant)
            or nameof(LauncherSettings.NeutralColorStrategy)
            or nameof(LauncherSettings.CustomThemeColor)
            or nameof(LauncherSettings.ThemeColorPalette)
            or nameof(LauncherSettings.SelectedThemeColorPaletteIndex)
            or nameof(LauncherSettings.BackgroundSource)
            or nameof(LauncherSettings.CustomBackgroundPath)
            or nameof(LauncherSettings.BackgroundFit)
            or nameof(LauncherSettings.BackgroundFillColor);

    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSaveSettings));
        SaveSettingsCommand.NotifyCanExecuteChanged();
    }

    private void ApplyLogLevel(string logLevelCode)
    {
        try
        {
            var level = logLevelCode switch
            {
                LogLevels.Verbose => LogEventLevel.Verbose,
                LogLevels.Debug => LogEventLevel.Debug,
                LogLevels.Warning => LogEventLevel.Warning,
                LogLevels.Error => LogEventLevel.Error,
                LogLevels.Fatal => LogEventLevel.Fatal,
                _ => LogEventLevel.Information
            };
            unifiedLogger.SetMinimumLevel(level);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Settings: failed to apply log level: {ex.Message}");
            // Best-effort — log level application must never disrupt settings flow.
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CancelAppearancePreview();
        gameRuntimeStatusCts?.Cancel();
        gameRuntimeStatusCts?.Dispose();
        gameRuntimeStatusCts = null;
        editor.PropertyChanged -= OnEditorPropertyChanged;
        editor.CurrentPropertyChanged -= OnCurrentSettingChanged;
        Appearance.Dispose();
    }
}
