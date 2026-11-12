using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.UI.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Helpers;

namespace Cafe.Launcher.UI.Features.GameOperations;

internal partial class GameOperationsViewModel : ObservableObject, IGameOperationJourneyHost, IGameOperationActivity, IDisposable, ILanguageAwarePresentation
{
    /// <summary>
    /// 暂停/恢复按钮的两个图标名。按「按钮此刻提供什么」命名而不是按状态命名：暂停态下按钮
    /// 提供的是恢复，因此显示播放图标——按状态命名会让这个极性在读代码时看不出来。
    /// </summary>
    private const string PauseIcon = "Pause";

    private const string ResumeIcon = "Play";

    private readonly GameOperationJourney journey;
    private readonly IGameOperationExecutor executor;
    private readonly IGameSessionMonitor sessionMonitor;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly DialogsViewModel dialogs;
    private readonly ShellViewModel shell;
    private LauncherStatusSnapshot? currentSnapshot;
    private long runningStateVersion;
    private bool disposed;
    private long uninstallRequestId;
    private long uninstallPresentationVersion;

    /// <summary>卸载扫描期间尚无分母，显示不定进度；删除阶段显示实际百分比。</summary>
    [ObservableProperty]
    private bool isProgressIndeterminate;

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
    private string installButtonIconKind = "Download";

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

    /// <summary>卸载确认、执行与结果共用的特性表面。</summary>
    public UninstallDialogViewModel Uninstall { get; }

    /// <summary>扫描与删除期间禁止关闭窗口或发起正常退出。</summary>
    public bool IsUninstallExecuting => Uninstall.IsExecuting;

    /// <summary>
    /// 底部状态行中的会话项（ADR-035）：启动中/运行中/已退出/未能启动。
    /// 空闲时只隐藏会话项，不影响同一行的校验、网络、下载源与版本。
    /// </summary>
    [ObservableProperty]
    private string gameSessionStateText = "";

    [ObservableProperty]
    private bool isGameSessionVisible;

    [ObservableProperty]
    private bool canPauseOperation;

    [ObservableProperty]
    private string pauseResumeText = "";

    [ObservableProperty]
    private string pauseResumeIcon = PauseIcon;

    /// <summary>Raised when shell state must be refreshed after an operation (driven by the journey host).</summary>
    public event Func<GameOperationsRefreshMode, Task>? RefreshRequested;

    /// <summary>Raised when a failure action should open the log viewer (driven by the journey host).</summary>
    public event Func<Task>? OpenLogViewerRequested;

    /// <summary>Raised when a successful launch should minimize the launcher (driven by the journey host).</summary>
    public event Action? MinimizeRequested;

    /// <summary>Raised when a successful launch should exit the launcher (driven by the journey host).</summary>
    public event Action? ExitRequested;

    /// <summary>
    /// Raised when the window must come back on screen: the session monitor classified the
    /// launch as failed and the news has to reach the user even from the tray (ADR-035).
    /// </summary>
    public event Action? ShowRequested;

    bool IGameOperationJourneyHost.IsBusy => journey.IsShuttingDown || shell.IsBusy || IsTaskExecuting || IsInstallPathBusy;

    LauncherStatusSnapshot? IGameOperationJourneyHost.CurrentSnapshot => currentSnapshot;

    internal GameOperationsViewModel(
        IGameOperationExecutor executor,
        IGameShortcutService gameShortcutService,
        IGameSessionMonitor sessionMonitor,
        LocalizationService localizer,
        ToastService toastService,
        ILauncherDiagnostics diagnostics,
        ShellViewModel shell,
        DialogsViewModel dialogs,
        IErrorHandlingService errorHandling,
        Func<TimeSpan, Task>? delayAsync = null)
    {
        this.executor = executor;
        this.localizer = localizer;
        this.toastService = toastService;
        this.dialogs = dialogs;
        this.shell = shell;
        shell.PropertyChanged += OnShellTaskStateChanged;
        this.sessionMonitor = sessionMonitor;
        InstallButtonText = localizer.T(LocalizationKeys.InstallGame);
        journey = new GameOperationJourney(
            executor,
            gameShortcutService,
            sessionMonitor,
            localizer,
            toastService,
            diagnostics,
            errorHandling,
            delayAsync ?? Task.Delay,
            this);
        journey.IsRunningChanged += OnInstallationIsRunningChanged;
        sessionMonitor.StateChanged += OnGameSessionStateChanged;
        dialogs.RepairConfirm.Confirmed += RepairAsync;
        Uninstall = new UninstallDialogViewModel(localizer, ConfirmUninstallAsync,
            () => AsyncEvent.InvokeSequentiallyAsync(OpenLogViewerRequested));
        Uninstall.PropertyChanged += OnUninstallPresentationChanged;
        dialogs.StopConfirm.Confirmed += PerformStop;
    }

