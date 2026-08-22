using System;
using System.ComponentModel;
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
using Cafe.Launcher.Avalonia.ViewModels;
using Serilog.Events;

namespace Cafe.Launcher.Avalonia.Features.Settings;

public partial class SettingsViewModel : ViewModelBase, IDisposable, IModalContentViewModel
{
    private const int AutoSaveDebounceMilliseconds = 400;
    private readonly LauncherSettingsService settingsService;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly LauncherUpdateService launcherUpdateService;
    private readonly DialogsViewModel dialogs;
    private readonly ISettingsEditor editor;
    private readonly UnifiedLogger unifiedLogger;
    private readonly GameInstallationPath gameInstallationPath;
    private readonly IErrorHandlingService errorHandling;
    private readonly SemaphoreSlim settingsSaveGate = new(1, 1);
    private readonly object autoSaveSync = new();
    private CancellationTokenSource? appearancePreviewCts;
    private Task appearancePreviewTask = Task.CompletedTask;
    private Task runtimeApplyTask = Task.CompletedTask;
    private Task pendingAutoSaveTask = Task.CompletedTask;
    private CancellationTokenSource? autoSaveDelayCts;
    private bool isAutoSaveQueued;
    private bool isAutoSaveFlushRequested;
    private bool disposed;

    // Coordination delegates — set by parent after construction.
    public Func<string, Task<string?>>? PickGameFolderAsync { get; set; }
    public Func<Task<string?>>? PickBackgroundImageAsync { get; set; }
    public Func<Task<string?>>? PickBackgroundFolderAsync { get; set; }
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
        IErrorHandlingService errorHandling)
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
    }

    private void OnCurrentSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsAutoSaveEnabled && editor.IsDirty)
        {
            QueueAutoSave();
        }

        if (IsAppearanceSetting(e.PropertyName))
        {
            RequestAppearancePreview(e.PropertyName);
        }
        else if (e.PropertyName == nameof(LauncherSettings.Language))
        {
            RequestRuntimeLanguageAndThemeApply();
        }
        else if (e.PropertyName == nameof(LauncherSettings.LogLevel))
        {
            ApplyLogLevel(editor.Current.LogLevel);
        }
    }

    // ── Settings UI state ────────────────────────────────────────────────

    public bool IsSettingsDirty => editor.IsDirty;

    public bool CanSaveSettings => IsSettingsDirty && !IsSaving;

    public bool IsAutoSaveEnabled { get; set; }

    [ObservableProperty]
    private bool hasAutoSaveFailure;

    [ObservableProperty]
    private string autoSaveFailureMessage = "";

    [ObservableProperty]
    private bool isUnsavedChangesVisible;

    [ObservableProperty]
    private bool isSaving;

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
        }
    }

    public bool IsGeneralCategorySelected => SelectedCategory == SettingsCategoryCodes.General;
    public bool IsGameCategorySelected => SelectedCategory == SettingsCategoryCodes.Game;
    public bool IsDownloadNetworkCategorySelected => SelectedCategory == SettingsCategoryCodes.DownloadNetwork;
    public bool IsAppearanceCategorySelected => SelectedCategory == SettingsCategoryCodes.Appearance;
    public bool IsAdvancedCategorySelected => SelectedCategory == SettingsCategoryCodes.Advanced;
    public bool IsAboutCategorySelected => SelectedCategory == SettingsCategoryCodes.About;

    internal Task PendingAppearancePreview => appearancePreviewTask;
    internal Task PendingRuntimeApply => runtimeApplyTask;
    internal Task PendingAutoSave => pendingAutoSaveTask;

    /// <summary>
    /// Persists the latest pending settings snapshot without waiting for the debounce delay.
    /// Closing an edit session calls this method so a recent edit cannot be replaced by the
    /// last saved snapshot when the panel is reopened.
    /// </summary>
    public async Task FlushPendingAutoSaveAsync()
    {
        if (disposed || !editor.IsDirty)
        {
            return;
        }

        Task task;
        lock (autoSaveSync)
        {
            isAutoSaveQueued = true;
            isAutoSaveFlushRequested = true;
            autoSaveDelayCts?.Cancel();
            if (pendingAutoSaveTask.IsCompleted)
            {
                pendingAutoSaveTask = ProcessAutoSaveQueueAsync();
            }

            task = pendingAutoSaveTask;
        }

        await task;
    }

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

    /// <summary>Loads a persisted settings snapshot into the active edit session.</summary>
    public void ApplyLauncherSettings(LauncherSettings settings)
    {
        editor.ApplySnapshot(settings);
        var snapshot = editor.GetSnapshot();
        Appearance.Load(snapshot);
        ApplyLogLevel(snapshot.LogLevel);
    }

    // ── Commands ──────────────────────────────────────────────────────────

    /// <summary>
    /// Uses the shared destructive-action confirmation before resetting the complete
    /// launcher configuration. The shell owns the reset so every runtime consumer
    /// reloads the same persisted defaults.
    /// </summary>
    [RelayCommand]
    private void RequestResetSettings()
    {
        dialogs.ShowDebugResetConfirmation();
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        var savedSettings = editor.GetSavedSnapshot();
        var result = await launcherUpdateService.CheckForUpdateAsync(
            savedSettings.UpdateChannel,
            savedSettings.ProxyMode);

        if (!result.IsSuccessful)
        {
            var operationMessage = localizer.T("launcherUpdateCheckFailed");
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
            toastService.ShowSuccess(localizer.F("launcherUpdateUpToDate", BuildInfo.LauncherVersion));
            return;
        }

        dialogs.ShowUpdateAvailable(result.LatestVersion, result.Files);
    }

    [RelayCommand(CanExecute = nameof(CanSaveSettings))]
    private async Task SaveSettingsAsync()
    {
        await PersistCurrentSettingsAsync(showSuccessToast: true);
    }

    private void QueueAutoSave()
    {
        if (disposed)
        {
            return;
        }

        lock (autoSaveSync)
        {
            isAutoSaveQueued = true;
            autoSaveDelayCts?.Cancel();
            autoSaveDelayCts = new CancellationTokenSource();
            if (!pendingAutoSaveTask.IsCompleted)
            {
                return;
            }

            pendingAutoSaveTask = ProcessAutoSaveQueueAsync();
        }
    }

    private async Task ProcessAutoSaveQueueAsync()
    {
        while (true)
        {
            CancellationToken delayToken;
            var skipDebounce = false;
            lock (autoSaveSync)
            {
                if (!isAutoSaveQueued)
                {
                    return;
                }

                isAutoSaveQueued = false;
                skipDebounce = isAutoSaveFlushRequested;
                isAutoSaveFlushRequested = false;
                delayToken = autoSaveDelayCts?.Token ?? CancellationToken.None;
            }

            if (!skipDebounce)
            {
                try
                {
                    await Task.Delay(AutoSaveDebounceMilliseconds, delayToken);
                }
                catch (OperationCanceledException) when (delayToken.IsCancellationRequested)
                {
                    continue;
                }
            }

            await PersistCurrentSettingsAsync(showSuccessToast: false);
        }
    }

    private async Task PersistCurrentSettingsAsync(bool showSuccessToast)
    {
        await settingsSaveGate.WaitAsync();
        try
        {
            if (!editor.IsDirty)
            {
                return;
            }

            CancelAppearancePreview();
            IsSaving = true;
            if (editor.Current.ThemeColorMode == ThemeColorModes.Wallpaper
                && Appearance.ThemeColorPaletteItems.Count == 0)
            {
                Appearance.RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
            }

            editor.Commit(current =>
            {
                current.ThemeColorPalette = Appearance.GetThemeColorPaletteHexes();
                current.SelectedThemeColorPaletteIndex = Appearance.SelectedThemeColorPaletteIndex;
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

            editor.MarkSaved(settings);
            HasAutoSaveFailure = false;
            AutoSaveFailureMessage = "";

            if (showSuccessToast)
            {
                toastService.ShowSuccess(localizer.T("settingsSaved"));
            }

            await AsyncEvent.InvokeSequentiallyAsync(SettingsSaved);
        }
        catch (Exception exception)
        {
            HasAutoSaveFailure = true;
            AutoSaveFailureMessage = localizer.F("settingsSaveFailed", exception.Message);
            await errorHandling.HandleErrorAsync("Settings save failed.", exception,
                new ErrorHandlingOptions { ToastMessage = AutoSaveFailureMessage });
        }
        finally
        {
            IsSaving = false;
            settingsSaveGate.Release();
        }
    }

    [RelayCommand]
    private async Task RetryAutoSaveAsync()
    {
        await FlushPendingAutoSaveAsync();
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
        if (PickGameFolderAsync is null)
        {
            toastService.ShowWarning(localizer.T("folderPickerUnavailable"));
            return;
        }

        var pickedPath = await PickGameFolderAsync(startPath);
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
            toastService.ShowSuccess(localizer.T("gamePathUpdated"));

            await AsyncEvent.InvokeSequentiallyAsync(SettingsSaved);
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Settings game path update failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F("gamePathUpdateFailed", exception.Message) });
        }
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
    }

    [RelayCommand]
    private void ClearBackground()
    {
        editor.Current.CustomBackgroundPath = "";
        editor.Current.BackgroundSource = BackgroundSources.Bundled;
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

    private void RequestRuntimeLanguageAndThemeApply()
    {
        runtimeApplyTask = ApplyRuntimeLanguageAndThemeAsync(editor.GetSnapshot());
    }

    private async Task ApplyRuntimeLanguageAndThemeAsync(LauncherSettings settings)
    {
        if (ApplyLanguageAndTheme is null)
        {
            return;
        }

        try
        {
            await ApplyLanguageAndTheme(settings);
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Settings runtime apply failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F("settingsSaveFailed", exception.Message) });
        }
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
                new ErrorHandlingOptions { ToastMessage = localizer.F("appearancePreviewFailed", exception.Message) });
        }
    }

    private static bool IsAppearanceSetting(string? propertyName) =>
        propertyName is nameof(LauncherSettings.ThemeMode)
            or nameof(LauncherSettings.MotionMode)
            or nameof(LauncherSettings.ThemeColorMode)
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
        IsAutoSaveEnabled = false;
        lock (autoSaveSync)
        {
            isAutoSaveQueued = false;
            isAutoSaveFlushRequested = false;
            autoSaveDelayCts?.Cancel();
        }

        CancelAppearancePreview();
        editor.PropertyChanged -= OnEditorPropertyChanged;
        editor.CurrentPropertyChanged -= OnCurrentSettingChanged;
        autoSaveDelayCts?.Dispose();
        Appearance.Dispose();

        var pendingWork = Task.WhenAll(pendingAutoSaveTask, runtimeApplyTask);
        if (pendingWork.IsCompleted)
        {
            settingsSaveGate.Dispose();
        }
        else
        {
            _ = pendingWork.ContinueWith(
                _ => settingsSaveGate.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
