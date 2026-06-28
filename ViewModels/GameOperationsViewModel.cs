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

    [ObservableProperty]
    private bool isInstallPanelVisible = true;

    [ObservableProperty]
    private bool isControlPanelVisible;

    [ObservableProperty]
    private bool isProgressPanelVisible;

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

    public Func<LauncherStatusSnapshot?>? GetSnapshot { get; set; }

    public Func<Task>? RequestRefreshAsync { get; set; }

    public Func<Task>? RequestRefreshAfterPersistedResumeAsync { get; set; }

    public Func<LauncherStatusSnapshot, Task>? ApplySnapshotAsync { get; set; }

    public Action? MinimizeWindow { get; set; }

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
        IsProgressPanelVisible = false;
        CanPauseOperation = false;
        IsControlPanelVisible = snapshot?.RuntimeState == LauncherRuntimeState.Ready;
        IsInstallPanelVisible = !IsControlPanelVisible;
    }

    [RelayCommand]
    private async Task StartGameAsync()
    {
        var snapshot = GetSnapshot?.Invoke();
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
                MinimizeWindow?.Invoke();
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
            var snapshot = GetSnapshot?.Invoke();
            if (snapshot is null)
            {
                shell.OperationNote = localizer.T("stateNotLoaded");
                return;
            }

            if (snapshot.RuntimeState == LauncherRuntimeState.Corrupted)
            {
                dialogs.ShowRepairConfirm(localizer.T("repairWarning"));
                return;
            }

            if (snapshot.RuntimeState is LauncherRuntimeState.IoFailure or LauncherRuntimeState.RemoteUnavailable)
            {
                if (RequestRefreshAsync is not null)
                {
                    await RequestRefreshAsync.Invoke();
                }

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
            var refresh = RequestRefreshAfterPersistedResumeAsync ?? RequestRefreshAsync;
            if (refresh is not null)
            {
                await refresh.Invoke();
            }
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
            var currentSnapshot = GetSnapshot?.Invoke();
            if (currentSnapshot is not null && ApplySnapshotAsync is not null)
            {
                await ApplySnapshotAsync.Invoke(currentSnapshot);
            }
        }
    }

    [RelayCommand]
    private async Task RequestRepairAsync()
    {
        var snapshot = GetSnapshot?.Invoke();
        if (snapshot is null)
        {
            shell.OperationNote = localizer.T("stateNotLoaded");
            return;
        }

        if (snapshot.RuntimeState is not (LauncherRuntimeState.Corrupted or LauncherRuntimeState.Ready))
        {
            shell.OperationNote = localizer.T("operationUnavailableForCurrentState");
            return;
        }

        dialogs.ShowRepairConfirm(localizer.T("repairWarning"));
        await Task.CompletedTask;
    }

    public async Task RepairAsync()
    {
        var snapshot = GetSnapshot?.Invoke();
        if (snapshot is null)
        {
            shell.OperationNote = localizer.T("stateNotLoaded");
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
            if (RequestRefreshAsync is not null)
            {
                await RequestRefreshAsync.Invoke();
            }
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
            var currentSnapshot = GetSnapshot?.Invoke();
            if (currentSnapshot is not null && ApplySnapshotAsync is not null)
            {
                await ApplySnapshotAsync.Invoke(currentSnapshot);
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
        var snapshot = GetSnapshot?.Invoke();
        if (snapshot is null)
        {
            shell.OperationNote = localizer.T("stateNotLoaded");
            return;
        }

        if (snapshot.RuntimeState != LauncherRuntimeState.Ready)
        {
            shell.OperationNote = localizer.T("operationUnavailableForCurrentState");
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
        var snapshot = GetSnapshot?.Invoke();
        if (snapshot is null)
        {
            shell.OperationNote = localizer.T("stateNotLoaded");
            return;
        }

        if (snapshot.RuntimeState != LauncherRuntimeState.Ready)
        {
            shell.OperationNote = localizer.T("operationUnavailableForCurrentState");
            return;
        }

        dialogs.IsUninstallConfirmVisible = false;
        shell.IsBusy = true;
        IsProgressPanelVisible = true;
        IsInstallPanelVisible = false;
        IsControlPanelVisible = false;
        ProgressTitle = localizer.T("uninstalling");
        ProgressDetail = localizer.T("deletingManifestFiles");

        try
        {
            var result = await backend.UninstallAsync(snapshot, ApplyProgress);
            shell.OperationNote = result.Message;
            if (RequestRefreshAsync is not null)
            {
                await RequestRefreshAsync.Invoke();
            }
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
        var snapshot = GetSnapshot?.Invoke();
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
            if (RequestRefreshAsync is not null)
            {
                await RequestRefreshAsync.Invoke();
            }
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
        else if (result.ErrorType == "stopped")
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
            shell.OperationNote = localizer.T("stateNotLoaded");
            return false;
        }

        return true;
    }

    private bool PrepareOperation()
    {
        var snapshot = GetSnapshot?.Invoke();
        if (!PrepareShellOnly(snapshot))
        {
            return false;
        }

        shell.IsBusy = true;
        IsProgressPanelVisible = true;
        IsInstallPanelVisible = false;
        IsControlPanelVisible = false;
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
        IsProgressPanelVisible = true;
        IsInstallPanelVisible = false;
        IsControlPanelVisible = false;
        ProgressValue = Math.Clamp(progress.Progress, 0, 100);
        ProgressTitle = ResolveProgressTitle(progress);
        ProgressDetail = progress.Stage switch
        {
            "repair-confirm" => progress.AffectedFileCount > 0
                ? localizer.F(
                    "repairFilesNeeded",
                    progress.AffectedFileCount,
                    FileSizeFormatter.Format(progress.DownloadedSize))
                : localizer.T("repairNoFilesNeeded"),
            "paused" => localizer.T("paused"),
            "repair-check" => localizer.T("repairCheckingFiles"),
            "update-check" => localizer.T("updateCheckingFiles"),
            "check-file" => localizer.T("verifyingDownloadedFiles"),
            "disk-check" => localizer.F(
                "diskSpaceCheck",
                FileSizeFormatter.Format(progress.RequiredDiskBytes),
                progress.AvailableDiskBytes.HasValue
                    ? FileSizeFormatter.Format(progress.AvailableDiskBytes.Value)
                    : "--"),
            "verification-retry" => localizer.F(
                "verificationRetry",
                progress.FailedFileCount,
                progress.RetryAttempt,
                progress.RetryLimit),
            "verification-failed" => localizer.F("verificationFailed", progress.FailedFileCount),
            "repair-done" => localizer.T("repairCompleted"),
            "download-done" => localizer.T("installUpdateCompleted"),
            "stopped" => localizer.T("operationStopped"),
            "download" => localizer.T("downloading"),
            _ => localizer.T("working")
        };
        var clearsDownloadMetrics = progress.Stage is
            "repair-confirm" or "paused" or "disk-check" or "verification-retry" or "verification-failed";
        ProgressSpeed = clearsDownloadMetrics ? "" : progress.Speed;
        ProgressSize = progress.TotalSize > 0 && !clearsDownloadMetrics
            ? $"{FileSizeFormatter.Format(progress.DownloadedSize)} / {FileSizeFormatter.Format(progress.TotalSize)}"
            : "";
        ProgressEstimated = progress.TotalSize > 0 && progress.Stage == "download" && !string.IsNullOrWhiteSpace(progress.Estimated)
            ? localizer.F("estimatedTimeRemaining", progress.Estimated)
            : "";
        IsPaused = progress.IsPaused;
        CanPauseOperation = progress.CanPause;
        PauseResumeText = progress.IsPaused ? localizer.T("resume") : localizer.T("pause");
        PauseResumeIcon = progress.IsPaused ? "Play" : "Pause";
    }

    private string ResolveProgressTitle(GameOperationProgress progress)
    {
        return progress.OperationKind switch
        {
            GameOperationKinds.Repair => localizer.T("repairing"),
            GameOperationKinds.Uninstall => localizer.T("uninstalling"),
            GameOperationKinds.Download => localizer.T("downloading"),
            _ => localizer.T("working")
        };
    }

}
