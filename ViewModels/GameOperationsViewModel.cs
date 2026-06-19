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
    private readonly GameLaunchService gameLaunchService;
    private readonly GameDownloadService gameDownloadService;
    private readonly GameUninstallService gameUninstallService;
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
    private string installButtonText = "Install Game";

    [ObservableProperty]
    private string progressTitle = "Preparing";

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
    {
        this.gameLaunchService = gameLaunchService;
        this.gameDownloadService = gameDownloadService;
        this.gameUninstallService = gameUninstallService;
        this.localizer = localizer;
        this.toastService = toastService;
        this.diagnostics = diagnostics;
        this.shell = shell;
        this.dialogs = dialogs;
    }

    public void ApplyLanguage()
    {
        PauseResumeText = IsPaused ? localizer.T("resume") : localizer.T("pause");
        if (string.IsNullOrWhiteSpace(ProgressTitle) || ProgressTitle == "Preparing")
        {
            ProgressTitle = localizer.T("preparing");
        }
    }

    public void ApplySnapshot(LauncherStatusSnapshot snapshot)
    {
        InstallButtonText = snapshot.IsInstalled ? localizer.T("updateGame") : localizer.T("installGame");
        SetIdlePanels(snapshot);
    }

    public void SetIdlePanels(LauncherStatusSnapshot? snapshot)
    {
        IsProgressPanelVisible = false;
        CanPauseOperation = false;
        IsControlPanelVisible = snapshot?.IsInstalled == true && snapshot.BelowLowestVersion == false;
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
            var launchResult = await gameLaunchService.StartAsync(snapshot!);
            shell.SetLaunchCheckResult(launchResult.Validation.Message);
            shell.OperationNote = launchResult.Message;

            if (launchResult.Success)
            {
                toastService.ShowSuccess(localizer.T("gameLaunchedMinimized"));
                await Task.Delay(600);
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
            await TryLogErrorAsync("Game launch failed.", exception);
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

            var result = await gameDownloadService.InstallOrUpdateAsync(snapshot, ApplyProgress);
            shell.OperationNote = result.Message;
            if (result.Success)
                toastService.ShowSuccess(result.Message);
            else
                toastService.ShowError(result.Message);
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
            await TryLogErrorAsync("Game install/update failed.", exception);
        }
        finally
        {
            shell.IsBusy = false;
            var snapshot = GetSnapshot?.Invoke();
            if (snapshot is not null && ApplySnapshotAsync is not null)
            {
                await ApplySnapshotAsync.Invoke(snapshot);
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

        dialogs.ShowRepairConfirm(localizer.T("repairWarning"));
        await Task.CompletedTask;
    }

    public async Task RepairAsync()
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

            var result = await gameDownloadService.RepairAsync(snapshot, ApplyProgress);
            shell.OperationNote = result.Message;
            if (result.Success)
                toastService.ShowSuccess(result.Message);
            else
                toastService.ShowError(result.Message);
            if (RequestRefreshAsync is not null)
            {
                await RequestRefreshAsync.Invoke();
            }
        }
        catch (Exception exception)
        {
            shell.OperationNote = localizer.F("networkWithMessage", exception.Message);
            toastService.ShowError(exception.Message);
            await TryLogErrorAsync("Game repair failed.", exception);
        }
        finally
        {
            shell.IsBusy = false;
            var snapshot = GetSnapshot?.Invoke();
            if (snapshot is not null && ApplySnapshotAsync is not null)
            {
                await ApplySnapshotAsync.Invoke(snapshot);
            }
        }
    }

    [RelayCommand]
    private void StopOperation()
    {
        if (gameDownloadService.IsRunning)
        {
            dialogs.ShowStopConfirm();
            return;
        }

        PerformStop();
    }

    public void PerformStop()
    {
        gameDownloadService.Stop();
        shell.OperationNote = localizer.T("stopRequested");
        try { toastService.ShowWarning(localizer.T("stopRequested")); } catch { }
    }

    [RelayCommand]
    private void PauseResume()
    {
        if (!CanPauseOperation)
        {
            return;
        }

        if (gameDownloadService.IsPaused)
        {
            gameDownloadService.Resume();
            IsPaused = false;
            PauseResumeText = localizer.T("pause");
            PauseResumeIcon = "Pause";
            ProgressDetail = localizer.T("downloading");
            shell.OperationNote = localizer.T("resumeRequested");
        }
        else
        {
            gameDownloadService.Pause();
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

        var validation = await gameUninstallService.ValidateAsync(snapshot.LocalGame.GamePath);
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

        dialogs.IsUninstallConfirmVisible = false;
        shell.IsBusy = true;
        IsProgressPanelVisible = true;
        IsInstallPanelVisible = false;
        IsControlPanelVisible = false;
        ProgressTitle = localizer.T("uninstalling");
        ProgressDetail = localizer.T("deletingManifestFiles");

        try
        {
            var result = await gameUninstallService.UninstallAsync(snapshot, ApplyProgress);
            shell.OperationNote = result.Message;
            if (RequestRefreshAsync is not null)
            {
                await RequestRefreshAsync.Invoke();
            }
        }
        catch (Exception exception)
        {
            shell.OperationNote = localizer.F("networkWithMessage", exception.Message);
            await TryLogErrorAsync("Game uninstall failed.", exception);
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
            var result = await gameDownloadService.ResumePersistedAsync(snapshot, ApplyProgress, cancellationToken);
            if (result is null)
            {
                return;
            }

            shell.OperationNote = result.Message;
            if (result.Success)
                toastService.ShowSuccess(result.Message);
            else
                toastService.ShowError(result.Message);
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
            await TryLogErrorAsync("Persisted game download resume failed.", exception);
        }
        finally
        {
            shell.IsBusy = false;
            CanPauseOperation = false;
        }
    }

    public void StopDownload(bool clearPersistedState)
    {
        gameDownloadService.Stop(clearPersistedState);
    }

    public bool IsDownloadRunning => gameDownloadService.IsRunning;

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
                ? $"{progress.AffectedFileCount} files need repair ({FileSizeFormatter.Format(progress.DownloadedSize)})"
                : "No files need repair",
            "paused" => localizer.T("paused"),
            _ => progress.Stage
        };
        ProgressSpeed = progress.Stage == "repair-confirm" || progress.Stage == "paused" ? "" : progress.Speed;
        ProgressSize = progress.TotalSize > 0 && progress.Stage != "repair-confirm" && progress.Stage != "paused"
            ? $"{FileSizeFormatter.Format(progress.DownloadedSize)} / {FileSizeFormatter.Format(progress.TotalSize)}"
            : "";
        ProgressEstimated = progress.TotalSize > 0 && progress.Stage == "download" && !string.IsNullOrWhiteSpace(progress.Estimated)
            ? $"ETA {progress.Estimated}"
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

    private async Task TryLogErrorAsync(string title, Exception exception)
    {
        try
        {
            await diagnostics.ErrorAsync(title, exception);
        }
        catch
        {
            shell.OperationNote = $"{shell.OperationNote} Local diagnostics log write failed.";
        }
    }
}
