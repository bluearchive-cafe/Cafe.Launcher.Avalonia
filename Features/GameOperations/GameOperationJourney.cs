using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// Owns the journey rules for launch, install, repair, and uninstall:
/// state validation, confirmations, retry, refresh fan-out, and notifications.
/// The presentation module (GameOperationsViewModel) stays pure — state binding,
/// progress mapping, and thin command delegation.
/// </summary>
internal sealed class GameOperationJourney : IGameOperationJourney
{
    public event Func<GameOperationsRefreshMode, Task>? RefreshRequested;
    public event Func<Task>? OpenLogViewerRequested;
    public event Action? MinimizeRequested;

    private readonly IGameLaunchWorkflow launchWorkflow;
    private readonly IGameInstallationWorkflow installationWorkflow;
    private readonly IGameUninstallWorkflow uninstallWorkflow;
    private readonly Func<TimeSpan, Task> delayAsync;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly LocalDiagnostics diagnostics;
    private readonly ShellViewModel shell;
    private readonly DialogsViewModel dialogs;
    private readonly IErrorHandlingService errorHandling;
    private readonly IGameOperationJourneyHost host;

    private LauncherStatusSnapshot? lastInstallSnapshot;

    public GameOperationJourney(
        IGameLaunchWorkflow launchWorkflow,
        IGameInstallationWorkflow installationWorkflow,
        IGameUninstallWorkflow uninstallWorkflow,
        LocalizationService localizer,
        ToastService toastService,
        LocalDiagnostics diagnostics,
        ShellViewModel shell,
        DialogsViewModel dialogs,
        IErrorHandlingService errorHandling,
        Func<TimeSpan, Task> delayAsync,
        IGameOperationJourneyHost host)
    {
        this.launchWorkflow = launchWorkflow;
        this.installationWorkflow = installationWorkflow;
        this.uninstallWorkflow = uninstallWorkflow;
        this.delayAsync = delayAsync;
        this.localizer = localizer;
        this.toastService = toastService;
        this.diagnostics = diagnostics;
        this.shell = shell;
        this.dialogs = dialogs;
        this.errorHandling = errorHandling;
        this.host = host;
    }

    public bool IsDownloadRunning => installationWorkflow.IsRunning;

    public bool IsPaused => installationWorkflow.IsPaused;

    public async Task StartGameAsync(LauncherStatusSnapshot snapshot)
    {
        if (!PrepareShellOnly(snapshot))
        {
            return;
        }

        host.SetBusy(true);
        host.SetOperationNote(localizer.T("runningLaunchCheck"));

        try
        {
            var launchResult = await launchWorkflow.StartGameAsync(snapshot);
            shell.SetLaunchCheckResult(launchResult.Validation.Message);
            host.SetOperationNote(launchResult.Message);

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
            await errorHandling.HandleErrorAsync("Game launch failed.", exception,
                new ErrorHandlingOptions { OperationNoteKey = "gameLaunchFailed" });
        }
        finally
        {
            host.SetBusy(false);
        }
    }

    public async Task InstallOrUpdateAsync(LauncherStatusSnapshot snapshot)
    {
        lastInstallSnapshot = snapshot;
        var result = await RunInstallOrUpdateAttemptAsync(snapshot);
        if (result is null)
        {
            return;
        }

        if (result.Success || result.ErrorCode == GameOperationErrorCode.Stopped)
        {
            ShowOperationResult(result);
            return;
        }

        ShowInstallUpdateFailureToast(result.Message, result.ErrorCode);
    }

    public async Task RequestRepairAsync(LauncherStatusSnapshot snapshot)
    {
        if (snapshot.RuntimeState is not (LauncherRuntimeState.Corrupted or LauncherRuntimeState.Ready))
        {
            host.SetOperationNote(localizer.T("operationUnavailableForCurrentState"));
            toastService.ShowWarning(localizer.T("operationUnavailableForCurrentState"));
            return;
        }

        dialogs.ShowRepairConfirm(localizer.T("repairWarning"));
    }

