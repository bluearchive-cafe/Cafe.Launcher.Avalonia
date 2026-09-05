using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

public partial class GameOperationsViewModel : ViewModelBase, IGameOperationJourneyHost, IGameOperationActivity, IDisposable
{
    private readonly IGameOperationJourney journey;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
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

    /// <summary>Raised when shell state must be refreshed after an operation (driven by the journey host).</summary>
    public event Func<GameOperationsRefreshMode, Task>? RefreshRequested;

    /// <summary>Raised when a failure action should open the log viewer (driven by the journey host).</summary>
    public event Func<Task>? OpenLogViewerRequested;

    /// <summary>Raised when a successful launch should minimize the launcher (driven by the journey host).</summary>
    public event Action? MinimizeRequested;

    bool IGameOperationJourneyHost.IsBusy => shell.IsBusy;

    LauncherStatusSnapshot? IGameOperationJourneyHost.CurrentSnapshot => currentSnapshot;

    internal GameOperationsViewModel(
        IGameOperationExecutor executor,
        IGameShortcutService gameShortcutService,
        LocalizationService localizer,
        ToastService toastService,
        LocalDiagnostics diagnostics,
        ShellViewModel shell,
        DialogsViewModel dialogs,
        IErrorHandlingService errorHandling,
        Func<TimeSpan, Task>? delayAsync = null)
    {
        this.localizer = localizer;
        this.toastService = toastService;
        this.dialogs = dialogs;
        this.shell = shell;
        journey = new GameOperationJourney(
            executor,
            gameShortcutService,
            localizer,
            toastService,
            diagnostics,
            errorHandling,
            delayAsync ?? Task.Delay,
            this);
        journey.IsRunningChanged += OnInstallationIsRunningChanged;
        dialogs.ConfirmRepairRequested += RepairAsync;
        dialogs.ConfirmUninstallRequested += ConfirmUninstallAsync;
        dialogs.ConfirmStopRequested += PerformStop;
    }

    public void ApplyLanguage()
    {
        PauseResumeText = IsPaused ? localizer.T(LocalizationKeys.Resume) : localizer.T(LocalizationKeys.Pause);
        if (string.IsNullOrWhiteSpace(ProgressTitle))
        {
            ProgressTitle = localizer.T(LocalizationKeys.Preparing);
        }
    }

    public void ApplySnapshot(LauncherStatusSnapshot snapshot)
    {
        currentSnapshot = snapshot;
        InstallButtonText = snapshot.RuntimeState switch
        {
            LauncherRuntimeState.NotInstalled => localizer.T(LocalizationKeys.InstallGame),
            LauncherRuntimeState.Corrupted => localizer.T(LocalizationKeys.Repair),
            LauncherRuntimeState.IoFailure or LauncherRuntimeState.RemoteUnavailable => localizer.T(LocalizationKeys.Refresh),
            _ => localizer.T(LocalizationKeys.UpdateGame)
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
        ProgressTitle = localizer.T(LocalizationKeys.Preparing);
        ProgressIconKind = ResolveProgressPresentation(GameOperationKind.Idle).IconKind;
        ProgressValue = 0;
        ProgressDetail = localizer.T(LocalizationKeys.BuildingFileList);
        ProgressSpeed = "";
        ProgressSize = "";
        ProgressEstimated = "";
        IsPaused = false;
        CanPauseOperation = false;
        PauseResumeText = localizer.T(LocalizationKeys.Pause);
        PauseResumeIcon = "Pause";
    }

    void IGameOperationJourneyHost.ApplySnapshot(LauncherStatusSnapshot snapshot)
    {
        ApplySnapshot(snapshot);
    }

    void IGameOperationJourneyHost.SetLaunchCheckResult(string message) =>
        shell.SetLaunchCheckResult(message);

    void IGameOperationJourneyHost.ShowRepairConfirmation(string message) =>
        dialogs.ShowRepairConfirm(message);

    Task<bool> IGameOperationJourneyHost.RefreshAsync(GameOperationsRefreshMode mode)
    {
        if (RefreshRequested is null)
        {
            return Task.FromResult(false);
        }

        return RefreshAndReportAsync(mode);
    }

    private async Task<bool> RefreshAndReportAsync(GameOperationsRefreshMode mode)
    {
        await AsyncEvent.InvokeSequentiallyAsync(RefreshRequested, mode);
        return true;
    }

    Task IGameOperationJourneyHost.ShowLogViewerAsync() =>
        AsyncEvent.InvokeSequentiallyAsync(OpenLogViewerRequested);

    void IGameOperationJourneyHost.RequestMinimize() => MinimizeRequested?.Invoke();

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
        if (currentSnapshot is null)
        {
            return;
        }

        if (currentSnapshot.RuntimeState is not (LauncherRuntimeState.Corrupted or LauncherRuntimeState.Ready))
        {
            toastService.ShowWarning(localizer.T(LocalizationKeys.OperationUnavailableForCurrentState));
            return;
        }

        dialogs.ShowRepairConfirm(localizer.T(LocalizationKeys.RepairWarning));
    }