    public void RefreshLocalizedText()
    {
        ApplyPausePresentation();
        if (taskProgress is { } current)
        {
            ProgressTitle = ResolveProgressPresentation(current.OperationKind).Title;
            ProgressDetail = IsTaskStopping ? localizer.T(LocalizationKeys.TaskStopping)
                : IsPaused ? localizer.T(LocalizationKeys.Paused) : FormatProgressDetail(current);
        }
        RefreshTaskPresentation();
        Uninstall.RefreshLocalizedText();
        if (sessionMonitor.State is not GameSessionState.Idle)
        {
            // 语言切换时状态行跟着换词；只重排文案，不重放 StartFailed 的恢复与报错
            // ——那是状态变化时刻的一次性动作。
            ApplySessionStatePresentation();
        }

        if (string.IsNullOrWhiteSpace(ProgressTitle))
        {
            ProgressTitle = localizer.T(LocalizationKeys.Preparing);
        }
    }

    /// <summary>Gets the actual installation path used by folder, repair, and uninstall commands.</summary>
    public string GameManagementPath => currentSnapshot?.LocalGame.GamePath ?? "";

    public void ApplySnapshot(LauncherStatusSnapshot snapshot)
    {
        currentSnapshot = snapshot;
        OnPropertyChanged(nameof(GameManagementPath));
        InstallButtonText = snapshot.RuntimeState switch
        {
            LauncherRuntimeState.NotInstalled => localizer.T(LocalizationKeys.InstallGame),
            LauncherRuntimeState.Corrupted => localizer.T(LocalizationKeys.Repair),
            LauncherRuntimeState.IoFailure or LauncherRuntimeState.RemoteUnavailable => localizer.T(LocalizationKeys.Refresh),
            _ => localizer.T(LocalizationKeys.UpdateGame)
        };
        InstallButtonIconKind = snapshot.RuntimeState switch
        {
            LauncherRuntimeState.NotInstalled => "Download",
            LauncherRuntimeState.Corrupted => "Tools",
            LauncherRuntimeState.IoFailure or LauncherRuntimeState.RemoteUnavailable => "Refresh",
            _ => "Update"
        };
        InstallButtonToolTip = shell.IsInstallBlockedByDiskSpace
            ? shell.InstallDiskSpaceMessage
            : InstallButtonText;
        InstallOrUpdateCommand.NotifyCanExecuteChanged();
        SetIdlePanels(snapshot);
        RefreshTaskPresentation();
    }

    public void SetIdlePanels(LauncherStatusSnapshot? snapshot)
    {
        CanPauseOperation = false;
        PanelMode = snapshot?.RuntimeState == LauncherRuntimeState.Ready
            ? GameOperationPanelMode.Control
            : GameOperationPanelMode.Install;
    }

    private void PrepareOperation(GameOperationPresentation.Preparing preparing)
    {
        shell.IsBusy = true;
        if (IsUninstallExecuting)
        {
            return;
        }
        BeginTask(preparing.Kind, preparing.Automatic);
        PanelMode = GameOperationPanelMode.Progress;
        displayedProgressStage = GameOperationStage.Idle;
        displayedProgressFloor = 0;
        ProgressTitle = localizer.T(LocalizationKeys.Preparing);
        ProgressIconKind = ResolveProgressPresentation(GameOperationKind.Idle).IconKind;
        ProgressValue = 0;
        IsProgressIndeterminate = true;
        ProgressDetail = localizer.T(LocalizationKeys.BuildingFileList);
        ProgressSpeed = "";
        ProgressSize = "";
        ProgressEstimated = "";
        IsPaused = false;
        CanPauseOperation = false;
        ApplyPausePresentation();
        RefreshTaskPresentation();
    }

