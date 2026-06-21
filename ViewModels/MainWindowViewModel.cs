using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ILauncherCoreService launcherCoreService;
    private readonly LauncherSettingsService settingsService;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly LocalDiagnostics diagnostics;
    private readonly OldLauncherDetectionService oldLauncherService;
    private readonly CancellationTokenSource lifetimeCts = new();
    private int initialized;
    private bool disposed;
    private bool skipNextPersistedResume;
    private LauncherStatusSnapshot? currentSnapshot;

    public ShellViewModel Shell { get; }

    public BackgroundViewModel Background { get; }

    public RemoteContentViewModel RemoteContent { get; }

    public DialogsViewModel Dialogs { get; }

    public GameOperationsViewModel Operations { get; }

    public ToastHostViewModel Toasts { get; }

    public WindowChromeViewModel WindowChrome { get; }

    public SettingsViewModel Settings { get; }

    public ResourcePanelViewModel ResourcePanel { get; }

    public MigrationWizardViewModel MigrationWizard { get; }

    public MainWindowViewModel(
        ILauncherCoreService launcherCoreService,
        LauncherSettingsService settingsService,
        LocalizationService localizer,
        ToastService toastService,
        LocalDiagnostics diagnostics,
        OldLauncherDetectionService oldLauncherService,
        ShellViewModel shell,
        BackgroundViewModel background,
        RemoteContentViewModel remoteContent,
        DialogsViewModel dialogs,
        GameOperationsViewModel operations,
        ToastHostViewModel toasts,
        WindowChromeViewModel windowChrome,
        SettingsViewModel settingsViewModel,
        ResourcePanelViewModel resourcePanelViewModel,
        MigrationWizardViewModel migrationWizard)
    {
        this.launcherCoreService = launcherCoreService;
        this.settingsService = settingsService;
        this.localizer = localizer;
        this.toastService = toastService;
        this.diagnostics = diagnostics;
        this.oldLauncherService = oldLauncherService;

        Shell = shell;
        Background = background;
        RemoteContent = remoteContent;
        Dialogs = dialogs;
        Operations = operations;
        Toasts = toasts;
        WindowChrome = windowChrome;
        Settings = settingsViewModel;
        ResourcePanel = resourcePanelViewModel;
        MigrationWizard = migrationWizard;

        WireChildren();
        ApplyLanguage(LauncherLanguages.Auto);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref initialized, 1) == 1)
        {
            return;
        }

        // Check for first-launch migration from old launcher
        var settings = await settingsService.ReadAsync(cancellationToken);
        if (!settings.HasCompletedFirstLaunchWizard)
        {
            var detection = oldLauncherService.Detect();
            if (detection is not null)
            {
                MigrationWizard.Load(detection);
                MigrationWizard.IsVisible = true;
                return; // Wait for user to complete or skip wizard
            }

            // No old launcher found — mark complete, never show again
            settings.HasCompletedFirstLaunchWizard = true;
            await settingsService.SaveAsync(settings, cancellationToken);
        }

        await RefreshAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Shell.IsBusy = true;
        var loaded = false;
        try
        {
            var settingsForLanguage = await settingsService.ReadAsync(cancellationToken);
            ApplyLanguage(settingsForLanguage.Language);
            Settings.Appearance.Load(settingsForLanguage);
            SettingsAppearanceViewModel.ApplyTheme(settingsForLanguage.ThemeMode);
            Settings.Appearance.ApplyThemeColor(
                settingsForLanguage.ThemeColorMode,
                SettingsAppearanceViewModel.ParseColorOrDefault(settingsForLanguage.CustomThemeColor));
            Shell.SetLoading();
            RemoteContent.BeginLoading(settingsForLanguage.ShowRemoteContentCard);

            // Migrate game path from original Yostar launcher on first run
            if (string.IsNullOrWhiteSpace(settingsForLanguage.GamePath))
            {
                var migratedPath = OriginalLauncherMigrationService.TryGetGamePath();
                if (migratedPath is not null)
                {
                    settingsForLanguage.GamePath = migratedPath;
                    await settingsService.SaveAsync(settingsForLanguage, cancellationToken);
                }
            }

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

        ResourcePanel.GetProxyMode = () => currentSnapshot?.Settings.ProxyMode ?? ProxyModes.Direct;
        ResourcePanel.GetPatchUrlGroup = () => currentSnapshot?.Settings.PatchUrlGroup ?? PatchUrlGroups.Official;
        ResourcePanel.ResourcePanelSourceConfirmRequested += ShowResourcePanelSourceConfirmDialog;
        Dialogs.ConfirmResourcePanelSourceSwitchRequested += SwitchToCafeAndOpenResourcePanel;

        Operations.GetSnapshot = () => currentSnapshot;
        Operations.RequestRefreshAsync = async () => await RefreshAsync();
        Operations.RequestRefreshAfterPersistedResumeAsync = async () =>
        {
            skipNextPersistedResume = true;
            await RefreshAsync();
        };
        Operations.ApplySnapshotAsync = ApplySnapshotAsync;

        Dialogs.ConfirmRepairRequested += Operations.RepairAsync;
        Dialogs.ConfirmUninstallRequested += Operations.ConfirmUninstallAsync;
        Dialogs.ConfirmStopRequested += Operations.PerformStop;
        Dialogs.CloseAfterStoppingDownloadRequested += WindowChrome.CloseAfterStoppingDownload;
        Dialogs.CloseRequested += () => WindowChrome.CloseWindow?.Invoke();
        Dialogs.ConfirmUpdateAvailableRequested += url => ExternalLinkService.Open(url);

        RemoteContent.OpenExternalUrlRequested = WindowChrome.OpenExternalUrl;

        WindowChrome.GetSnapshot = () => currentSnapshot;

        MigrationWizard.MigrationApplied += HandleMigrationAppliedAsync;
        MigrationWizard.MigrationSkipped += HandleMigrationSkippedAsync;
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

    private async Task HandleMigrationAppliedAsync(LauncherSettings migratedSettings)
    {
        migratedSettings.HasCompletedFirstLaunchWizard = true;
        await settingsService.SaveAsync(migratedSettings, CancellationToken.None);
        MigrationWizard.IsVisible = false;
        await RefreshAsync(CancellationToken.None);
    }

    private async Task HandleMigrationSkippedAsync()
    {
        var settings = new LauncherSettings { HasCompletedFirstLaunchWizard = true };
        await settingsService.SaveAsync(settings, CancellationToken.None);
        MigrationWizard.IsVisible = false;
        await RefreshAsync(CancellationToken.None);
    }

    private async Task ApplySnapshotAsync(LauncherStatusSnapshot snapshot)
    {
        ApplySettingsSnapshot(snapshot.Settings, snapshot.LocalGame.GamePath);
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

    private void ApplySettingsSnapshot(LauncherSettings settings, string localGamePath)
    {
        Settings.ApplyLauncherSettings(settings, localGamePath);
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

    // ── Window interaction (Escape key resolution) ──────────────────────

    /// <summary>
    /// Build the current UI state snapshot for Escape key resolution.
    /// </summary>
    public WindowInteractionState BuildEscapeState() => new()
    {
        IsMigrationVisible = MigrationWizard.IsVisible,
        IsDownloadRunningCloseConfirmVisible = Dialogs.IsDownloadRunningCloseConfirmVisible,
        IsStopConfirmVisible = Dialogs.IsStopConfirmVisible,
        IsUnsavedChangesVisible = Settings.IsUnsavedChangesVisible,
        IsRepairConfirmVisible = Dialogs.IsRepairConfirmVisible,
        IsResourcePanelSourceConfirmVisible = Dialogs.IsResourcePanelSourceConfirmVisible,
        IsUninstallConfirmVisible = Dialogs.IsUninstallConfirmVisible,
        IsNoticeDialogVisible = Dialogs.IsNoticeDialogVisible,
        IsSettingsVisible = WindowChrome.IsSettingsVisible,
        IsResourcePanelVisible = ResourcePanel.IsResourcePanelVisible,
    };

    /// <summary>
    /// Execute the command corresponding to the given Escape action.
    /// </summary>
    private void ExecuteEscapeAction(WindowEscapeAction action)
    {
        switch (action)
        {
            case WindowEscapeAction.SkipMigration:
                MigrationWizard.SkipMigrationCommand.Execute(null);
                break;
            case WindowEscapeAction.CancelCloseWhileDownloading:
                Dialogs.CancelCloseWhileDownloadingCommand.Execute(null);
                break;
            case WindowEscapeAction.CancelStop:
                Dialogs.CancelStopCommand.Execute(null);
                break;
            case WindowEscapeAction.KeepEditingSettings:
                WindowChrome.KeepEditingSettingsCommand.Execute(null);
                break;
            case WindowEscapeAction.CancelRepair:
                Dialogs.CancelRepairCommand.Execute(null);
                break;
            case WindowEscapeAction.CancelResourcePanelSourceSwitch:
                Dialogs.CancelResourcePanelSourceSwitchCommand.Execute(null);
                break;
            case WindowEscapeAction.CancelUninstall:
                Dialogs.CancelUninstallCommand.Execute(null);
                break;
            case WindowEscapeAction.DismissNotice:
                Dialogs.DismissNoticeCommand.Execute(null);
                break;
            case WindowEscapeAction.ToggleSettings:
                WindowChrome.ShowSettingsCommand.Execute(null);
                break;
            case WindowEscapeAction.CloseResourcePanel:
                ResourcePanel.CloseResourcePanelCommand.Execute(null);
                break;
        }
    }

    /// <summary>
    /// Attempt to handle the Escape key press.
    /// Returns true if a visible overlay/dialog was dismissed, false if no action was needed.
    /// </summary>
    public bool TryHandleEscape()
    {
        var action = WindowEscapeStrategy.ResolveEscape(BuildEscapeState());
        if (action is null)
        {
            return false;
        }

        ExecuteEscapeAction(action.Value);
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
        MigrationWizard.MigrationApplied -= HandleMigrationAppliedAsync;
        MigrationWizard.MigrationSkipped -= HandleMigrationSkippedAsync;
        ResourcePanel.ResourcePanelSourceConfirmRequested -= ShowResourcePanelSourceConfirmDialog;
        Dialogs.ConfirmResourcePanelSourceSwitchRequested -= SwitchToCafeAndOpenResourcePanel;
        Dialogs.ConfirmRepairRequested -= Operations.RepairAsync;
        Dialogs.ConfirmUninstallRequested -= Operations.ConfirmUninstallAsync;
        Dialogs.ConfirmStopRequested -= Operations.PerformStop;
        Dialogs.CloseAfterStoppingDownloadRequested -= WindowChrome.CloseAfterStoppingDownload;
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
