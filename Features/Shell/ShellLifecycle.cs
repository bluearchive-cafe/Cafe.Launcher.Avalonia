using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Cafe.Launcher.Avalonia.Features.Diagnostics;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Features.Shell;

/// <summary>
/// Owns shell startup, refresh, settings-save, first-run wizard completion,
/// resource-panel switching, and every cross-feature subscription.
/// The window (MainWindowViewModel) only presents shell state.
/// </summary>
public sealed class ShellLifecycle : IDisposable
{
    /// <summary>Raised after a saved status-detail setting changes the shell presentation mode.</summary>
    public event Action? StatusDetailModeChanged;

    /// <summary>Gets the modal host coordinated by this shell lifecycle.</summary>
    public ModalHostViewModel ModalHost { get; }

    private readonly ILauncherCoreService launcherCoreService;
    private readonly LauncherSettingsService settingsService;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly LauncherUpdateService launcherUpdateService;
    private readonly LocalDiagnostics diagnostics;
    private readonly IErrorHandlingService errorHandling;
    private readonly WindowsAnimationSettingsProvider windowsAnimationSettingsProvider;
    private readonly IShellLifecyclePresentation presentation;
    private readonly ShellViewModel shell;
    private readonly BackgroundViewModel background;
    private readonly RemoteContentViewModel remoteContent;
    private readonly DialogsViewModel dialogs;
    private readonly GameOperationsViewModel operations;
    private readonly ToastHostViewModel toasts;
    private readonly WindowChromeViewModel windowChrome;
    private readonly SettingsViewModel settings;
    private readonly ResourcePanelViewModel resourcePanel;
    private readonly LogViewerDialogViewModel logViewer;
    private readonly DebugViewModel debug;
    private readonly Func<Bitmap?> getBackgroundBitmap;
    private readonly Func<LauncherSettings, string?, CancellationToken, Task> previewAppearanceAsync;
    private readonly Func<LauncherSettings, Task> applyLanguageAndThemeAsync;
    private readonly Action<string?> openExternalUrl;
    private readonly Func<string, Task<string?>> pickSetupWizardGameFolderAsync;
    private readonly CancellationTokenSource lifetimeCts = new();
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private int initialized;
    private bool disposed;
    private bool motionSettingsApplied;
    private bool settingsSnapshotInitialized;
    private LauncherStatusSnapshot? currentSnapshot;
    private bool isWired;

    /// <summary>Gets the active startup update check so tests can coordinate without timing delays.</summary>
    internal Task PendingStartupUpdateCheck { get; private set; } = Task.CompletedTask;

    /// <summary>Initializes shell lifecycle dependencies and subscribes error handling callbacks.</summary>
    public ShellLifecycle(
        ILauncherCoreService launcherCoreService,
        LauncherSettingsService settingsService,
        LocalizationService localizer,
        ToastService toastService,
        LauncherUpdateService launcherUpdateService,
        LocalDiagnostics diagnostics,
        IErrorHandlingService errorHandling,
        WindowsAnimationSettingsProvider windowsAnimationSettingsProvider,
        IShellLifecyclePresentation presentation,
        ShellViewModel shell,
        BackgroundViewModel background,
        RemoteContentViewModel remoteContent,
        DialogsViewModel dialogs,
        GameOperationsViewModel operations,
        ToastHostViewModel toasts,
        WindowChromeViewModel windowChrome,
        SettingsViewModel settingsViewModel,
        ResourcePanelViewModel resourcePanelViewModel,
        LogViewerDialogViewModel logViewer,
        DebugViewModel debug,
        ModalHostViewModel modalHost)
    {
        this.launcherCoreService = launcherCoreService;
        this.settingsService = settingsService;
        this.localizer = localizer;
        this.toastService = toastService;
        this.launcherUpdateService = launcherUpdateService;
        this.diagnostics = diagnostics;
        this.errorHandling = errorHandling;
        this.windowsAnimationSettingsProvider = windowsAnimationSettingsProvider;
        this.presentation = presentation;
        this.shell = shell;
        this.background = background;
        this.remoteContent = remoteContent;
        this.dialogs = dialogs;
        this.operations = operations;
        this.toasts = toasts;
        this.windowChrome = windowChrome;
        settings = settingsViewModel;
        resourcePanel = resourcePanelViewModel;
        this.logViewer = logViewer;
        this.debug = debug;
        ModalHost = modalHost;

        getBackgroundBitmap = background.GetBackgroundBitmap;
        previewAppearanceAsync = PreviewAppearanceAsync;
        applyLanguageAndThemeAsync = ApplyLanguageAndThemeAsync;
        openExternalUrl = windowChrome.OpenExternalUrl;
        pickSetupWizardGameFolderAsync = PickSetupWizardGameFolderAsync;

        errorHandling.CriticalErrorRequested += OnCriticalError;
        errorHandling.OperationNoteRequested += OnOperationNoteRequested;
        localizer.LocalizationFailure += OnLocalizationFailure;
    }