    public async Task RepairAsync(LauncherStatusSnapshot snapshot)
    {
        if (!PrepareOperation(snapshot))
        {
            return;
        }

        try
        {
            var result = await installationWorkflow.RepairAsync(snapshot, host.ApplyProgress);
            host.SetOperationNote(result.Message);
            ShowOperationResult(result);
            await RequestRefresh(GameOperationsRefreshMode.Normal);
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Game repair failed.", exception,
                new ErrorHandlingOptions { OperationNoteKey = "networkWithMessage" });
        }
        finally
        {
            host.SetBusy(false);
            ApplySnapshotSafe(snapshot);
        }
    }

    public async Task RequestUninstallAsync(LauncherStatusSnapshot snapshot)
    {
        if (snapshot.RuntimeState != LauncherRuntimeState.Ready)
        {
            host.SetOperationNote(localizer.T("operationUnavailableForCurrentState"));
            toastService.ShowWarning(localizer.T("operationUnavailableForCurrentState"));
            return;
        }

        var validation = await uninstallWorkflow.ValidateUninstallAsync(snapshot.LocalGame.GamePath);
        if (!validation.Success)
        {
            host.SetOperationNote(validation.Message);
            return;
        }

        dialogs.ShowUninstallConfirm(localizer.F(
            "uninstallConfirmText",
            snapshot.LocalGame.GamePath,
            Math.Max(0, validation.AffectedFileCount - 2)));
    }

    public async Task ConfirmUninstallAsync(LauncherStatusSnapshot snapshot)
    {
        if (snapshot.RuntimeState != LauncherRuntimeState.Ready)
        {
            host.SetOperationNote(localizer.T("operationUnavailableForCurrentState"));
            return;
        }

        dialogs.IsUninstallConfirmVisible = false;
        host.SetBusy(true);

        try
        {
            // Prepare for uninstall — the first progress update from the workflow
            // will set the correct icon. Call PrepareOperation to reset panel state.
            host.PrepareOperation();
            var result = await uninstallWorkflow.UninstallAsync(snapshot, host.ApplyProgress);
            host.SetOperationNote(result.Message);
            await RequestRefresh(GameOperationsRefreshMode.Normal);
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Game uninstall failed.", exception,
                new ErrorHandlingOptions { ShowToast = false, OperationNoteKey = "networkWithMessage" });
        }
        finally
        {
            host.SetBusy(false);
        }
    }

    public void RequestStop()
    {
        if (installationWorkflow.IsRunning)
        {
            dialogs.ShowStopConfirm();
            return;
        }

        PerformStop();
    }

    public void PerformStop()
    {
        installationWorkflow.Stop(clearPersistedState: true);
        host.SetOperationNote(localizer.T("stopRequested"));
        try { toastService.ShowWarning(localizer.T("stopRequested")); }
        catch (Exception ex)
        {
            LocalDiagnostics.LogSync(
                LogEntrySeverity.Warn,
                "StopToastFailed",
                $"Failed to show stop toast: {ex.Message}");
        }
    }

    public async Task ResumePersistedAsync(LauncherStatusSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (host.IsBusy)
        {
            return;
        }

        try
        {
            host.SetBusy(true);
            var result = await installationWorkflow.ResumePersistedAsync(
                snapshot,
                host.ApplyProgress,
                cancellationToken);
            if (result is null)
            {
                return;
            }

            host.SetOperationNote(result.Message);
            ShowOperationResult(result);
            await RequestRefresh(GameOperationsRefreshMode.Normal);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Persisted game download resume failed.", exception,
                new ErrorHandlingOptions { ShowToast = false, OperationNoteKey = "networkWithMessage" });
        }
        finally
        {
            host.SetBusy(false);
        }
    }

    public void Stop(bool clearPersistedState)
    {
        installationWorkflow.Stop(clearPersistedState);
    }

    public void Pause()
    {
        installationWorkflow.Pause();
    }

    public void Resume()
    {
        installationWorkflow.Resume();
    }

    private async Task<GameOperationResult?> RunInstallOrUpdateAttemptAsync(LauncherStatusSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (!PrepareOperation(snapshot))
        {
            return null;
        }

        try
        {
            if (snapshot.RuntimeState == LauncherRuntimeState.Corrupted)
            {
                dialogs.ShowRepairConfirm(localizer.T("repairWarning"));
                return null;
            }

            if (snapshot.RuntimeState is LauncherRuntimeState.IoFailure or LauncherRuntimeState.RemoteUnavailable)
            {
                await RequestRefresh(GameOperationsRefreshMode.Normal);
                return null;
            }

            if (snapshot.RuntimeState == LauncherRuntimeState.Ready)
            {
                host.SetOperationNote(localizer.T("operationUnavailableForCurrentState"));
                return null;
            }

            var result = await installationWorkflow.InstallOrUpdateAsync(snapshot, host.ApplyProgress, cancellationToken);
            host.SetOperationNote(result.Message);
            await RequestRefresh(GameOperationsRefreshMode.SkipPersistedResume);
            return result;
        }
        catch (Exception exception)
        {
            var key = exception is IOException or UnauthorizedAccessException
                ? "fileOperationFailed"
                : "networkWithMessage";
            await errorHandling.HandleErrorAsync("Game install/update failed.", exception,
                new ErrorHandlingOptions { OperationNoteKey = key });
            return new GameOperationResult
            {
                Success = false,
                Message = localizer.T("launcherStateNotLoaded"),
                ErrorCode = GameOperationErrorCode.System
            };
        }
        finally
        {
            host.SetBusy(false);
            ApplySnapshotSafe(snapshot);
        }
    }

    private void ShowInstallUpdateFailureToast(string message, GameOperationErrorCode errorCode)
    {
        var isTerminal = errorCode is
            GameOperationErrorCode.PathMissing or
            GameOperationErrorCode.CdnConfiguration or
            GameOperationErrorCode.RemoteConfiguration or
            GameOperationErrorCode.GameRunning or
            GameOperationErrorCode.InsufficientDiskSpace or
            GameOperationErrorCode.InvalidState;

        if (isTerminal)
        {
            toastService.ShowError(message);
            return;
        }

        toastService.Show(new ToastOptions
        {
            Title = localizer.T("installUpdateFailedTitle"),
            Message = message,
            Severity = ToastSeverity.Error,
            PrimaryAction = new ToastAction(localizer.T("retry"), RetryInstallOrUpdateAsync, Timeout: null),
            SecondaryAction = new ToastAction(localizer.T("viewLog"), OpenLogViewerAsync)
        });
    }

    private async Task<ToastActionResult> RetryInstallOrUpdateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = lastInstallSnapshot;
        if (snapshot is null)
        {
            return ToastActionResult.Failure(localizer.T("launcherStateNotLoaded"), localizer.T("installUpdateFailedTitle"));
        }

        var result = await RunInstallOrUpdateAttemptAsync(snapshot, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result?.Success == true)
        {
            ShowOperationResult(result);
            return ToastActionResult.Success();
        }

        var message = result?.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = result?.ErrorCode == GameOperationErrorCode.Stopped
                ? localizer.T("operationStopped")
                : localizer.T("operationUnavailableForCurrentState");
        }

        return ToastActionResult.Failure(message, localizer.T("installUpdateFailedTitle"));
    }

    private async Task<ToastActionResult> OpenLogViewerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await AsyncEvent.InvokeSequentiallyAsync(OpenLogViewerRequested);
        return ToastActionResult.Success();
    }

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

    private bool PrepareShellOnly(LauncherStatusSnapshot? snapshot)
    {
        if (host.IsBusy)
        {
            host.SetOperationNote(localizer.T("busy"));
            return false;
        }

        if (snapshot is null)
        {
            host.SetOperationNote(localizer.T("launcherStateNotLoaded"));
            return false;
        }

        return true;
    }

    private bool PrepareOperation(LauncherStatusSnapshot snapshot)
    {
        if (!PrepareShellOnly(snapshot))
        {
            return false;
        }

        host.PrepareOperation();
        return true;
    }

    private void ApplySnapshotSafe(LauncherStatusSnapshot snapshot)
    {
        host.ApplySnapshot(snapshot);
    }
}
