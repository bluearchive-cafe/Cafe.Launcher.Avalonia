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
    private readonly GameOperationJourney journey;
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

    // 同阶段进度单调钳制状态（AUD-PERF-006）：见 ApplyProgressCore。
    private GameOperationStage displayedProgressStage = GameOperationStage.Idle;
    private int displayedProgressFloor;

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

    /// <summary>
    /// 卸载确认框里「彻底清除」的勾选状态（ADR-030）。每次打开确认框重置为 false——
    /// 破坏性选项不预置，用户必须主动勾。
    /// </summary>
    [ObservableProperty]
    private bool isThoroughUninstallSelected;

    /// <summary>
    /// 彻底清除选项的标签：安装目录与受管 Prefix 的实测大小（ADR-030）。
    /// 空字符串时确认框整行折叠（其他确认框都不设它）。
    /// </summary>
    [ObservableProperty]
    private string thoroughUninstallOptionText = "";

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

    /// <summary>Raised when a successful launch should exit the launcher (driven by the journey host).</summary>
    public event Action? ExitRequested;

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
        dialogs.RepairConfirm.Confirmed += RepairAsync;
        dialogs.UninstallConfirm.Confirmed += ConfirmUninstallAsync;
        dialogs.StopConfirm.Confirmed += PerformStop;
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
        dialogs.RepairConfirm.Show(message);

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

    void IGameOperationJourneyHost.RequestExit() => ExitRequested?.Invoke();

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

        if (GameOperationPolicy.Decide(GameOperationPolicy.Operation.Repair, currentSnapshot.RuntimeState)
            == GameOperationDecision.RejectedForCurrentState)
        {
            ShowOperationUnavailable();
            return;
        }

        dialogs.RepairConfirm.Show(localizer.T(LocalizationKeys.RepairWarning));
    }

    public async Task RepairAsync()
    {
        // 确认框点过之后状态可能又变了：不能静默什么都不做（ADR-027）。
        if (currentSnapshot is null)
        {
            return;
        }

        if (GameOperationPolicy.Decide(GameOperationPolicy.Operation.Repair, currentSnapshot.RuntimeState)
            == GameOperationDecision.RejectedForCurrentState)
        {
            ShowOperationUnavailable();
            return;
        }

        await journey.RepairAsync(currentSnapshot);
    }

    [RelayCommand]
    public void RequestStop()
    {
        if (journey.IsDownloadRunning)
        {
            dialogs.ShowStopConfirm();
            return;
        }

        journey.PerformStop();
    }

    /// <summary>
    /// 按意图停止活动工作流。检查点去留是域内策略：调用方只说为什么停，
    /// 由 <see cref="ToStopReason"/> 翻译成下载模块的停止原因。
    /// </summary>
    public void StopOperation(GameOperationStopIntent intent) => journey.Stop(ToStopReason(intent));

    private static DownloadStopReason ToStopReason(GameOperationStopIntent intent) => intent switch
    {
        GameOperationStopIntent.UserStop => DownloadStopReason.UserRequested,
        GameOperationStopIntent.ProcessExit => DownloadStopReason.ApplicationExit,
        _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unhandled stop intent.")
    };

    public Task PerformStop()
    {
        journey.PerformStop();
        return Task.CompletedTask;
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

        if (GameOperationPolicy.Decide(GameOperationPolicy.Operation.Uninstall, currentSnapshot.RuntimeState)
            == GameOperationDecision.RejectedForCurrentState)
        {
            ShowOperationUnavailable();
            return;
        }

        var validation = await journey.ValidateUninstallAsync(currentSnapshot);
        if (validation is null)
        {
            // 预检失败的原因已由 journey 就地报出（ADR-029）：这里只需不再打开确认框。
            return;
        }

        // 勾选每次打开都归零（ADR-030）：破坏性选项不预置。
        IsThoroughUninstallSelected = false;
        // 对话框先弹、尺寸后到（ADR-030 第 4 条）：统计要遍历整个安装目录与 Prefix，
        // 实测 37k 文件 ≈ 3.4 秒；放在 Show 之前会让点击「没反应」那么久。
        // 先显示带「正在统计」的标签，测量回来后再换成带数字的那句。
        ThoroughUninstallOptionText = localizer.T(LocalizationKeys.UninstallThoroughCleanupOptionPending);

        dialogs.UninstallConfirm.Show(localizer.F(
            LocalizationKeys.UninstallConfirmText,
            currentSnapshot.LocalGame.GamePath,
            Math.Max(0, validation.AffectedFileCount - 2)));

        var footprint = await journey.MeasureUninstallFootprintAsync(currentSnapshot);
        if (dialogs.UninstallConfirm.IsVisible)
        {
            // 用户还没关掉/确认：把实测数字换上去（测量与删除同源，显示多少就删多少）。
            ThoroughUninstallOptionText = localizer.F(
                LocalizationKeys.UninstallThoroughCleanupOption,
                FileSizeFormatter.Format(footprint.InstallDirectoryBytes),
                FileSizeFormatter.Format(footprint.PrefixBytes));
        }
    }

    private void ShowOperationUnavailable() =>
        GameOperationRejections.WarnUnavailable(localizer, toastService);

    public async Task ConfirmUninstallAsync()
    {
        if (currentSnapshot is not null)
        {
            // Set uninstall icon before the journey runs so the test sees it
            ProgressIconKind = ResolveProgressPresentation(GameOperationKind.Uninstall).IconKind;
            await journey.ConfirmUninstallAsync(
                currentSnapshot,
                IsThoroughUninstallSelected
                    ? UninstallScope.ThoroughCleanup
                    : UninstallScope.ManifestFilesOnly);
        }
    }

    public async Task ResumePersistedDownloadAsync(CancellationToken cancellationToken)
    {
        if (currentSnapshot is not null)
            await journey.ResumePersistedAsync(currentSnapshot, cancellationToken);
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
        // 并行校验/下载的进度回调由多个线程池线程经 Dispatcher.Post 汇入，到达次序
        // 不保证递增：递增与回调非原子，百分比可瞬时回退（AUD-PERF-006）。同一阶段
        // 内钳制为单调；阶段切换时重置下界——新阶段（含重试轮经 VerificationRetry
        // 折返 FileCheck）合法地从低百分比重新开始。
        var value = Math.Clamp(progress.Progress, 0, 100);
        if (progress.Stage != displayedProgressStage)
        {
            displayedProgressStage = progress.Stage;
            displayedProgressFloor = 0;
        }
        else if (value < displayedProgressFloor)
        {
            value = displayedProgressFloor;
        }

        displayedProgressFloor = value;
        ProgressValue = value;
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
        dialogs.RepairConfirm.Confirmed -= RepairAsync;
        dialogs.UninstallConfirm.Confirmed -= ConfirmUninstallAsync;
        dialogs.StopConfirm.Confirmed -= PerformStop;
    }

}