    /// <summary>Initializes the shell once by loading settings and launcher state.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref initialized, 1) == 1)
        {
            return;
        }

        await RefreshAsync(cancellationToken);
    }

    /// <summary>Reapplies the system motion preference when the user chose the system option.</summary>
    public void RefreshSystemMotionPreference()
    {
        if (!settingsSnapshotInitialized)
        {
            return;
        }

        var savedSettings = settings.Editor.GetSavedSnapshot();
        if (savedSettings.MotionMode != MotionModes.System)
        {
            return;
        }

        ApplyMotionSettings(savedSettings);
    }

    /// <summary>Applies the initial automatic language before a launcher snapshot exists.</summary>
    public void ApplyInitialLanguage() => ApplyLanguage(LauncherLanguages.Auto);

    /// <summary>Reloads launcher state and updates all dependent presentation models.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(resumePersistedDownload: true, cancellationToken);
    }

    private async Task RefreshAsync(
        bool resumePersistedDownload,
        CancellationToken cancellationToken = default)
    {
        await refreshGate.WaitAsync(cancellationToken);
        var loaded = false;
        try
        {
            presentation.IsBusy = true;
            shell.IsBusy = true;
            try
            {
                var settingsForLanguage = await settingsService.ReadAsync(cancellationToken);
                settings.Editor.ApplySnapshot(settingsForLanguage);
                settingsSnapshotInitialized = true;
                ApplyMotionSettings(settingsForLanguage);
                ApplyLanguage(settingsForLanguage.Language);
                settings.Appearance.Load(settingsForLanguage);
                SettingsAppearanceViewModel.ApplyTheme(settingsForLanguage.ThemeMode);
                settings.Appearance.ApplyThemeColor(
                    settingsForLanguage.ThemeColorMode,
                    SettingsAppearanceViewModel.ParseColorOrDefault(settingsForLanguage.CustomThemeColor));
                shell.SetLoading();
                remoteContent.BeginLoading(settingsForLanguage.ShowRemoteContentCard);

                var snapshot = await launcherCoreService.LoadAsync(cancellationToken);
                currentSnapshot = snapshot;
                await ApplySnapshotAsync(snapshot);
                loaded = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                shell.SetRefreshError(exception);
                operations.SetIdlePanels(currentSnapshot);
                await errorHandling.HandleErrorAsync("Launcher core refresh failed.", exception,
                    new ErrorHandlingOptions { OperationNoteKey = "networkWithMessage" });
            }
            finally
            {
                remoteContent.EndLoading();
                shell.IsBusy = false;
                presentation.IsBusy = false;
            }

        }
        finally
        {
            refreshGate.Release();
        }

        if (!loaded)
        {
            return;
        }

        if (settings.Editor.GetSavedSnapshot().EnableStartupUpdateCheck)
        {
            PendingStartupUpdateCheck = CheckForStartupUpdateAsync(cancellationToken);
        }

        if (resumePersistedDownload)
        {
            await operations.ResumePersistedDownloadAsync(cancellationToken);
        }
    }

    /// <summary>Refreshes the shell after the settings editor saves a new snapshot.</summary>
    public async Task HandleSettingsSavedAsync()
    {
        var previousPatchUrlGroup = currentSnapshot?.Settings.PatchUrlGroup;
        var savedPatchUrlGroup = settings.Editor.Current.PatchUrlGroup;
        remoteContent.UpdateRemoteContentVisibility(
            settings.Editor.Current.ShowRemoteContentCard);
        ApplyMotionSettings(settings.Editor.Current);

        if (operations.IsDownloadRunning)
        {
            if (currentSnapshot is not null)
            {
                currentSnapshot.Settings = await settingsService.ReadAsync();
            }

            return;
        }

        await RefreshAsync();
        if (currentSnapshot?.RuntimeState is LauncherRuntimeState.Ready or LauncherRuntimeState.UpdateAvailable
            && !string.Equals(previousPatchUrlGroup, savedPatchUrlGroup, StringComparison.Ordinal))
        {
            dialogs.ShowRepairConfirm(localizer.T("downloadSourceChangedRepairPrompt"));
        }
    }

    /// <summary>Saves completed wizard settings, applies their language, and refreshes the shell.</summary>
    public async Task HandleSetupWizardCompletedAsync(LauncherSettings newSettings)
    {
        await settingsService.SaveAsync(newSettings);
        ApplyLanguage(newSettings.Language);
        dialogs.IsSetupWizardVisible = false;
        await RefreshAsync();
    }

    /// <summary>Shows confirmation before switching the resource-panel source.</summary>
    public void ShowResourcePanelSourceConfirmDialog()
    {
        dialogs.ShowResourcePanelSourceConfirm(localizer.T("resourcePanelCafeOnlyMessage"));
    }

    /// <summary>Switches the confirmed resource-panel source and opens its panel.</summary>
    public async Task SwitchSourceThenOpenPanelAsync()
    {
        try
        {
            var savedSettings = await settingsService.ReadAsync();
            savedSettings.PatchUrlGroup = PatchUrlGroups.Cafe;
            await settingsService.SaveAsync(savedSettings);
            settings.Editor.Current.PatchUrlGroup = PatchUrlGroups.Cafe;

            await HandleSettingsSavedAsync();
            await resourcePanel.OpenPanelDirectly();
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Resource panel source switch failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F("resourcePanelLoadFailed", exception.Message) });
        }
    }

    /// <summary>Restores default settings after crash recovery requests a reset.</summary>
    public async Task ResetSettingsAfterCrashAsync()
    {
        await settingsService.SaveAsync(LauncherSettings.CreateDefaults());
        await RefreshAsync();
    }

    private void OnResourcePanelSourceSwitchConfirmed() => _ = SwitchSourceThenOpenPanelAsync();

    private static void OnUpdateAvailableConfirmed(string downloadUrl) => ExternalLinkService.Open(downloadUrl);

    private Task HandleSetupWizardSettingsAppliedAsync(LauncherSettings newSettings) =>
        HandleSetupWizardCompletedAsync(newSettings);

    /// <summary>Refreshes shell state after a game operation and records resume behavior.</summary>
    internal async Task HandleOperationsRefreshRequestedAsync(GameOperationsRefreshMode mode)
    {
        await RefreshAsync(
            resumePersistedDownload: mode != GameOperationsRefreshMode.SkipPersistedResume);
    }

    /// <summary>Refreshes shell state in response to the debug panel.</summary>
    internal Task HandleDebugRefreshRequestedAsync() => RefreshAsync();

    /// <summary>Opens the log viewer from an operation failure action.</summary>
    internal Task OpenLogViewerAsync() => logViewer.OpenCommand.ExecuteAsync(null);

    /// <summary>Opens the log viewer from crash-recovery UI.</summary>
    internal void OpenCrashLog()
    {
        logViewer.OpenCommand.Execute(null);
    }

    private async Task PreviewAppearanceAsync(
        LauncherSettings previewSettings,
        string? propertyName,
        CancellationToken cancellationToken)
    {
        SettingsAppearanceViewModel.ApplyTheme(previewSettings.ThemeMode);
        settings.Appearance.ApplyThemeColor(
            previewSettings.ThemeColorMode,
            SettingsAppearanceViewModel.ParseColorOrDefault(previewSettings.CustomThemeColor));
        background.ApplyBackgroundPresentation(previewSettings);

        if (propertyName is null
            or nameof(LauncherSettings.BackgroundSource)
            or nameof(LauncherSettings.CustomBackgroundPath))
        {
            await background.UpdateBackgroundImageAsync(
                previewSettings,
                currentSnapshot,
                cancellationToken);
        }
    }

    private Task ApplyLanguageAndThemeAsync(LauncherSettings launcherSettings)
    {
        ApplyLanguage(launcherSettings.Language);
        SettingsAppearanceViewModel.ApplyTheme(launcherSettings.ThemeMode);
        settings.Appearance.ApplyThemeColor(
            launcherSettings.ThemeColorMode,
            SettingsAppearanceViewModel.ParseColorOrDefault(launcherSettings.CustomThemeColor));
        return Task.CompletedTask;
    }

    private Task<string?> PickSetupWizardGameFolderAsync(string currentPath) =>
        settings.PickGameFolderAsync?.Invoke(currentPath) ?? Task.FromResult<string?>(null);

    /// <summary>Subscribes cross-feature events once for the active shell lifecycle.</summary>
    public void Wire()
    {
        if (isWired) return;
        isWired = true;

        settings.Appearance.GetBackgroundBitmap = getBackgroundBitmap;
        settings.PreviewAppearanceAsync = previewAppearanceAsync;
        settings.ApplyLanguageAndTheme = applyLanguageAndThemeAsync;
        settings.SettingsSaved += HandleSettingsSavedAsync;

        resourcePanel.ResourcePanelSourceConfirmRequested += ShowResourcePanelSourceConfirmDialog;
        dialogs.ConfirmResourcePanelSourceSwitchRequested += OnResourcePanelSourceSwitchConfirmed;

        operations.RefreshRequested += HandleOperationsRefreshRequestedAsync;
        operations.OpenLogViewerRequested += OpenLogViewerAsync;

        dialogs.CloseAfterStoppingDownloadRequested += windowChrome.CloseAfterStoppingDownload;
        dialogs.CloseRequested += windowChrome.RequestClose;
        dialogs.ConfirmUpdateAvailableRequested += OnUpdateAvailableConfirmed;
        dialogs.CrashRecoveryResetSettingsRequested += ResetSettingsAfterCrashAsync;
        dialogs.CrashRecoveryViewLogRequested += OpenCrashLog;
        dialogs.ErrorViewLogRequested += OpenCrashLog;

        debug.RefreshRequested += HandleDebugRefreshRequestedAsync;
        debug.ResetSettingsRequested += ResetSettingsAfterCrashAsync;
        debug.ResetSettingsConfirmationRequested += dialogs.ShowDebugResetConfirmation;
        dialogs.ConfirmDebugResetRequested += debug.ConfirmResetSettingsAsync;

        remoteContent.OpenExternalUrlRequested = openExternalUrl;

        dialogs.SetupWizard.PickGameFolderAsync = pickSetupWizardGameFolderAsync;
        dialogs.SetupWizard.LanguagePreviewRequested += PreviewSetupWizardLanguage;
        dialogs.SetupWizard.SettingsApplied += HandleSetupWizardSettingsAppliedAsync;

        windowChrome.PropertyChanged += OnWindowChromePropertyChanged;
        settings.PropertyChanged += OnSettingsPropertyChanged;
        settings.Editor.CurrentPropertyChanged += OnSettingPropertyChanged;
        resourcePanel.PropertyChanged += OnResourcePanelPropertyChanged;
        logViewer.PropertyChanged += OnLogViewerPropertyChanged;
        debug.PropertyChanged += OnDebugPropertyChanged;
        dialogs.PropertyChanged += OnDialogsPropertyChanged;
    }

    /// <summary>Removes cross-feature event subscriptions established by <see cref="Wire"/>.</summary>
    public void Unwire()
    {
        if (!isWired) return;
        isWired = false;

        settings.SettingsSaved -= HandleSettingsSavedAsync;
        operations.RefreshRequested -= HandleOperationsRefreshRequestedAsync;
        operations.OpenLogViewerRequested -= OpenLogViewerAsync;
        resourcePanel.ResourcePanelSourceConfirmRequested -= ShowResourcePanelSourceConfirmDialog;
        dialogs.ConfirmResourcePanelSourceSwitchRequested -= OnResourcePanelSourceSwitchConfirmed;
        dialogs.CloseAfterStoppingDownloadRequested -= windowChrome.CloseAfterStoppingDownload;
        dialogs.CloseRequested -= windowChrome.RequestClose;
        dialogs.ConfirmUpdateAvailableRequested -= OnUpdateAvailableConfirmed;
        dialogs.CrashRecoveryResetSettingsRequested -= ResetSettingsAfterCrashAsync;
        dialogs.CrashRecoveryViewLogRequested -= OpenCrashLog;
        dialogs.ErrorViewLogRequested -= OpenCrashLog;
        dialogs.SetupWizard.LanguagePreviewRequested -= PreviewSetupWizardLanguage;
        dialogs.SetupWizard.SettingsApplied -= HandleSetupWizardSettingsAppliedAsync;
        debug.RefreshRequested -= HandleDebugRefreshRequestedAsync;
        debug.ResetSettingsRequested -= ResetSettingsAfterCrashAsync;
        debug.ResetSettingsConfirmationRequested -= dialogs.ShowDebugResetConfirmation;
        dialogs.ConfirmDebugResetRequested -= debug.ConfirmResetSettingsAsync;
        windowChrome.PropertyChanged -= OnWindowChromePropertyChanged;
        settings.PropertyChanged -= OnSettingsPropertyChanged;
        settings.Editor.CurrentPropertyChanged -= OnSettingPropertyChanged;
        resourcePanel.PropertyChanged -= OnResourcePanelPropertyChanged;
        logViewer.PropertyChanged -= OnLogViewerPropertyChanged;
        debug.PropertyChanged -= OnDebugPropertyChanged;
        dialogs.PropertyChanged -= OnDialogsPropertyChanged;

        if (settings.Appearance.GetBackgroundBitmap == getBackgroundBitmap)
        {
            settings.Appearance.GetBackgroundBitmap = null;
        }

        if (settings.PreviewAppearanceAsync == previewAppearanceAsync)
        {
            settings.PreviewAppearanceAsync = null;
        }

        if (settings.ApplyLanguageAndTheme == applyLanguageAndThemeAsync)
        {
            settings.ApplyLanguageAndTheme = null;
        }

        if (remoteContent.OpenExternalUrlRequested == openExternalUrl)
        {
            remoteContent.OpenExternalUrlRequested = null;
        }

        if (dialogs.SetupWizard.PickGameFolderAsync == pickSetupWizardGameFolderAsync)
        {
            dialogs.SetupWizard.PickGameFolderAsync = null;
        }
    }

    /// <summary>Handles Escape for the active modal and returns whether a modal consumed it.</summary>
    public bool TryHandleEscape()
    {
        switch (ModalHost.Top?.Kind)
        {
            case ModalKind.DownloadRunningCloseConfirmation:
                dialogs.CancelCloseWhileDownloadingCommand.Execute(null);
                break;
            case ModalKind.StopConfirmation:
                dialogs.CancelStopCommand.Execute(null);
                break;
            case ModalKind.UnsavedSettingsConfirmation:
                windowChrome.KeepEditingSettingsCommand.Execute(null);
                break;
            case ModalKind.RepairConfirmation:
                dialogs.CancelRepairCommand.Execute(null);
                break;
            case ModalKind.ResourcePanelSourceConfirmation:
                dialogs.CancelResourcePanelSourceSwitchCommand.Execute(null);
                break;
            case ModalKind.UninstallConfirmation:
                dialogs.CancelUninstallCommand.Execute(null);
                break;
            case ModalKind.Notice:
                dialogs.DismissNoticeCommand.Execute(null);
                break;
            case ModalKind.Update:
                dialogs.CancelUpdateAvailableCommand.Execute(null);
                break;
            case ModalKind.CrashRecovery:
                dialogs.ContinueAfterCrashCommand.Execute(null);
                break;
            case ModalKind.Error:
                dialogs.ContinueAfterErrorCommand.Execute(null);
                break;
            case ModalKind.LogViewer:
                logViewer.CloseCommand.Execute(null);
                break;
            case ModalKind.Debug:
                debug.CloseCommand.Execute(null);
                break;
            case ModalKind.DebugResetConfirmation:
                dialogs.CancelDebugResetCommand.Execute(null);
                break;
            case ModalKind.SetupWizardExitConfirmation:
                dialogs.CancelSetupWizardExitCommand.Execute(null);
                break;
            case ModalKind.Settings:
                windowChrome.ShowSettingsCommand.Execute(null);
                break;
            case ModalKind.SetupWizard:
                dialogs.RequestSetupWizardExitCommand.Execute(null);
                break;
            case ModalKind.ResourcePanel:
                resourcePanel.CloseResourcePanelCommand.Execute(null);
                break;
            default:
                return false;
        }
        return true;
    }

    /// <summary>Unsubscribes lifecycle callbacks and disposes presentation collaborators in ownership order.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        Unwire();
        operations.StopDownload(clearPersistedState: false);
        operations.Dispose();
        shell.Dispose();
        settings.Dispose();
        remoteContent.Dispose();
        background.Dispose();
        toasts.Dispose();
        resourcePanel.Dispose();
        debug.Dispose();
        dialogs.SetupWizard.Dispose();
        lifetimeCts.Cancel();
        lifetimeCts.Dispose();
        refreshGate.Dispose();
        errorHandling.CriticalErrorRequested -= OnCriticalError;
        errorHandling.OperationNoteRequested -= OnOperationNoteRequested;
        localizer.LocalizationFailure -= OnLocalizationFailure;
    }

    private async Task CheckForStartupUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var savedSettings = settings.Editor.GetSavedSnapshot();
            var result = await launcherUpdateService.CheckForUpdateAsync(
                savedSettings.UpdateChannel,
                savedSettings.ProxyMode,
                cancellationToken);

            if (result.IsSuccessful && result.IsUpdateAvailable)
            {
                toastService.Show(
                    localizer.F("startupUpdateAvailable", result.LatestVersion),
                    ToastSeverity.Info,
                    durationMs: 8000);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await diagnostics.DebugAsync(
                "StartupUpdateCheck",
                $"Startup update check failed (non-critical): {exception.Message}",
                CancellationToken.None);
        }
    }

    private async Task ApplySnapshotAsync(LauncherStatusSnapshot snapshot)
    {
        ApplySettingsSnapshot(snapshot.Settings);
        ApplyLanguage(snapshot.Settings.Language);
        SettingsAppearanceViewModel.ApplyTheme(snapshot.Settings.ThemeMode);
        await background.UpdateBackgroundImageAsync(
            snapshot.Settings,
            snapshot,
            lifetimeCts.Token);
        settings.Appearance.ApplyThemeColor(
            snapshot.Settings.ThemeColorMode,
            SettingsAppearanceViewModel.ParseColorOrDefault(snapshot.Settings.CustomThemeColor));

        shell.ApplySnapshot(snapshot, settings);
        operations.ApplySnapshot(snapshot);
        remoteContent.Apply(snapshot.Remote, snapshot.Settings, lifetimeCts.Token);
        remoteContent.SetLoadError(snapshot.RuntimeState == LauncherRuntimeState.RemoteUnavailable);
        await dialogs.ShowNoticeDialogIfNeededAsync(snapshot.Remote.BaseConfig, lifetimeCts.Token);
    }

    private void ApplySettingsSnapshot(LauncherSettings savedSettings)
    {
        settings.ApplyLauncherSettings(savedSettings);
        resourcePanel.ApplySettings(savedSettings);
        ApplyMotionSettings(savedSettings);
    }

    private void ApplyMotionSettings(LauncherSettings savedSettings)
    {
        var windowsAnimationsEnabled = savedSettings.MotionMode == MotionModes.System
            ? windowsAnimationSettingsProvider.GetWindowsAnimationsEnabled()
            : null;
        var reduceMotion = MotionSettingsResolver.ShouldReduceMotion(
            savedSettings.MotionMode,
            windowsAnimationsEnabled);
        if (motionSettingsApplied && reduceMotion == presentation.IsMotionReduced)
        {
            return;
        }

        motionSettingsApplied = true;
        presentation.IsMotionReduced = reduceMotion;
        remoteContent.ApplyMotionPreference(reduceMotion);
        toasts.ApplyMotionPreference(reduceMotion);
    }

    private void ApplyLanguage(string language)
    {
        shell.ApplyLanguage(language, settings, resourcePanel, currentSnapshot is not null);
        background.BackgroundImagePickerTitle = localizer.T("chooseBackgroundImageTitle");
        background.BackgroundFolderPickerTitle = localizer.T("chooseBackgroundFolderTitle");
        remoteContent.ApplyLanguage();
        dialogs.ApplyLanguage();
        operations.ApplyLanguage();
        debug.ApplyLanguage();
    }

    private void PreviewSetupWizardLanguage(string language)
    {
        if (dialogs.IsSetupWizardVisible)
        {
            ApplyLanguage(language);
        }
    }

    private void OnLocalizationFailure(object? sender, LocalizationFailureEventArgs eventArgs)
    {
        _ = errorHandling.HandleErrorAsync(
            "Localization resources could not be loaded.",
            eventArgs.Exception,
            new ErrorHandlingOptions
            {
                ToastMessage = "Localization unavailable.",
                IncludeExceptionDetails = false,
                OperationNoteKey = null
            });
    }

    private void OnStatusDetailModeChanged()
    {
        StatusDetailModeChanged?.Invoke();
    }

    private void OnCriticalError(CriticalErrorInfo info)
    {
        dialogs.ShowCriticalError(info.Message, info.Details);
    }

    private void OnOperationNoteRequested(string note)
    {
        shell.OperationNote = note;
    }

    private void OnSettingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherSettings.StatusDetailMode))
        {
            OnStatusDetailModeChanged();
        }
    }

    private void OnWindowChromePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WindowChromeViewModel.IsSettingsVisible))
        {
            SyncModal(ModalKind.Settings, windowChrome.IsSettingsVisible, settings);
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsUnsavedChangesVisible))
        {
            SyncModal(
                ModalKind.UnsavedSettingsConfirmation,
                settings.IsUnsavedChangesVisible,
                settings);
        }
    }

    private void OnResourcePanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResourcePanelViewModel.IsResourcePanelVisible))
        {
            SyncModal(
                ModalKind.ResourcePanel,
                resourcePanel.IsResourcePanelVisible,
                resourcePanel);
        }
    }

    private void OnLogViewerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogViewerDialogViewModel.IsVisible))
        {
            SyncModal(ModalKind.LogViewer, logViewer.IsVisible, logViewer);
        }
    }

    private void OnDebugPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DebugViewModel.IsVisible))
        {
            SyncModal(ModalKind.Debug, debug.IsVisible, debug);
        }
    }

    private void OnDialogsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DialogsViewModel.IsNoticeDialogVisible):
                SyncModal(ModalKind.Notice, dialogs.IsNoticeDialogVisible, dialogs);
                break;
            case nameof(DialogsViewModel.IsUpdateAvailableVisible):
                SyncModal(ModalKind.Update, dialogs.IsUpdateAvailableVisible, dialogs);
                break;
            case nameof(DialogsViewModel.IsCrashRecoveryVisible):
                SyncModal(ModalKind.CrashRecovery, dialogs.IsCrashRecoveryVisible, dialogs);
                break;
            case nameof(DialogsViewModel.IsErrorDialogVisible):
                SyncModal(ModalKind.Error, dialogs.IsErrorDialogVisible, dialogs);
                break;
            case nameof(DialogsViewModel.IsDebugResetConfirmationVisible):
                SyncModal(
                    ModalKind.DebugResetConfirmation,
                    dialogs.IsDebugResetConfirmationVisible,
                    dialogs);
                break;
            case nameof(DialogsViewModel.IsSetupWizardVisible):
                SyncModal(ModalKind.SetupWizard, dialogs.IsSetupWizardVisible, dialogs.SetupWizard);
                break;
            case nameof(DialogsViewModel.IsSetupWizardExitConfirmVisible):
                SyncModal(
                    ModalKind.SetupWizardExitConfirmation,
                    dialogs.IsSetupWizardExitConfirmVisible,
                    dialogs);
                break;
            case nameof(DialogsViewModel.IsRepairConfirmVisible):
                SyncModal(ModalKind.RepairConfirmation, dialogs.IsRepairConfirmVisible, dialogs);
                break;
            case nameof(DialogsViewModel.IsResourcePanelSourceConfirmVisible):
                SyncModal(
                    ModalKind.ResourcePanelSourceConfirmation,
                    dialogs.IsResourcePanelSourceConfirmVisible,
                    dialogs);
                break;
            case nameof(DialogsViewModel.IsUninstallConfirmVisible):
                SyncModal(
                    ModalKind.UninstallConfirmation,
                    dialogs.IsUninstallConfirmVisible,
                    dialogs);
                break;
            case nameof(DialogsViewModel.IsStopConfirmVisible):
                SyncModal(ModalKind.StopConfirmation, dialogs.IsStopConfirmVisible, dialogs);
                break;
            case nameof(DialogsViewModel.IsDownloadRunningCloseConfirmVisible):
                SyncModal(
                    ModalKind.DownloadRunningCloseConfirmation,
                    dialogs.IsDownloadRunningCloseConfirmVisible,
                    dialogs);
                break;
        }
    }

    private void SyncModal(ModalKind kind, bool isVisible, IModalContentViewModel content)
    {
        if (isVisible)
        {
            ModalHost.Open(kind, content);
        }
        else
        {
            ModalHost.Close(kind);
        }
    }
}