    public async Task RepairAsync()
    {
        if (currentSnapshot is not null && currentSnapshot.RuntimeState is LauncherRuntimeState.Corrupted or LauncherRuntimeState.Ready)
            await journey.RepairAsync(currentSnapshot);
    }

    [RelayCommand]
    public void StopOperation()
    {
        if (journey.IsDownloadRunning)
        {
            dialogs.ShowStopConfirm();
            return;
        }

        journey.PerformStop();
    }

    public void PerformStop()
    {
        journey.PerformStop();
    }

    [RelayCommand]
    public void PauseResume()
    {
        if (!CanPauseOperation)
        {
            return;
        }

        if (journey.IsPaused)
        {
            journey.Resume();
            IsPaused = false;
            PauseResumeText = localizer.T(LocalizationKeys.Pause);
            PauseResumeIcon = "Pause";
            ProgressDetail = localizer.T(LocalizationKeys.Downloading);
        }
        else
        {
            journey.Pause();
            IsPaused = true;
            PauseResumeText = localizer.T(LocalizationKeys.Resume);
            PauseResumeIcon = "Play";
            ProgressDetail = localizer.T(LocalizationKeys.Paused);
            ProgressSpeed = "";
            ProgressEstimated = "";
        }
    }

    [RelayCommand]
    private async Task RequestUninstallAsync()
    {
        if (currentSnapshot is null)
        {
            return;
        }

        if (currentSnapshot.RuntimeState != LauncherRuntimeState.Ready)
        {
            toastService.ShowWarning(localizer.T(LocalizationKeys.OperationUnavailableForCurrentState));
            return;
        }

        var validation = await journey.ValidateUninstallAsync(currentSnapshot);
        if (validation is null)
        {
            return;
        }

        dialogs.ShowUninstallConfirm(localizer.F(
            LocalizationKeys.UninstallConfirmText,
            currentSnapshot.LocalGame.GamePath,
            Math.Max(0, validation.AffectedFileCount - 2)));
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
                    LocalizationKeys.RepairFilesNeeded,
                    progress.AffectedFileCount,
                    FileSizeFormatter.Format(progress.DownloadedSize))
                : localizer.T(LocalizationKeys.RepairNoFilesNeeded),
            GameOperationStage.Paused => localizer.T(LocalizationKeys.Paused),
            GameOperationStage.RepairCheck => localizer.T(LocalizationKeys.RepairCheckingFiles),
            GameOperationStage.UpdateCheck => localizer.T(LocalizationKeys.UpdateCheckingFiles),
            GameOperationStage.FileCheck => localizer.T(LocalizationKeys.VerifyingDownloadedFiles),
            GameOperationStage.DiskCheck => localizer.F(
                LocalizationKeys.DiskSpaceCheck,
                FileSizeFormatter.Format(progress.RequiredDiskBytes),
                progress.AvailableDiskBytes.HasValue
                    ? FileSizeFormatter.Format(progress.AvailableDiskBytes.Value)
                    : "--"),
            GameOperationStage.VerificationRetry => localizer.F(
                LocalizationKeys.VerificationRetry,
                progress.FailedFileCount,
                progress.RetryAttempt,
                progress.RetryLimit),
            GameOperationStage.VerificationFailed => localizer.F(LocalizationKeys.VerificationFailed, progress.FailedFileCount),
            GameOperationStage.RepairCompleted => localizer.T(LocalizationKeys.RepairCompleted),
            GameOperationStage.DownloadCompleted => localizer.T(LocalizationKeys.InstallUpdateCompleted),
            GameOperationStage.Stopped => localizer.T(LocalizationKeys.OperationStopped),
            GameOperationStage.Downloading => localizer.T(LocalizationKeys.Downloading),
            GameOperationStage.Uninstalling => localizer.T(LocalizationKeys.Uninstalling),
            GameOperationStage.Idle => localizer.T(LocalizationKeys.Working),
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
                LocalizationKeys.EstimatedTimeRemaining,
                progress.EstimatedRemaining.Value.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture))
            : "";
        IsPaused = progress.IsPaused;
        CanPauseOperation = progress.CanPause;
        PauseResumeText = progress.IsPaused ? localizer.T(LocalizationKeys.Resume) : localizer.T(LocalizationKeys.Pause);
        PauseResumeIcon = progress.IsPaused ? "Play" : "Pause";
    }

    private (string Title, string IconKind) ResolveProgressPresentation(GameOperationKind operationKind)
    {
        return operationKind switch
        {
            GameOperationKind.Repair => (localizer.T(LocalizationKeys.Repairing), "Tools"),
            GameOperationKind.Uninstall => (localizer.T(LocalizationKeys.Uninstalling), "DeleteOutline"),
            GameOperationKind.Download => (localizer.T(LocalizationKeys.Downloading), "Download"),
            GameOperationKind.Idle => (localizer.T(LocalizationKeys.Working), "Sync"),
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

    /// <inheritdoc />
    public event PropertyChangedEventHandler? ActivityPropertyChanged
    {
        add => PropertyChanged += value;
        remove => PropertyChanged -= value;
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
