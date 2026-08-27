using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

public partial class GameOperationsViewModel : ViewModelBase, IGameOperationJourneyHost, IDisposable
{
    private readonly IGameOperationJourney journey;
    private readonly LocalizationService localizer;
    private readonly DialogsViewModel dialogs;
    private readonly ShellViewModel shell;
    private LauncherStatusSnapshot? currentSnapshot;
    private long runningStateVersion;
    private bool disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstallPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsControlPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsProgressPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsAnyPanelVisible))]
    private GameOperationPanelMode panelMode = GameOperationPanelMode.Install;

    public bool IsInstallPanelVisible => PanelMode == GameOperationPanelMode.Install;

    public bool IsControlPanelVisible => PanelMode == GameOperationPanelMode.Control;

    public bool IsProgressPanelVisible => PanelMode == GameOperationPanelMode.Progress;

    /// <summary>
    /// Entrance anchor for the single operation task surface (ADR-016): stays true whenever
    /// any state occupies the container so the one-shot rise never replays on state switches.
    /// </summary>
    public bool IsAnyPanelVisible =>
        PanelMode is GameOperationPanelMode.Install
            or GameOperationPanelMode.Control
            or GameOperationPanelMode.Progress;

    [ObservableProperty]
    private string installButtonText = "";

    [ObservableProperty]
    private string installButtonToolTip = "";

    [ObservableProperty]
    private string progressTitle = "";

    [ObservableProperty]
    private string progressIconKind = "Sync";

    [ObservableProperty]
    private int progressValue;

    [ObservableProperty]
    private string progressDetail = "";

    [ObservableProperty]
    private string progressSpeed = "";

    [ObservableProperty]
    private string progressSize = "";

    [ObservableProperty]
    private string progressEstimated = "";

    [ObservableProperty]
    private bool isPaused;

    [ObservableProperty]
    private bool canPauseOperation;

    [ObservableProperty]
    private string pauseResumeText = "";

    [ObservableProperty]
    private string pauseResumeIcon = "Pause";

    public event Func<GameOperationsRefreshMode, Task>? RefreshRequested
    {
        add => journey.RefreshRequested += value;
        remove => journey.RefreshRequested -= value;
    }

    public event Func<Task>? OpenLogViewerRequested
    {
        add => journey.OpenLogViewerRequested += value;
        remove => journey.OpenLogViewerRequested -= value;
    }

    public event Action? MinimizeRequested
    {
        add => journey.MinimizeRequested += value;
        remove => journey.MinimizeRequested -= value;
    }

    bool IGameOperationJourneyHost.IsBusy => shell.IsBusy;

    LauncherStatusSnapshot? IGameOperationJourneyHost.CurrentSnapshot => currentSnapshot;

    internal GameOperationsViewModel(
        GameLaunchService gameLaunchService,
        GameDownloadService gameDownloadService,
        GameUninstallService gameUninstallService,
        GameShortcutService gameShortcutService,
        LocalizationService localizer,
        ToastService toastService,
        LocalDiagnostics diagnostics,
        ShellViewModel shell,
        DialogsViewModel dialogs,
        IErrorHandlingService errorHandling)
        : this(
            new GameOperationJourneyFactory(
                new GameLaunchWorkflow(gameLaunchService),
                new GameInstallationWorkflow(gameDownloadService),
                new GameUninstallWorkflow(gameUninstallService),
                gameShortcutService,
                localizer,
                toastService,
                diagnostics,
                shell,
                dialogs,
                errorHandling),
            localizer,
            shell,
            dialogs)
    {
    }

    internal GameOperationsViewModel(
        IGameLaunchWorkflow launchWorkflow,
        IGameInstallationWorkflow installationWorkflow,
        IGameUninstallWorkflow uninstallWorkflow,
        IGameShortcutService shortcutService,
        LocalizationService localizer,
        ToastService toastService,
        LocalDiagnostics diagnostics,
        ShellViewModel shell,
        DialogsViewModel dialogs,
        Func<TimeSpan, Task> delayAsync,
        IErrorHandlingService errorHandling)
        : this(
            new GameOperationJourneyFactory(
                launchWorkflow,
                installationWorkflow,
                uninstallWorkflow,
                shortcutService,
                localizer,
                toastService,
                diagnostics,
                shell,
                dialogs,
                errorHandling,
                delayAsync),
            localizer,
            shell,
            dialogs)
    {
    }

    internal GameOperationsViewModel(
        IGameOperationJourneyFactory journeyFactory,
        LocalizationService localizer,
        ShellViewModel shell,
        DialogsViewModel dialogs)
    {
        this.localizer = localizer;
        this.dialogs = dialogs;
        this.shell = shell;
        journey = journeyFactory.Create(this);
        journey.IsRunningChanged += OnInstallationIsRunningChanged;
        dialogs.ConfirmRepairRequested += RepairAsync;
        dialogs.ConfirmUninstallRequested += ConfirmUninstallAsync;
        dialogs.ConfirmStopRequested += PerformStop;
    }

    public void ApplyLanguage()
    {
        PauseResumeText = IsPaused ? localizer.T("resume") : localizer.T("pause");
        if (string.IsNullOrWhiteSpace(ProgressTitle))
        {
            ProgressTitle = localizer.T("preparing");
        }
    }

    public void ApplySnapshot(LauncherStatusSnapshot snapshot)
    {
        currentSnapshot = snapshot;
        InstallButtonText = snapshot.RuntimeState switch
        {
            LauncherRuntimeState.NotInstalled => localizer.T("installGame"),
            LauncherRuntimeState.Corrupted => localizer.T("repair"),
            LauncherRuntimeState.IoFailure or LauncherRuntimeState.RemoteUnavailable => localizer.T("refresh"),
            _ => localizer.T("updateGame")
        };
        InstallButtonToolTip = shell.IsInstallBlockedByDiskSpace
            ? shell.InstallDiskSpaceMessage
            : InstallButtonText;
        InstallOrUpdateCommand.NotifyCanExecuteChanged();
        SetIdlePanels(snapshot);
    }

    public void SetIdlePanels(LauncherStatusSnapshot? snapshot)
    {
        CanPauseOperation = false;
        PanelMode = snapshot?.RuntimeState == LauncherRuntimeState.Ready
            ? GameOperationPanelMode.Control
            : GameOperationPanelMode.Install;
    }

    void IGameOperationJourneyHost.SetBusy(bool busy) => shell.IsBusy = busy;
    void IGameOperationJourneyHost.PrepareOperation()
    {
        shell.IsBusy = true;
        PanelMode = GameOperationPanelMode.Progress;
        ProgressTitle = localizer.T("preparing");
        ProgressIconKind = ResolveProgressPresentation(GameOperationKind.Idle).IconKind;
        ProgressValue = 0;
        ProgressDetail = localizer.T("buildingFileList");
        ProgressSpeed = "";
        ProgressSize = "";
        ProgressEstimated = "";
        IsPaused = false;
        CanPauseOperation = false;
        PauseResumeText = localizer.T("pause");
        PauseResumeIcon = "Pause";
    }

    void IGameOperationJourneyHost.ApplySnapshot(LauncherStatusSnapshot snapshot)
    {
        ApplySnapshot(snapshot);
    }

    [RelayCommand]
    private async Task StartGameAsync()
    {
        if (currentSnapshot is not null)
            await journey.StartGameAsync(currentSnapshot);
    }

    [RelayCommand]
    private async Task CheckForGameUpdateAsync()
    {
        if (currentSnapshot is not null)
            await journey.CheckForUpdateAsync(currentSnapshot);
    }

    [RelayCommand]
    private async Task CreateGameShortcutAsync()
    {
        if (currentSnapshot is not null)
            await journey.CreateDesktopShortcutAsync(currentSnapshot);
    }

    [RelayCommand]
    private void OpenGameFolder()
    {
        if (currentSnapshot is not null)
        {
            journey.OpenGameFolder(currentSnapshot);
        }
    }

    private bool CanInstallOrUpdate() => !shell.IsInstallBlockedByDiskSpace;

    [RelayCommand(CanExecute = nameof(CanInstallOrUpdate))]
    private async Task InstallOrUpdateAsync()
    {
        if (currentSnapshot is not null)
            await journey.InstallOrUpdateAsync(currentSnapshot);
    }

    [RelayCommand]
    private async Task RequestRepairAsync()
    {
        if (currentSnapshot is not null)
            await journey.RequestRepairAsync(currentSnapshot);
    }

    public async Task RepairAsync()
    {
        if (currentSnapshot is not null && currentSnapshot.RuntimeState is LauncherRuntimeState.Corrupted or LauncherRuntimeState.Ready)
            await journey.RepairAsync(currentSnapshot);
    }

    [RelayCommand]
    private void StopOperation()
    {
        journey.RequestStop();
    }

    public void PerformStop()
    {
        journey.PerformStop();
    }

    [RelayCommand]
    private void PauseResume()
    {
        if (!CanPauseOperation)
        {
            return;
        }

        if (journey.IsPaused)
        {
            journey.Resume();
            IsPaused = false;
            PauseResumeText = localizer.T("pause");
            PauseResumeIcon = "Pause";
            ProgressDetail = localizer.T("downloading");
        }
        else
        {
            journey.Pause();
            IsPaused = true;
            PauseResumeText = localizer.T("resume");
            PauseResumeIcon = "Play";
            ProgressDetail = localizer.T("paused");
            ProgressSpeed = "";
            ProgressEstimated = "";
        }
    }

    [RelayCommand]
    private async Task RequestUninstallAsync()
    {
        if (currentSnapshot is not null)
            await journey.RequestUninstallAsync(currentSnapshot);
    }

    public async Task ConfirmUninstallAsync()
    {
        if (currentSnapshot is not null)
        {
            // Set uninstall icon before the journey runs so the test sees it
            ProgressIconKind = ResolveProgressPresentation(GameOperationKind.Uninstall).IconKind;
            await journey.ConfirmUninstallAsync(currentSnapshot);
        }
    }

    public async Task ResumePersistedDownloadAsync(CancellationToken cancellationToken)
    {
        if (currentSnapshot is not null)
            await journey.ResumePersistedAsync(currentSnapshot, cancellationToken);
    }

    public void StopDownload(bool clearPersistedState)
    {
        journey.Stop(clearPersistedState);
    }

    public bool IsDownloadRunning => journey.IsDownloadRunning;

    public void ApplyProgress(GameOperationProgress progress)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            if (Application.Current is null)
            {
                ApplyProgressCore(progress);
                return;
            }

            Dispatcher.UIThread.Post(() => ApplyProgress(progress));
            return;
        }

        ApplyProgressCore(progress);
    }

    private void ApplyProgressCore(GameOperationProgress progress)
    {
        PanelMode = GameOperationPanelMode.Progress;
        ProgressValue = Math.Clamp(progress.Progress, 0, 100);
        var progressPresentation = ResolveProgressPresentation(progress.OperationKind);
        ProgressTitle = progressPresentation.Title;
        ProgressIconKind = progressPresentation.IconKind;
        ProgressDetail = progress.Stage switch
        {
            GameOperationStage.RepairConfirmation => progress.AffectedFileCount > 0
                ? localizer.F(
                    "repairFilesNeeded",
                    progress.AffectedFileCount,
                    FileSizeFormatter.Format(progress.DownloadedSize))
                : localizer.T("repairNoFilesNeeded"),
            GameOperationStage.Paused => localizer.T("paused"),
            GameOperationStage.RepairCheck => localizer.T("repairCheckingFiles"),
            GameOperationStage.UpdateCheck => localizer.T("updateCheckingFiles"),
            GameOperationStage.FileCheck => localizer.T("verifyingDownloadedFiles"),
            GameOperationStage.DiskCheck => localizer.F(
                "diskSpaceCheck",
                FileSizeFormatter.Format(progress.RequiredDiskBytes),
                progress.AvailableDiskBytes.HasValue
                    ? FileSizeFormatter.Format(progress.AvailableDiskBytes.Value)
                    : "--"),
            GameOperationStage.VerificationRetry => localizer.F(
                "verificationRetry",
                progress.FailedFileCount,
                progress.RetryAttempt,
                progress.RetryLimit),
            GameOperationStage.VerificationFailed => localizer.F("verificationFailed", progress.FailedFileCount),
            GameOperationStage.RepairCompleted => localizer.T("repairCompleted"),
            GameOperationStage.DownloadCompleted => localizer.T("installUpdateCompleted"),
            GameOperationStage.Stopped => localizer.T("operationStopped"),
            GameOperationStage.Downloading => localizer.T("downloading"),
            GameOperationStage.Uninstalling => localizer.T("uninstalling"),
            GameOperationStage.Idle => localizer.T("working"),
            _ => throw new ArgumentOutOfRangeException(nameof(progress), progress.Stage, null)
        };
        var clearsDownloadMetrics = progress.Stage is
            GameOperationStage.RepairConfirmation or GameOperationStage.Paused
            or GameOperationStage.DiskCheck or GameOperationStage.VerificationRetry
            or GameOperationStage.VerificationFailed;
        ProgressSpeed = clearsDownloadMetrics || progress.BytesPerSecond <= 0
            ? ""
            : $"{FileSizeFormatter.Format(progress.BytesPerSecond)}/S";
        ProgressSize = progress.TotalSize > 0 && !clearsDownloadMetrics
            ? $"{FileSizeFormatter.Format(progress.DownloadedSize)} / {FileSizeFormatter.Format(progress.TotalSize)}"
            : "";
        ProgressEstimated = progress.TotalSize > 0
            && progress.Stage == GameOperationStage.Downloading
            && progress.EstimatedRemaining.HasValue
            ? localizer.F(
                "estimatedTimeRemaining",
                progress.EstimatedRemaining.Value.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture))
            : "";
        IsPaused = progress.IsPaused;
        CanPauseOperation = progress.CanPause;
        PauseResumeText = progress.IsPaused ? localizer.T("resume") : localizer.T("pause");
        PauseResumeIcon = progress.IsPaused ? "Play" : "Pause";
    }

    private (string Title, string IconKind) ResolveProgressPresentation(GameOperationKind operationKind)
    {
        return operationKind switch
        {
            GameOperationKind.Repair => (localizer.T("repairing"), "Tools"),
            GameOperationKind.Uninstall => (localizer.T("uninstalling"), "DeleteOutline"),
            GameOperationKind.Download => (localizer.T("downloading"), "Download"),
            GameOperationKind.Idle => (localizer.T("working"), "Sync"),
            _ => throw new ArgumentOutOfRangeException(nameof(operationKind), operationKind, null)
        };
    }

    private void OnInstallationIsRunningChanged()
    {
        var version = Interlocked.Increment(ref runningStateVersion);
        if (disposed)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess() && Application.Current is not null)
        {
            Dispatcher.UIThread.Post(() => NotifyDownloadRunningChanged(version));
            return;
        }

        NotifyDownloadRunningChanged(version);
    }

    private void NotifyDownloadRunningChanged(long version)
    {
        if (disposed || version != Interlocked.Read(ref runningStateVersion))
        {
            return;
        }

        OnPropertyChanged(nameof(IsDownloadRunning));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        journey.IsRunningChanged -= OnInstallationIsRunningChanged;
        dialogs.ConfirmRepairRequested -= RepairAsync;
        dialogs.ConfirmUninstallRequested -= ConfirmUninstallAsync;
        dialogs.ConfirmStopRequested -= PerformStop;
    }

}
