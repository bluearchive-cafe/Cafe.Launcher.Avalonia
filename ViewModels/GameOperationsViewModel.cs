using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class GameOperationsViewModel : ViewModelBase
{
    private readonly IGameOperationsBackend backend;
    private readonly Func<TimeSpan, Task> delayAsync;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly LocalDiagnostics diagnostics;
    private readonly ShellViewModel shell;
    private readonly DialogsViewModel dialogs;
    private LauncherStatusSnapshot? currentSnapshot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstallPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsControlPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsProgressPanelVisible))]
    private GameOperationPanelMode panelMode = GameOperationPanelMode.Install;

    public bool IsInstallPanelVisible => PanelMode == GameOperationPanelMode.Install;

    public bool IsControlPanelVisible => PanelMode == GameOperationPanelMode.Control;

    public bool IsProgressPanelVisible => PanelMode == GameOperationPanelMode.Progress;

    [ObservableProperty]
    private string installButtonText = "";

    [ObservableProperty]
    private string progressTitle = "";

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

    public event Func<GameOperationsRefreshMode, Task>? RefreshRequested;
    public event Action? MinimizeRequested;

    public GameOperationsViewModel(
        GameLaunchService gameLaunchService,
        GameDownloadService gameDownloadService,
        GameUninstallService gameUninstallService,
        LocalizationService localizer,
        ToastService toastService,
        LocalDiagnostics diagnostics,
        ShellViewModel shell,
        DialogsViewModel dialogs)
        : this(
            new GameOperationsBackend(
                gameLaunchService,
                gameDownloadService,
                gameUninstallService),
            localizer,
            toastService,
            diagnostics,
            shell,
            dialogs,
            Task.Delay)
    {
    }

    internal GameOperationsViewModel(
        IGameOperationsBackend backend,
        LocalizationService localizer,
        ToastService toastService,
        LocalDiagnostics diagnostics,
        ShellViewModel shell,
        DialogsViewModel dialogs,
        Func<TimeSpan, Task> delayAsync)
    {
        this.backend = backend;
        this.delayAsync = delayAsync;
        this.localizer = localizer;
        this.toastService = toastService;
        this.diagnostics = diagnostics;
        this.shell = shell;
        this.dialogs = dialogs;
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
        SetIdlePanels(snapshot);
    }

    public void SetIdlePanels(LauncherStatusSnapshot? snapshot)
    {
        CanPauseOperation = false;
        PanelMode = snapshot?.RuntimeState == LauncherRuntimeState.Ready
            ? GameOperationPanelMode.Control
            : GameOperationPanelMode.Install;
    }

    [RelayCommand]
    private async Task StartGameAsync()
    {
        var snapshot = currentSnapshot;
        if (!PrepareShellOnly(snapshot))
        {
            return;
        }

        shell.IsBusy = true;
        shell.OperationNote = localizer.T("runningLaunchCheck");

        try
        {
            var launchResult = await backend.StartGameAsync(snapshot!);
            shell.SetLaunchCheckResult(launchResult.Validation.Message);
            shell.OperationNote = launchResult.Message;

            if (launchResult.Success)
            {
                toastService.ShowSuccess(localizer.T("gameLaunchedMinimized"));
                await delayAsync(TimeSpan.FromMilliseconds(600));
                MinimizeRequested?.Invoke();
            }
            else
            {
                toastService.ShowWarning(launchResult.Message);
                await diagnostics.MessageAsync("Game launch blocked.", launchResult.Message);
            }
        }
        catch (Exception exception)
        {
            shell.OperationNote = localizer.F("gameLaunchFailed", exception.Message);
            toastService.ShowError(exception.Message);
            await diagnostics.ErrorAsync("Game launch failed.", exception);
        }
        finally
        {
            shell.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallOrUpdateAsync()
    {
        if (!PrepareOperation())
        {
            return;
        }

        try
        {
            var snapshot = currentSnapshot;
            if (snapshot is null)
            {
                shell.OperationNote = localizer.T("launcherStateNotLoaded");
                return;
            }

            if (snapshot.RuntimeState == LauncherRuntimeState.Corrupted)
            {
                dialogs.ShowRepairConfirm(localizer.T("repairWarning"));
                return;
            }

            if (snapshot.RuntimeState is LauncherRuntimeState.IoFailure or LauncherRuntimeState.RemoteUnavailable)
            {
                await RequestRefresh(GameOperationsRefreshMode.Normal);

                return;
            }

            if (snapshot.RuntimeState == LauncherRuntimeState.Ready)
            {
                shell.OperationNote = localizer.T("operationUnavailableForCurrentState");
                return;
            }

            var result = await backend.InstallOrUpdateAsync(snapshot, ApplyProgress);
            shell.OperationNote = result.Message;
            ShowOperationResult(result);
            await RequestRefresh(GameOperationsRefreshMode.SkipPersistedResume);
        }
        catch (Exception exception)
        {
            shell.OperationNote = localizer.F("networkWithMessage", exception.Message);
            toastService.ShowError(exception.Message);
            await diagnostics.ErrorAsync("Game install/update failed.", exception);
        }
        finally
        {
            shell.IsBusy = false;
            if (currentSnapshot is not null)
            {
                ApplySnapshot(currentSnapshot);
            }
        }
    }

    [RelayCommand]
    private async Task RequestRepairAsync()
    {
        var snapshot = currentSnapshot;
        if (snapshot is null)
        {
            shell.OperationNote = localizer.T("launcherStateNotLoaded");
            return;
        }

        if (snapshot.RuntimeState is not (LauncherRuntimeState.Corrupted or LauncherRuntimeState.Ready))
        {
            shell.OperationNote = localizer.T("operationUnavailableForCurrentState");
            toastService.ShowWarning(shell.OperationNote);
            return;
        }

        dialogs.ShowRepairConfirm(localizer.T("repairWarning"));
    }

    public async Task RepairAsync()
    {
        var snapshot = currentSnapshot;
        if (snapshot is null)
        {
            shell.OperationNote = localizer.T("launcherStateNotLoaded");
            return;
        }

        if (snapshot.RuntimeState is not (LauncherRuntimeState.Corrupted or LauncherRuntimeState.Ready))
        {
            shell.OperationNote = localizer.T("operationUnavailableForCurrentState");
            return;
        }

        if (!PrepareOperation())
        {
            return;
        }

        try
        {
            var result = await backend.RepairAsync(snapshot, ApplyProgress);
            shell.OperationNote = result.Message;
            ShowOperationResult(result);
            await RequestRefresh(GameOperationsRefreshMode.Normal);
        }
        catch (Exception exception)
        {
            shell.OperationNote = localizer.F("networkWithMessage", exception.Message);
            toastService.ShowError(exception.Message);
            await diagnostics.ErrorAsync("Game repair failed.", exception);
        }
        finally
        {
            shell.IsBusy = false;
            if (currentSnapshot is not null)
            {
                ApplySnapshot(currentSnapshot);
            }
        }
    }

    [RelayCommand]
    private void StopOperation()
    {
        if (backend.IsDownloadRunning)
        {
            dialogs.ShowStopConfirm();
            return;
        }

        PerformStop();
    }

    public void PerformStop()
    {
        backend.Stop(clearPersistedState: true);
        shell.OperationNote = localizer.T("stopRequested");
        try { toastService.ShowWarning(localizer.T("stopRequested")); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to show stop toast: {ex.Message}"); }
    }

    [RelayCommand]
    private void PauseResume()
    {
        if (!CanPauseOperation)
        {
            return;
        }

        if (backend.IsPaused)
        {
            backend.Resume();
            IsPaused = false;
            PauseResumeText = localizer.T("pause");
            PauseResumeIcon = "Pause";
            ProgressDetail = localizer.T("downloading");
            shell.OperationNote = localizer.T("resumeRequested");
        }
        else
        {
            backend.Pause();
            IsPaused = true;
            PauseResumeText = localizer.T("resume");
            PauseResumeIcon = "Play";
            ProgressDetail = localizer.T("paused");
            ProgressSpeed = "";
            ProgressEstimated = "";
            shell.OperationNote = localizer.T("pauseRequested");
        }
    }

    [RelayCommand]
    private async Task RequestUninstallAsync()
    {
        var snapshot = currentSnapshot;
        if (snapshot is null)
        {
            shell.OperationNote = localizer.T("launcherStateNotLoaded");
            return;
        }

        if (snapshot.RuntimeState != LauncherRuntimeState.Ready)
        {
            shell.OperationNote = localizer.T("operationUnavailableForCurrentState");
            toastService.ShowWarning(shell.OperationNote);
            return;
        }

        var validation = await backend.ValidateUninstallAsync(snapshot.LocalGame.GamePath);
        if (!validation.Success)
        {
            shell.OperationNote = validation.Message;
            return;
        }

        dialogs.ShowUninstallConfirm(localizer.F(
            "uninstallConfirmText",
            snapshot.LocalGame.GamePath,
            Math.Max(0, validation.AffectedFileCount - 2)));
    }

    public async Task ConfirmUninstallAsync()
    {
        var snapshot = currentSnapshot;
        if (snapshot is null)
        {
            shell.OperationNote = localizer.T("launcherStateNotLoaded");
            return;
        }

        if (snapshot.RuntimeState != LauncherRuntimeState.Ready)
        {
            shell.OperationNote = localizer.T("operationUnavailableForCurrentState");
            return;
        }

        dialogs.IsUninstallConfirmVisible = false;
        shell.IsBusy = true;
        PanelMode = GameOperationPanelMode.Progress;
        ProgressTitle = localizer.T("uninstalling");
        ProgressDetail = localizer.T("deletingManifestFiles");

        try
        {
            var result = await backend.UninstallAsync(snapshot, ApplyProgress);
            shell.OperationNote = result.Message;
            await RequestRefresh(GameOperationsRefreshMode.Normal);
        }
        catch (Exception exception)
        {
            shell.OperationNote = localizer.F("networkWithMessage", exception.Message);
            await diagnostics.ErrorAsync("Game uninstall failed.", exception);
        }
        finally
        {
            shell.IsBusy = false;
        }
    }

    public async Task ResumePersistedDownloadAsync(CancellationToken cancellationToken)
    {
        var snapshot = currentSnapshot;
        if (snapshot is null || shell.IsBusy)
        {
            return;
        }

        try
        {
            shell.IsBusy = true;
            var result = await backend.ResumePersistedAsync(snapshot, ApplyProgress, cancellationToken);
            if (result is null)
            {
                return;
            }

            shell.OperationNote = result.Message;
            ShowOperationResult(result);
            await RequestRefresh(GameOperationsRefreshMode.Normal);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            shell.OperationNote = localizer.F("networkWithMessage", exception.Message);
            await diagnostics.ErrorAsync("Persisted game download resume failed.", exception, CancellationToken.None);
        }
        finally
        {
            shell.IsBusy = false;
            CanPauseOperation = false;
        }
    }

    public void StopDownload(bool clearPersistedState)
    {
        backend.Stop(clearPersistedState);
    }

    public bool IsDownloadRunning => backend.IsDownloadRunning;

    private void ShowOperationResult(GameOperationResult result)
    {
        if (result.Success)
        {
            toastService.ShowSuccess(result.Message);
        }
        else if (result.ErrorCode == GameOperationErrorCode.Stopped)
        {
            toastService.ShowWarning(result.Message);
        }
        else
        {
            toastService.ShowError(result.Message);
        }
    }

    private bool PrepareShellOnly(LauncherStatusSnapshot? snapshot)
    {
        if (shell.IsBusy)
        {
            shell.OperationNote = localizer.T("busy");
            return false;
        }

        if (snapshot is null)
        {
            shell.OperationNote = localizer.T("launcherStateNotLoaded");
            return false;
        }

        return true;
    }

    private bool PrepareOperation()
    {
        var snapshot = currentSnapshot;
        if (!PrepareShellOnly(snapshot))
        {
            return false;
        }

        shell.IsBusy = true;
        PanelMode = GameOperationPanelMode.Progress;
        ProgressTitle = localizer.T("preparing");
        ProgressValue = 0;
        ProgressDetail = localizer.T("buildingFileList");
        ProgressSpeed = "";
        ProgressSize = "";
        ProgressEstimated = "";
        IsPaused = false;
        CanPauseOperation = false;
        PauseResumeText = localizer.T("pause");
        PauseResumeIcon = "Pause";
        return true;
    }

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
        ProgressTitle = ResolveProgressTitle(progress);
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

    private async Task RequestRefresh(GameOperationsRefreshMode mode)
    {
        if (RefreshRequested is null)
        {
            return;
        }

        foreach (Func<GameOperationsRefreshMode, Task> subscriber in RefreshRequested.GetInvocationList())
        {
            await subscriber(mode);
        }
    }

    private string ResolveProgressTitle(GameOperationProgress progress)
    {
        return progress.OperationKind switch
        {
            GameOperationKind.Repair => localizer.T("repairing"),
            GameOperationKind.Uninstall => localizer.T("uninstalling"),
            GameOperationKind.Download => localizer.T("downloading"),
            GameOperationKind.Idle => localizer.T("working"),
            _ => throw new ArgumentOutOfRangeException(nameof(progress), progress.OperationKind, null)
        };
    }

}
