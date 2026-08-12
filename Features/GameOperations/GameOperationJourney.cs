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
    /// <summary>Raised after an operation needs the shell state refreshed.</summary>
    public event Func<GameOperationsRefreshMode, Task>? RefreshRequested;

    /// <summary>Raised when an operation failure asks the shell to open its log viewer.</summary>
    public event Func<Task>? OpenLogViewerRequested;

    /// <summary>Raised after a successful game launch requests window minimization.</summary>
    public event Action? MinimizeRequested;

    /// <summary>Forwards installation running-state changes to the presentation host.</summary>
    public event Action? IsRunningChanged
    {
        add => installationWorkflow.IsRunningChanged += value;
        remove => installationWorkflow.IsRunningChanged -= value;
    }

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

    /// <summary>Initializes the workflow collaborators and presentation host for game operations.</summary>
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

    /// <summary>Gets whether a download or repair workflow is currently running.</summary>
    public bool IsDownloadRunning => installationWorkflow.IsRunning;

    /// <summary>Gets whether the active download workflow is paused.</summary>
    public bool IsPaused => installationWorkflow.IsPaused;

    /// <summary>Starts the game after validating the supplied launcher state.</summary>
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
                await diagnostics.MessageAsync("GameLaunch", launchResult.Message);
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

    /// <summary>Installs or updates the game and presents the terminal result.</summary>
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

    /// <summary>Validates whether repair can be requested and opens its confirmation dialog.</summary>
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

    /// <summary>Runs a confirmed repair and refreshes launcher state afterward.</summary>
    public async Task RepairAsync(LauncherStatusSnapshot snapshot)
    {
        if (!PrepareOperation(snapshot))
        {
            return;
        }

        var refreshHandled = false;
        try
        {
            var result = await installationWorkflow.RepairAsync(snapshot, host.ApplyProgress);
            host.SetOperationNote(result.Message);
            ShowOperationResult(result);
            refreshHandled = await RequestRefresh(GameOperationsRefreshMode.Normal);
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Game repair failed.", exception,
                new ErrorHandlingOptions { OperationNoteKey = "networkWithMessage" });
        }
        finally
        {
            host.SetBusy(false);
            if (!refreshHandled)
            {
                ApplySnapshotSafe(snapshot);
            }
        }
    }

    /// <summary>Validates uninstall eligibility and opens its confirmation dialog.</summary>
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

    /// <summary>Runs a confirmed uninstall and refreshes launcher state afterward.</summary>
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

    /// <summary>Requests a stop confirmation when work is active, or stops immediately otherwise.</summary>
    public void RequestStop()
    {
        if (installationWorkflow.IsRunning)
        {
            dialogs.ShowStopConfirm();
            return;
        }

        PerformStop();
    }

    /// <summary>Executes the stop after the confirmation flow has completed.</summary>
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

    /// <summary>Attempts to continue a persisted download while respecting cancellation.</summary>
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

    /// <summary>Stops the active workflow, optionally clearing its persisted checkpoint.</summary>
    public void Stop(bool clearPersistedState)
    {
        installationWorkflow.Stop(clearPersistedState);
    }

    /// <summary>Pauses the active download workflow.</summary>
    public void Pause()
    {
        installationWorkflow.Pause();
    }

    /// <summary>Resumes the active download workflow.</summary>
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

        var refreshHandled = false;
        try
        {
            if (snapshot.RuntimeState == LauncherRuntimeState.Corrupted)
            {
                dialogs.ShowRepairConfirm(localizer.T("repairWarning"));
                return null;
            }

            if (snapshot.RuntimeState is LauncherRuntimeState.IoFailure or LauncherRuntimeState.RemoteUnavailable)
            {
                refreshHandled = await RequestRefresh(GameOperationsRefreshMode.Normal);
                return null;
            }

            if (snapshot.RuntimeState == LauncherRuntimeState.Ready)
            {
                host.SetOperationNote(localizer.T("operationUnavailableForCurrentState"));
                return null;
            }

            var result = await installationWorkflow.InstallOrUpdateAsync(snapshot, host.ApplyProgress, cancellationToken);
            host.SetOperationNote(result.Message);
            refreshHandled = await RequestRefresh(GameOperationsRefreshMode.SkipPersistedResume);
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
            if (!refreshHandled)
            {
                ApplySnapshotSafe(snapshot);
            }
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

    private async Task<bool> RequestRefresh(GameOperationsRefreshMode mode)
    {
        if (RefreshRequested is null)
        {
            return false;
        }

        foreach (Func<GameOperationsRefreshMode, Task> subscriber in RefreshRequested.GetInvocationList())
        {
            await subscriber(mode);
        }

        return true;
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
