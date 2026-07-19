using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ILauncherCoreService launcherCoreService;
    private readonly LauncherSettingsService settingsService;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly LocalDiagnostics diagnostics;
    private readonly CancellationTokenSource lifetimeCts = new();
    private int initialized;
    private bool disposed;
    private bool skipNextPersistedResume;
    private bool motionSettingsApplied;
    private bool settingsSnapshotInitialized;
    private LauncherStatusSnapshot? currentSnapshot;
    private readonly WindowsAnimationSettingsProvider windowsAnimationSettingsProvider;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMotionEnabled))]
    private bool isMotionReduced = true;

    public bool IsMotionEnabled => !IsMotionReduced;

    public bool IsBottomPanelVisible => true;

    public bool IsStatusDetailExpanded =>
        Settings.Editor.Current.StatusDetailMode == StatusDetailModes.Detailed;

    public bool IsStatusDetailHidden =>
        Settings.Editor.Current.StatusDetailMode == StatusDetailModes.Hidden;

    public ShellViewModel Shell { get; }

    public BackgroundViewModel Background { get; }

    public RemoteContentViewModel RemoteContent { get; }

    public DialogsViewModel Dialogs { get; }

    public GameOperationsViewModel Operations { get; }

    public ToastHostViewModel Toasts { get; }

    public WindowChromeViewModel WindowChrome { get; }

    public SettingsViewModel Settings { get; }

    public ResourcePanelViewModel ResourcePanel { get; }

    public LogViewerDialogViewModel LogViewer { get; }

    public ModalHostViewModel ModalHost { get; }

    public MainWindowViewModel(
        ILauncherCoreService launcherCoreService,
        LauncherSettingsService settingsService,
        LocalizationService localizer,
        ToastService toastService,
        LocalDiagnostics diagnostics,
        UnifiedLogger unifiedLogger,
        ShellViewModel shell,
        BackgroundViewModel background,
        RemoteContentViewModel remoteContent,
        DialogsViewModel dialogs,
        GameOperationsViewModel operations,
        ToastHostViewModel toasts,
        WindowChromeViewModel windowChrome,
        SettingsViewModel settingsViewModel,
        ResourcePanelViewModel resourcePanelViewModel,
        LogViewerDialogViewModel? logViewer = null,
        ModalHostViewModel? modalHost = null,
        WindowsAnimationSettingsProvider? windowsAnimationSettingsProvider = null)
    {
        this.launcherCoreService = launcherCoreService;
        this.settingsService = settingsService;
        this.localizer = localizer;
        this.toastService = toastService;
        this.diagnostics = diagnostics;
        this.windowsAnimationSettingsProvider = windowsAnimationSettingsProvider ?? new WindowsAnimationSettingsProvider();

        Shell = shell;
        Background = background;
        RemoteContent = remoteContent;
        Dialogs = dialogs;
        Operations = operations;
        Toasts = toasts;
        WindowChrome = windowChrome;
        Settings = settingsViewModel;
        ResourcePanel = resourcePanelViewModel;
        LogViewer = logViewer ?? new LogViewerDialogViewModel(
            unifiedLogger,
            null,
            null,
            null,
            null,
            null);
        ModalHost = modalHost ?? new ModalHostViewModel();

        WireChildren();
        WireModalHost();
        ApplyLanguage(LauncherLanguages.Auto);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref initialized, 1) == 1)
        {
            return;
        }

        await RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Refreshes the effective motion preference only when the saved mode follows the system setting.
    /// </summary>
    public void RefreshSystemMotionPreference()
    {
        if (!settingsSnapshotInitialized)
        {
            return;
        }

        var settings = Settings.Editor.GetSavedSnapshot();
        if (settings.MotionMode != MotionModes.System)
        {
            return;
        }

        ApplyMotionSettings(settings);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Shell.IsBusy = true;
        var loaded = false;
        try
        {
            var settingsForLanguage = await settingsService.ReadAsync(cancellationToken);
            Settings.Editor.ApplySnapshot(settingsForLanguage);
            settingsSnapshotInitialized = true;
            ApplyMotionSettings(settingsForLanguage);
            ApplyLanguage(settingsForLanguage.Language);
            Settings.Appearance.Load(settingsForLanguage);
            SettingsAppearanceViewModel.ApplyTheme(settingsForLanguage.ThemeMode);
            Settings.Appearance.ApplyThemeColor(
                settingsForLanguage.ThemeColorMode,
                SettingsAppearanceViewModel.ParseColorOrDefault(settingsForLanguage.CustomThemeColor));
            Shell.SetLoading();
            RemoteContent.BeginLoading(settingsForLanguage.ShowRemoteContentCard);

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
            Shell.SetRefreshError(exception);
            Operations.SetIdlePanels(currentSnapshot);
            toastService.ShowError(localizer.F("networkWithMessage", exception.Message));
            await diagnostics.ErrorAsync("Launcher core refresh failed.", exception, CancellationToken.None);
        }
        finally
        {
            RemoteContent.EndLoading();
            Shell.IsBusy = false;
        }

        if (!loaded)
        {
            return;
        }

        if (skipNextPersistedResume)
        {
            skipNextPersistedResume = false;
            return;
        }

        await Operations.ResumePersistedDownloadAsync(cancellationToken);
    }

    private void WireChildren()
    {
        Settings.Appearance.GetBackgroundBitmap = Background.GetBackgroundBitmap;
        Settings.PreviewAppearanceAsync = async (settings, propertyName, cancellationToken) =>
        {
            SettingsAppearanceViewModel.ApplyTheme(settings.ThemeMode);
            Settings.Appearance.ApplyThemeColor(
                settings.ThemeColorMode,
                SettingsAppearanceViewModel.ParseColorOrDefault(settings.CustomThemeColor));
            Background.ApplyBackgroundPresentation(settings);

            if (propertyName is null
                or nameof(LauncherSettings.BackgroundSource)
                or nameof(LauncherSettings.CustomBackgroundPath))
            {
                await Background.UpdateBackgroundImageAsync(
                    settings,
                    currentSnapshot,
                    cancellationToken);
            }
        };
        Settings.ApplyLanguageAndTheme = async s =>
        {
            ApplyLanguage(s.Language);
            SettingsAppearanceViewModel.ApplyTheme(s.ThemeMode);
            // Background is intentionally NOT updated here.
            // Both callers (SaveSettingsAsync, ChooseGamePathAsync) fire SettingsSaved
            // immediately after, which triggers RefreshAsync → ApplySnapshotAsync →
            // Background.UpdateBackgroundImageAsync. Updating it here too would cause
            // a double-update; for folder-based (random) backgrounds each update picks a
            // different image, so the wallpaper visibly flickers between two random picks.
            Settings.Appearance.ApplyThemeColor(
                s.ThemeColorMode,
                SettingsAppearanceViewModel.ParseColorOrDefault(s.CustomThemeColor));
        };
        Settings.SettingsSaved += HandleSettingsSavedAsync;

        ResourcePanel.ResourcePanelSourceConfirmRequested += ShowResourcePanelSourceConfirmDialog;
        Dialogs.ConfirmResourcePanelSourceSwitchRequested += SwitchToCafeAndOpenResourcePanel;

        Operations.RefreshRequested += HandleOperationsRefreshRequestedAsync;

        Dialogs.ConfirmRepairRequested += Operations.RepairAsync;
        Dialogs.ConfirmUninstallRequested += Operations.ConfirmUninstallAsync;
        Dialogs.ConfirmStopRequested += Operations.PerformStop;
        Dialogs.CloseAfterStoppingDownloadRequested += WindowChrome.CloseAfterStoppingDownload;
        Dialogs.CloseRequested += WindowChrome.RequestClose;
        Dialogs.ConfirmUpdateAvailableRequested += url => ExternalLinkService.Open(url);
        Dialogs.CrashRecoveryResetSettingsRequested += ResetSettingsAfterCrashAsync;
        Dialogs.CrashRecoveryViewLogRequested += OpenCrashLog;

        RemoteContent.OpenExternalUrlRequested = WindowChrome.OpenExternalUrl;

        // Setup wizard
        Dialogs.SetupWizard.PickGameFolderAsync = PickGameFolderForWizardAsync;
        Dialogs.SetupWizard.LanguagePreviewRequested += PreviewSetupWizardLanguage;
        Dialogs.SetupWizard.SettingsApplied += HandleSetupWizardCompletedAsync;
    }

    private void WireModalHost()
    {
        WindowChrome.PropertyChanged += OnWindowChromePropertyChanged;
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        Settings.Editor.CurrentPropertyChanged += OnSettingPropertyChanged;
        ResourcePanel.PropertyChanged += OnResourcePanelPropertyChanged;
        LogViewer.PropertyChanged += OnLogViewerPropertyChanged;
        Dialogs.PropertyChanged += OnDialogsPropertyChanged;
    }

    private void OnSettingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherSettings.StatusDetailMode))
        {
            OnPropertyChanged(nameof(IsStatusDetailExpanded));
            OnPropertyChanged(nameof(IsStatusDetailHidden));
        }
    }

    private void OnWindowChromePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WindowChromeViewModel.IsSettingsVisible))
        {
            SyncModal(ModalKind.Settings, WindowChrome.IsSettingsVisible, Settings);
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsUnsavedChangesVisible))
        {
            SyncModal(
                ModalKind.UnsavedSettingsConfirmation,
                Settings.IsUnsavedChangesVisible,
                Settings);
        }
    }

    private void OnResourcePanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResourcePanelViewModel.IsResourcePanelVisible))
        {
            SyncModal(
                ModalKind.ResourcePanel,
                ResourcePanel.IsResourcePanelVisible,
                ResourcePanel);
        }
    }

    private void OnLogViewerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogViewerDialogViewModel.IsVisible))
        {
            SyncModal(ModalKind.LogViewer, LogViewer.IsVisible, LogViewer);
        }
    }

    private void OnDialogsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DialogsViewModel.IsNoticeDialogVisible):
                SyncModal(ModalKind.Notice, Dialogs.IsNoticeDialogVisible, Dialogs);
                break;
            case nameof(DialogsViewModel.IsUpdateAvailableVisible):
                SyncModal(ModalKind.Update, Dialogs.IsUpdateAvailableVisible, Dialogs);
                break;
            case nameof(DialogsViewModel.IsCrashRecoveryVisible):
                SyncModal(ModalKind.CrashRecovery, Dialogs.IsCrashRecoveryVisible, Dialogs);
                break;
            case nameof(DialogsViewModel.IsSetupWizardVisible):
                SyncModal(ModalKind.SetupWizard, Dialogs.IsSetupWizardVisible, Dialogs.SetupWizard);
                break;
            case nameof(DialogsViewModel.IsSetupWizardExitConfirmVisible):
                SyncModal(
                    ModalKind.SetupWizardExitConfirmation,
                    Dialogs.IsSetupWizardExitConfirmVisible,
                    Dialogs);
                break;
            case nameof(DialogsViewModel.IsRepairConfirmVisible):
                SyncModal(ModalKind.RepairConfirmation, Dialogs.IsRepairConfirmVisible, Dialogs);
                break;
            case nameof(DialogsViewModel.IsResourcePanelSourceConfirmVisible):
                SyncModal(
                    ModalKind.ResourcePanelSourceConfirmation,
                    Dialogs.IsResourcePanelSourceConfirmVisible,
                    Dialogs);
                break;
            case nameof(DialogsViewModel.IsUninstallConfirmVisible):
                SyncModal(
                    ModalKind.UninstallConfirmation,
                    Dialogs.IsUninstallConfirmVisible,
                    Dialogs);
                break;
            case nameof(DialogsViewModel.IsStopConfirmVisible):
                SyncModal(ModalKind.StopConfirmation, Dialogs.IsStopConfirmVisible, Dialogs);
                break;
            case nameof(DialogsViewModel.IsDownloadRunningCloseConfirmVisible):
                SyncModal(
                    ModalKind.DownloadRunningCloseConfirmation,
                    Dialogs.IsDownloadRunningCloseConfirmVisible,
                    Dialogs);
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

    internal async Task HandleOperationsRefreshRequestedAsync(GameOperationsRefreshMode mode)
    {
        if (mode == GameOperationsRefreshMode.SkipPersistedResume)
        {
            skipNextPersistedResume = true;
        }

        await RefreshAsync();
    }

    private async Task ResetSettingsAfterCrashAsync()
    {
        await settingsService.SaveAsync(LauncherSettings.CreateDefaults());
        await RefreshAsync();
    }

    private void OpenCrashLog()
    {
        LogViewer.OpenCommand.Execute(null);
    }

    private async Task<string?> PickGameFolderForWizardAsync(string currentPath)
    {
        if (Settings.PickGameFolderAsync is not null)
            return await Settings.PickGameFolderAsync(currentPath);
        return null;
    }

    private async Task HandleSetupWizardCompletedAsync(LauncherSettings settings)
    {
        await settingsService.SaveAsync(settings);

        // Apply language immediately so the wizard overlays reflect the choice
        ApplyLanguage(settings.Language);

        // Hide wizard
        Dialogs.IsSetupWizardVisible = false;

        // Run normal initialization
        await RefreshAsync();
    }

    private void ShowResourcePanelSourceConfirmDialog()
    {
        Dialogs.ShowResourcePanelSourceConfirm(localizer.T("resourcePanelCafeOnlyMessage"));
    }

    private void SwitchToCafeAndOpenResourcePanel()
    {
        _ = SwitchSourceThenOpenPanelAsync();
    }

    private async Task SwitchSourceThenOpenPanelAsync()
    {
        try
        {
            var settings = await settingsService.ReadAsync();
            settings.PatchUrlGroup = PatchUrlGroups.Cafe;
            await settingsService.SaveAsync(settings);
            Settings.Editor.Current.PatchUrlGroup = PatchUrlGroups.Cafe;

            await HandleSettingsSavedAsync();
            await ResourcePanel.OpenPanelDirectly();
        }
        catch (Exception exception)
        {
            toastService.ShowError(localizer.F("resourcePanelLoadFailed", exception.Message));
            await diagnostics.ErrorAsync("Resource panel source switch failed.", exception);
        }
    }

    private async Task HandleSettingsSavedAsync()
    {
        var previousPatchUrlGroup = currentSnapshot?.Settings.PatchUrlGroup;
        var savedPatchUrlGroup = Settings.Editor.Current.PatchUrlGroup;
        RemoteContent.UpdateRemoteContentVisibility(
            Settings.Editor.Current.ShowRemoteContentCard);
        ApplyMotionSettings(Settings.Editor.Current);

        if (Operations.IsDownloadRunning)
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
            Dialogs.ShowRepairConfirm(localizer.T("downloadSourceChangedRepairPrompt"));
        }
    }

    private async Task ApplySnapshotAsync(LauncherStatusSnapshot snapshot)
    {
        ApplySettingsSnapshot(snapshot.Settings);
        ApplyLanguage(snapshot.Settings.Language);
        SettingsAppearanceViewModel.ApplyTheme(snapshot.Settings.ThemeMode);
        await Background.UpdateBackgroundImageAsync(
            snapshot.Settings,
            snapshot,
            lifetimeCts.Token);
        Settings.Appearance.ApplyThemeColor(
            snapshot.Settings.ThemeColorMode,
            SettingsAppearanceViewModel.ParseColorOrDefault(snapshot.Settings.CustomThemeColor));

        Shell.ApplySnapshot(snapshot, Settings);
        Operations.ApplySnapshot(snapshot);
        RemoteContent.Apply(snapshot.Remote, snapshot.Settings, lifetimeCts.Token);
        await Dialogs.ShowNoticeDialogIfNeededAsync(snapshot.Remote.BaseConfig, lifetimeCts.Token);
    }

    private void ApplySettingsSnapshot(LauncherSettings settings)
    {
        Settings.ApplyLauncherSettings(settings);
        ResourcePanel.ApplySettings(settings);
        ApplyMotionSettings(settings);
    }

    private void ApplyMotionSettings(LauncherSettings settings)
    {
        var windowsAnimationsEnabled = settings.MotionMode == MotionModes.System
            ? windowsAnimationSettingsProvider.GetWindowsAnimationsEnabled()
            : null;
        var reduceMotion = MotionSettingsResolver.ShouldReduceMotion(
            settings.MotionMode,
            windowsAnimationsEnabled);
        if (motionSettingsApplied && reduceMotion == IsMotionReduced)
        {
            return;
        }

        motionSettingsApplied = true;
        IsMotionReduced = reduceMotion;
        RemoteContent.ApplyMotionPreference(reduceMotion);
        Toasts.ApplyMotionPreference(reduceMotion);
    }

    private void ApplyLanguage(string language)
    {
        Shell.ApplyLanguage(language, Settings, ResourcePanel, currentSnapshot is not null);
        Background.BackgroundImagePickerTitle = localizer.T("chooseBackgroundImageTitle");
        Background.BackgroundFolderPickerTitle = localizer.T("chooseBackgroundFolderTitle");
        RemoteContent.ApplyLanguage();
        Dialogs.ApplyLanguage();
        Operations.ApplyLanguage();
    }

    private void PreviewSetupWizardLanguage(string language)
    {
        if (Dialogs.IsSetupWizardVisible)
        {
            ApplyLanguage(language);
        }
    }

    // ── Window interaction (Escape key resolution) ──────────────────────

    /// <summary>
    /// Attempt to handle the Escape key press.
    /// Returns true if a visible overlay/dialog was dismissed, false if no action was needed.
    /// </summary>
    public bool TryHandleEscape()
    {
        switch (ModalHost.Top?.Kind)
        {
            case ModalKind.DownloadRunningCloseConfirmation:
                Dialogs.CancelCloseWhileDownloadingCommand.Execute(null);
                break;
            case ModalKind.StopConfirmation:
                Dialogs.CancelStopCommand.Execute(null);
                break;
            case ModalKind.UnsavedSettingsConfirmation:
                WindowChrome.KeepEditingSettingsCommand.Execute(null);
                break;
            case ModalKind.RepairConfirmation:
                Dialogs.CancelRepairCommand.Execute(null);
                break;
            case ModalKind.ResourcePanelSourceConfirmation:
                Dialogs.CancelResourcePanelSourceSwitchCommand.Execute(null);
                break;
            case ModalKind.UninstallConfirmation:
                Dialogs.CancelUninstallCommand.Execute(null);
                break;
            case ModalKind.Notice:
                Dialogs.DismissNoticeCommand.Execute(null);
                break;
            case ModalKind.Update:
                Dialogs.CancelUpdateAvailableCommand.Execute(null);
                break;
            case ModalKind.CrashRecovery:
                Dialogs.ContinueAfterCrashCommand.Execute(null);
                break;
            case ModalKind.LogViewer:
                LogViewer.CloseCommand.Execute(null);
                break;
            case ModalKind.SetupWizardExitConfirmation:
                Dialogs.CancelSetupWizardExitCommand.Execute(null);
                break;
            case ModalKind.Settings:
                WindowChrome.ShowSettingsCommand.Execute(null);
                break;
            case ModalKind.SetupWizard:
                Dialogs.RequestSetupWizardExitCommand.Execute(null);
                break;
            case ModalKind.ResourcePanel:
                ResourcePanel.CloseResourcePanelCommand.Execute(null);
                break;
            default:
                return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Settings.SettingsSaved -= HandleSettingsSavedAsync;
        Operations.RefreshRequested -= HandleOperationsRefreshRequestedAsync;
        ResourcePanel.ResourcePanelSourceConfirmRequested -= ShowResourcePanelSourceConfirmDialog;
        Dialogs.ConfirmResourcePanelSourceSwitchRequested -= SwitchToCafeAndOpenResourcePanel;
        Dialogs.ConfirmRepairRequested -= Operations.RepairAsync;
        Dialogs.ConfirmUninstallRequested -= Operations.ConfirmUninstallAsync;
        Dialogs.ConfirmStopRequested -= Operations.PerformStop;
        Dialogs.CloseAfterStoppingDownloadRequested -= WindowChrome.CloseAfterStoppingDownload;
        Dialogs.CloseRequested -= WindowChrome.RequestClose;
        Dialogs.CrashRecoveryResetSettingsRequested -= ResetSettingsAfterCrashAsync;
        Dialogs.CrashRecoveryViewLogRequested -= OpenCrashLog;
        Dialogs.SetupWizard.LanguagePreviewRequested -= PreviewSetupWizardLanguage;
        Dialogs.SetupWizard.SettingsApplied -= HandleSetupWizardCompletedAsync;
        WindowChrome.PropertyChanged -= OnWindowChromePropertyChanged;
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        Settings.Editor.CurrentPropertyChanged -= OnSettingPropertyChanged;
        ResourcePanel.PropertyChanged -= OnResourcePanelPropertyChanged;
        LogViewer.PropertyChanged -= OnLogViewerPropertyChanged;
        Dialogs.PropertyChanged -= OnDialogsPropertyChanged;
        Operations.StopDownload(clearPersistedState: false);
        Settings.Dispose();
        RemoteContent.Dispose();
        Background.Dispose();
        Toasts.Dispose();
        ResourcePanel.Dispose();
        lifetimeCts.Cancel();
        lifetimeCts.Dispose();
    }
}
