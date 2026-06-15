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

    public MainWindowViewModel(
        ILauncherCoreService launcherCoreService,
        LauncherSettingsService settingsService,
        LocalizationService localizer,
        ToastService toastService,
        LocalDiagnostics diagnostics,
        ShellViewModel shell,
        BackgroundViewModel background,
        RemoteContentViewModel remoteContent,
        DialogsViewModel dialogs,
        GameOperationsViewModel operations,
        ToastHostViewModel toasts,
        WindowChromeViewModel windowChrome,
        SettingsViewModel settingsViewModel,
        ResourcePanelViewModel resourcePanelViewModel)
    {
        this.launcherCoreService = launcherCoreService;
        this.settingsService = settingsService;
        this.localizer = localizer;
        this.toastService = toastService;
        this.diagnostics = diagnostics;

        Shell = shell;
        Background = background;
        RemoteContent = remoteContent;
        Dialogs = dialogs;
        Operations = operations;
        Toasts = toasts;
        WindowChrome = windowChrome;
        Settings = settingsViewModel;
        ResourcePanel = resourcePanelViewModel;

        WireChildren();
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

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Shell.IsBusy = true;
        var loaded = false;
        try
        {
            var settingsForLanguage = await settingsService.ReadAsync(cancellationToken);
            ApplyLanguage(settingsForLanguage.Language);
            Settings.BulkUpdate(s =>
            {
                s.SelectedThemeMode = settingsForLanguage.ThemeMode;
                s.LoadThemeColorState(settingsForLanguage);
            });
            SettingsViewModel.ApplyTheme(settingsForLanguage.ThemeMode);
            Settings.ApplyThemeColor(settingsForLanguage.ThemeColorMode, SettingsViewModel.ParseColorOrDefault(settingsForLanguage.CustomThemeColor));
            Shell.SetLoading();

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
            await TryLogErrorAsync("Launcher core refresh failed.", exception);
        }
        finally
        {
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
        Background.Configure(Settings);
        Toasts.Configure(Settings);

        Settings.GetSnapshot = () => currentSnapshot;
        Settings.GetBackgroundBitmap = Background.GetBackgroundBitmap;
        Settings.ApplyLanguageAndTheme = async s =>
        {
            ApplyLanguage(s.Language);
            SettingsViewModel.ApplyTheme(s.ThemeMode);
            // Background is intentionally NOT updated here.
            // Both callers (SaveSettingsAsync, ChooseGamePathAsync) fire SettingsSaved
            // immediately after, which triggers RefreshAsync → ApplySnapshotAsync →
            // Background.UpdateBackgroundImageAsync. Updating it here too would cause
            // a double-update; for folder-based (random) backgrounds each update picks a
            // different image, so the wallpaper visibly flickers between two random picks.
            Settings.ApplyThemeColor(s.ThemeColorMode, SettingsViewModel.ParseColorOrDefault(s.CustomThemeColor));
        };
        Settings.SettingsSaved += HandleSettingsSavedAsync;
        Settings.CloseRequested += () => WindowChrome.IsSettingsVisible = false;

        ResourcePanel.GetProxyMode = () => currentSnapshot?.Settings.ProxyMode ?? ProxyModes.Direct;
        ResourcePanel.GetPatchUrlGroup = () => currentSnapshot?.Settings.PatchUrlGroup ?? PatchUrlGroups.Official;
        ResourcePanel.ResourcePanelSourceConfirmRequested += ShowResourcePanelSourceConfirmDialog;
        Dialogs.ConfirmResourcePanelSourceSwitchRequested += SwitchToCafeAndOpenResourcePanel;

        Operations.Configure(Shell, Dialogs);
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

        RemoteContent.OpenExternalUrlRequested = WindowChrome.OpenExternalUrl;

        WindowChrome.Configure(Settings, RemoteContent, Dialogs, Operations);
        WindowChrome.GetSnapshot = () => currentSnapshot;
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
        var settings = await settingsService.ReadAsync();
        settings.PatchUrlGroup = PatchUrlGroups.Cafe;
        await settingsService.SaveAsync(settings);
        Settings.SelectedPatchUrlGroup = PatchUrlGroups.Cafe;

        await HandleSettingsSavedAsync();
        await ResourcePanel.OpenPanelDirectly();
    }

    private async Task HandleSettingsSavedAsync()
    {
        var previousPatchUrlGroup = currentSnapshot?.Settings.PatchUrlGroup;
        var savedPatchUrlGroup = Settings.SelectedPatchUrlGroup;
        await RefreshAsync();
        if (currentSnapshot?.IsInstalled == true
            && !string.Equals(previousPatchUrlGroup, savedPatchUrlGroup, StringComparison.Ordinal))
        {
            Dialogs.ShowRepairConfirm(localizer.T("downloadSourceChangedRepairPrompt"));
        }
    }

    private async Task ApplySnapshotAsync(LauncherStatusSnapshot snapshot)
    {
        ApplySettingsSnapshot(snapshot.Settings, snapshot.LocalGame.GamePath);
        ApplyLanguage(snapshot.Settings.Language);
        SettingsViewModel.ApplyTheme(snapshot.Settings.ThemeMode);
        await Background.UpdateBackgroundImageAsync(snapshot, lifetimeCts.Token);
        Settings.ApplyThemeColor(snapshot.Settings.ThemeColorMode, SettingsViewModel.ParseColorOrDefault(snapshot.Settings.CustomThemeColor));

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

    private async Task TryLogErrorAsync(string title, Exception exception)
    {
        try
        {
            await diagnostics.ErrorAsync(title, exception);
        }
        catch
        {
            Shell.OperationNote = $"{Shell.OperationNote} Local diagnostics log write failed.";
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ResourcePanel.ResourcePanelSourceConfirmRequested -= ShowResourcePanelSourceConfirmDialog;
        Dialogs.ConfirmResourcePanelSourceSwitchRequested -= SwitchToCafeAndOpenResourcePanel;
        Dialogs.ConfirmRepairRequested -= Operations.RepairAsync;
        Dialogs.ConfirmUninstallRequested -= Operations.ConfirmUninstallAsync;
        Dialogs.ConfirmStopRequested -= Operations.PerformStop;
        Dialogs.CloseAfterStoppingDownloadRequested -= WindowChrome.CloseAfterStoppingDownload;
        Operations.StopDownload(clearPersistedState: false);
        RemoteContent.Dispose();
        Background.Dispose();
        Toasts.Dispose();
        lifetimeCts.Cancel();
        lifetimeCts.Dispose();
    }
}