    /// <summary>
    /// 暂停/恢复这一对呈现（按钮文案与图标）由 <see cref="IsPaused"/> 一处推导。
    /// </summary>
    /// <remarks>
    /// 此前这个三元表达式在语言切换、准备复位、命令切换与进度回调四处各写一遍，图标名
    /// 「Pause」「Play」也散在各处——改一次文案要在四个地方对齐。调用方只需先落 <see cref="IsPaused"/>。
    /// </remarks>
    private void ApplyPausePresentation()
    {
        PauseResumeText = IsPaused ? localizer.T(LocalizationKeys.Resume) : localizer.T(LocalizationKeys.Pause);
        PauseResumeIcon = IsPaused ? ResumeIcon : PauseIcon;
    }

    void IGameOperationJourneyHost.Present(GameOperationPresentation update)
    {
        if (disposed) return;
        if (!Dispatcher.UIThread.CheckAccess() && Application.Current is not null)
        {
            Dispatcher.UIThread.Post(() => ((IGameOperationJourneyHost)this).Present(update));
            return;
        }
        switch (update)
        {
            case GameOperationPresentation.Busy busy: shell.IsBusy = busy.Value; break;
            case GameOperationPresentation.Preparing preparing: PrepareOperation(preparing); break;
            case GameOperationPresentation.Completed completed: CompleteTask(completed.Value); break;
            case GameOperationPresentation.Progress progress when IsTaskExecuting || IsUninstallExecuting: ApplyProgress(progress.Value); break;
            case GameOperationPresentation.Snapshot snapshot: ApplySnapshot(snapshot.Value); break;
            case GameOperationPresentation.LaunchCheck check: shell.SetLaunchCheckResult(check.Message); break;
            case GameOperationPresentation.RepairConfirmation confirmation: dialogs.RepairConfirm.Show(confirmation.Message); break;
            case GameOperationPresentation.UninstallConfirmation confirmation:
                uninstallRequestId = confirmation.RequestId;
                uninstallPresentationVersion = Uninstall.Open(confirmation.Value);
                break;
            case GameOperationPresentation.UninstallSize size when size.RequestId == uninstallRequestId:
                Uninstall.ApplyFootprint(uninstallPresentationVersion, size.Value);
                break;
            case GameOperationPresentation.UninstallExecuting:
                taskResult = null;
                IsTaskDialogVisible = false;
                RefreshTaskPresentation();
                Uninstall.BeginExecution();
                break;
            case GameOperationPresentation.UninstallCompleted completed: Uninstall.Complete(completed.Value); break;
            case GameOperationPresentation.Window { Action: GameOperationWindowAction.Minimize }: MinimizeRequested?.Invoke(); break;
            case GameOperationPresentation.Window { Action: GameOperationWindowAction.Exit }: ExitRequested?.Invoke(); break;
            case GameOperationPresentation.Window { Action: GameOperationWindowAction.Show }: ShowRequested?.Invoke(); break;
        }
    }

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
        {
            taskCreateDesktopShortcut = false;
            await journey.InstallOrUpdateAsync(currentSnapshot);
        }
    }

    [RelayCommand]
    private async Task RequestRepairAsync()
    {
        if (currentSnapshot is not null)
        {
            // 闸门与确认框都在旅程里：请求时与确认后两道闸门同址，展示层不再各写一遍
            // 判定与渲染（ADR-027）。
            await journey.RequestRepairAsync(currentSnapshot);
        }
    }

    public async Task RepairAsync()
    {
        // 确认框点过之后状态可能又变了：那道闸门现在与请求时的同址（GameOperationJourney），
        // 展示层不再各写一遍，也不再各自记住要把它报出来。
        if (currentSnapshot is not null)
        {
            await journey.RepairAsync(currentSnapshot);
        }
    }

    [RelayCommand]
    public void RequestStop()
    {
        if (IsTaskExecuting && !IsTaskStopping)
        {
            dialogs.ShowStopConfirm();
            return;
        }

    }

    /// <summary>
    /// 按意图停止活动工作流。检查点去留是域内策略：调用方只说为什么停，
    /// 由 <see cref="ToStopReason"/> 翻译成下载模块的停止原因。
    /// </summary>
    public void StopOperation(GameOperationStopIntent intent) => journey.Stop(ToStopReason(intent));

    /// <summary>拒绝新的游戏操作并等待下载、修复、续传和 Toast 重试的完整收尾。</summary>
    public Task PrepareForShutdownAsync() => journey.PrepareForShutdownAsync();

    private static DownloadStopReason ToStopReason(GameOperationStopIntent intent) => intent switch
    {
        GameOperationStopIntent.UserStop => DownloadStopReason.UserRequested,
        GameOperationStopIntent.ProcessExit => DownloadStopReason.ApplicationExit,
        _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unhandled stop intent.")
    };

    public Task PerformStop()
    {
        if (IsTaskExecuting)
        {
            IsTaskStopping = true;
            CanPauseOperation = false;
            ProgressDetail = localizer.T(LocalizationKeys.TaskStopping);
        }
        journey.PerformStop();
        return Task.CompletedTask;
    }

    [RelayCommand]
    public void PauseResume()
    {
        if (!CanPauseOperation || IsTaskStopping)
        {
            return;
        }

        if (journey.IsPaused)
        {
            ResetTaskPauseMeasurement();
            journey.Resume();
            IsPaused = false;
            ApplyPausePresentation();
            ProgressDetail = localizer.T(LocalizationKeys.Downloading);
        }
        else
        {
            journey.Pause();
            ResetTaskPauseMeasurement();
            IsPaused = true;
            ApplyPausePresentation();
            ProgressDetail = localizer.T(LocalizationKeys.Paused);
            ProgressSpeed = "";
            ProgressEstimated = "";
        }
        RefreshTaskPresentation();
    }

    [RelayCommand]
    private async Task RequestUninstallAsync()
    {
        if (journey.IsShuttingDown || currentSnapshot is null || shell.IsBusy || Uninstall.IsVisible)
        {
            return;
        }

        await journey.RequestUninstallAsync(currentSnapshot);
    }

    public async Task ConfirmUninstallAsync()
    {
        if (journey.IsShuttingDown || !Uninstall.IsConfirmation || !Uninstall.IsVisible
            || Uninstall.Snapshot is not { } snapshot) return;
        var scope = Uninstall.IsCompatibilitySelected
            ? UninstallScope.GameDirectoryAndManagedCompatibility : UninstallScope.GameDirectory;
        await journey.ConfirmUninstallAsync(snapshot, scope);
    }

    private void OnUninstallPresentationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UninstallDialogViewModel.IsExecuting))
        {
            OnPropertyChanged(nameof(IsUninstallExecuting));
            // Refresh can clear shell busy before uninstall reaches its terminal state;
            // that later transition must also invalidate the derived action bindings.
            RefreshTaskPresentation();
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
        if (disposed || journey.IsShuttingDown)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            if (Application.Current is null)
            {
                ApplyProgressCore(progress);
                return;
            }

            var version = taskVersion;
            var wasExecuting = IsTaskExecuting;
            Dispatcher.UIThread.Post(() =>
            {
                if (version == taskVersion && (!wasExecuting || IsTaskExecuting || IsUninstallExecuting)) ApplyProgress(progress);
            });
            return;
        }

        ApplyProgressCore(progress);
    }

    private void ApplyProgressCore(GameOperationProgress progress)
    {
        if (progress.OperationKind == GameOperationKind.Uninstall)
        {
            Uninstall.ApplyProgress(progress);
            return;
        }
        if (!IsTaskExecuting && HasTaskResult) return;
        if (!IsTaskExecuting) BeginTask(progress.OperationKind, automatic: true);
        if (IsTaskStopping) return;
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
        IsProgressIndeterminate = progress.Progress < 0 || (progress.IsCheckingDownload && !progress.IsPaused)
            || progress.Stage is GameOperationStage.Idle or GameOperationStage.DiskCheck or GameOperationStage.ApplyingFiles
                or GameOperationStage.DownloadCompleted or GameOperationStage.RepairCompleted;
        var progressPresentation = ResolveProgressPresentation(progress.OperationKind);
        ProgressTitle = progressPresentation.Title;
        ProgressIconKind = progressPresentation.IconKind;
        ProgressDetail = FormatProgressDetail(progress);
        var clearsDownloadMetrics = progress.Stage is
            GameOperationStage.RepairConfirmation or GameOperationStage.Paused
            or GameOperationStage.DiskCheck or GameOperationStage.VerificationRetry
            or GameOperationStage.VerificationFailed;
        ProgressSpeed = clearsDownloadMetrics || progress.BytesPerSecond <= 0
            ? ""
            : $"{FileSizeFormatter.Format(progress.BytesPerSecond)}/S";
        ProgressSize = progress.TotalSize > 0 && (!clearsDownloadMetrics || progress.IsPaused || progress.Stage == GameOperationStage.Paused)
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
        ApplyPausePresentation();
        ApplyFileActivity(progress);
        RefreshTaskProgressPresentation();
    }

    private string FormatProgressDetail(GameOperationProgress progress) => progress.Stage switch
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
            GameOperationStage.ApplyingFiles => localizer.T(LocalizationKeys.TaskFinishing),
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
            GameOperationStage.Downloading => localizer.T(progress.IsCheckingDownload
                ? LocalizationKeys.VerifyingDownloadedFiles : LocalizationKeys.Downloading),
            GameOperationStage.UninstallScanning => localizer.T(LocalizationKeys.UninstallScanning),
            GameOperationStage.UninstallCleanup => localizer.T(LocalizationKeys.UninstallCleanupDescription),
            GameOperationStage.Uninstalling => progress.TotalEntryCount > 0
                ? localizer.F(LocalizationKeys.UninstallProgress, progress.ProcessedEntryCount, progress.TotalEntryCount)
                : localizer.T(LocalizationKeys.Uninstalling),
            GameOperationStage.Idle => localizer.T(LocalizationKeys.Working),
            _ => throw new ArgumentOutOfRangeException(nameof(progress), progress.Stage, null)
        };

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

    private void OnGameSessionStateChanged()
    {
        if (disposed)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess() && Application.Current is not null)
        {
            Dispatcher.UIThread.Post(OnGameSessionStateChanged);
            return;
        }

        ApplySessionStatePresentation();
        if (sessionMonitor.State == GameSessionState.StartFailed)
        {
            // 规则在旅程里（ADR-035）：先恢复窗口再报错，报错文案与状态行同源。
            journey.NotifySessionStartFailed(sessionMonitor.LastSessionExit);
        }
    }

    /// <summary>
    /// 状态行只描述，不表态：<see cref="GameSessionState.StartFailed"/> 的恢复窗口与
    /// 错误 Toast 是状态变化时刻的一次性动作，语言切换重排文案时不得重放。
    /// </summary>
    private void ApplySessionStatePresentation()
    {
        var exit = sessionMonitor.LastSessionExit;
        GameSessionStateText = sessionMonitor.State switch
        {
            GameSessionState.Starting => localizer.T(LocalizationKeys.GameSessionStarting),
            GameSessionState.Running => localizer.T(LocalizationKeys.GameSessionRunning),
            GameSessionState.StartFailed => localizer.F(LocalizationKeys.GameSessionStartFailed, exit?.ExitCode ?? -1),
            // 轮询判定的退出没有可归属的退出码（宿主的退出码属于宿主），不硬凑数字。
            GameSessionState.Exited => exit is null
                ? localizer.T(LocalizationKeys.GameSessionExitedNoCode)
                : localizer.F(LocalizationKeys.GameSessionExited, exit.ExitCode),
            _ => ""
        };
        IsGameSessionVisible = sessionMonitor.State is not GameSessionState.Idle;
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
        taskActivityTimer?.Stop();
        if (taskActivityTimer is not null) taskActivityTimer.Tick -= OnTaskActivityTick;
        installSizeCancellation?.Cancel();
        shell.PropertyChanged -= OnShellTaskStateChanged;
        journey.IsRunningChanged -= OnInstallationIsRunningChanged;
        sessionMonitor.StateChanged -= OnGameSessionStateChanged;
        dialogs.RepairConfirm.Confirmed -= RepairAsync;
        Uninstall.PropertyChanged -= OnUninstallPresentationChanged;
        dialogs.StopConfirm.Confirmed -= PerformStop;
        journey.Dispose();
    }

}
