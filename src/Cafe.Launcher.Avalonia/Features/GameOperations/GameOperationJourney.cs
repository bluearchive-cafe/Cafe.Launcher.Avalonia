using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

    /// <summary>
    /// Owns the journey rules for launch, install, repair, and uninstall:
    /// state validation, confirmations, retry, refresh, and notifications.
    /// The presentation module (GameOperationsViewModel) stays pure — state
    /// binding, progress mapping, and thin command delegation — and drives
    /// refresh / log-viewer / minimize through the host interface.
    /// </summary>
    internal sealed class GameOperationJourney : IGameOperationJourney
{
    /// <summary>Forwards installation running-state changes to the presentation host.</summary>
    public event Action? IsRunningChanged
    {
        add => executor.IsRunningChanged += value;
        remove => executor.IsRunningChanged -= value;
    }

    private readonly IGameOperationExecutor executor;
    private readonly IGameShortcutService shortcutService;
    private readonly Func<TimeSpan, Task> delayAsync;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly LocalDiagnostics diagnostics;
    private readonly IErrorHandlingService errorHandling;
    private readonly IGameOperationJourneyHost host;

    private LauncherStatusSnapshot? lastInstallSnapshot;

    /// <summary>Initializes the execution seam and presentation host for game operations.</summary>
    public GameOperationJourney(
        IGameOperationExecutor executor,
        IGameShortcutService shortcutService,
        LocalizationService localizer,
        ToastService toastService,
        LocalDiagnostics diagnostics,
        IErrorHandlingService errorHandling,
        Func<TimeSpan, Task> delayAsync,
        IGameOperationJourneyHost host)
    {
        this.executor = executor;
        this.shortcutService = shortcutService;
        this.delayAsync = delayAsync;
        this.localizer = localizer;
        this.toastService = toastService;
        this.diagnostics = diagnostics;
        this.errorHandling = errorHandling;
        this.host = host;
    }

    /// <summary>Gets whether a download or repair workflow is currently running.</summary>
    public bool IsDownloadRunning => executor.IsDownloadRunning;

    /// <summary>Gets whether the active download workflow is paused.</summary>
    public bool IsPaused => executor.IsPaused;

    /// <summary>Starts the game after validating the supplied launcher state.</summary>
    public async Task StartGameAsync(LauncherStatusSnapshot snapshot)
    {
        if (!PrepareShellOnly(snapshot))
        {
            return;
        }

        host.SetBusy(true);

        try
        {
            var launchResult = await executor.LaunchAsync(snapshot);
            host.SetLaunchCheckResult(launchResult.Validation.Message);
            var launchDiagnostic = BuildLaunchDiagnostic(launchResult);

            if (launchResult.Success)
            {
                await diagnostics.MessageAsync("GameLaunch", launchDiagnostic);
                toastService.ShowSuccess(localizer.T(LocalizationKeys.GameLaunchedMinimized));
                await delayAsync(TimeSpan.FromMilliseconds(600));
                host.RequestMinimize();
            }
            else
            {
                toastService.ShowWarning(launchResult.Message);
                await diagnostics.WarningAsync("GameLaunch", launchDiagnostic);
                if (launchResult.DiagnosticException is not null)
                {
                    await diagnostics.ErrorAsync("GameLaunch", launchResult.DiagnosticException);
                }
            }
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Game launch failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(LocalizationKeys.GameLaunchFailed, exception.Message) });
        }
        finally
        {
            host.SetBusy(false);
        }
    }

    /// <summary>Refreshes launcher state and reports whether a game update is available.</summary>
    public async Task CheckForUpdateAsync(LauncherStatusSnapshot snapshot)
    {
        if (!PrepareShellOnly(snapshot))
        {
            return;
        }

        host.SetBusy(true);

        try
        {
            await RequestRefresh(GameOperationsRefreshMode.SkipPersistedResume);
            ReportUpdateCheck(host.CurrentSnapshot ?? snapshot);
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Game update check failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(LocalizationKeys.GameCheckUpdateFailed, exception.Message) });
        }
        finally
        {
            host.SetBusy(false);
        }
    }

    /// <summary>Creates the desktop shortcut for the installed game and reports the outcome.</summary>
    public async Task CreateDesktopShortcutAsync(LauncherStatusSnapshot snapshot)
    {
        if (!PrepareShellOnly(snapshot))
        {
            return;
        }

        host.SetBusy(true);

        try
        {
            var result = await shortcutService.CreateDesktopShortcutAsync(snapshot);
            switch (result.Status)
            {
                case GameShortcutStatus.Created:
                    toastService.ShowSuccess(localizer.T(LocalizationKeys.GameShortcutCreated));
                    break;
                case GameShortcutStatus.UnsupportedPlatform:
                    toastService.ShowWarning(localizer.T(LocalizationKeys.GameShortcutUnsupported));
                    break;
                case GameShortcutStatus.GameNotResolved:
                    toastService.ShowWarning(localizer.T(LocalizationKeys.GameShortcutTargetMissing));
                    break;
                case GameShortcutStatus.Failed:
                    toastService.ShowError(localizer.F(LocalizationKeys.GameShortcutFailed, result.Detail));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(snapshot), result.Status, null);
            }
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Desktop shortcut creation failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(LocalizationKeys.GameShortcutFailed, exception.Message) });
        }
        finally
        {
            host.SetBusy(false);
        }
    }

    /// <summary>Opens the installed game folder in the platform file manager.</summary>
    public void OpenGameFolder(LauncherStatusSnapshot snapshot)
    {
        if (!PrepareShellOnly(snapshot))
        {
            return;
        }

        try
        {
            if (!shortcutService.TryOpenGameFolder(snapshot))
            {
                toastService.ShowWarning(localizer.T(LocalizationKeys.GameFolderMissing));
            }
        }
        catch (Exception exception)
        {
            _ = errorHandling.HandleErrorAsync("Opening the game folder failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(LocalizationKeys.UnexpectedError, exception.Message) });
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
            var result = await executor.RepairAsync(snapshot, host.ApplyProgress);
            ShowOperationResult(result);
            refreshHandled = await RequestRefresh(GameOperationsRefreshMode.Normal);
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Game repair failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(LocalizationKeys.GameRepairFailed, exception.Message) });
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

    /// <summary>Validates uninstall eligibility and reports the affected file count.</summary>
    public async Task<GameOperationResult?> ValidateUninstallAsync(LauncherStatusSnapshot snapshot)
    {
        var validation = await executor.ValidateUninstallAsync(snapshot.LocalGame.GamePath);
        return validation.Success ? validation : null;
    }

    /// <summary>Runs a confirmed uninstall and refreshes launcher state afterward.</summary>
    public async Task ConfirmUninstallAsync(LauncherStatusSnapshot snapshot)
    {
        if (snapshot.RuntimeState != LauncherRuntimeState.Ready)
        {
            return;
        }

        host.SetBusy(true);

        try
        {
            // Prepare for uninstall — the first progress update from the workflow
            // will set the correct icon. Call PrepareOperation to reset panel state.
            host.PrepareOperation();
            var result = await executor.UninstallAsync(snapshot, host.ApplyProgress);
            await RequestRefresh(GameOperationsRefreshMode.Normal);
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Game uninstall failed.", exception,
                new ErrorHandlingOptions { ShowToast = false });
        }
        finally
        {
            host.SetBusy(false);
        }
    }

    /// <summary>Executes the stop after the confirmation flow has completed.</summary>
    public void PerformStop()
    {
        executor.Stop(clearPersistedState: true);
        try { toastService.ShowWarning(localizer.T(LocalizationKeys.StopRequested)); }
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
            var result = await executor.ResumePersistedAsync(
                snapshot,
                host.ApplyProgress,
                cancellationToken);
            if (result is null)
            {
                return;
            }

            ShowOperationResult(result);
            await RequestRefresh(GameOperationsRefreshMode.Normal);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Persisted game download resume failed.", exception,
                new ErrorHandlingOptions { ShowToast = false });
        }
        finally
        {
            host.SetBusy(false);
        }
    }

    /// <summary>Stops the active workflow, optionally clearing its persisted checkpoint.</summary>
    public void Stop(bool clearPersistedState)
    {
        executor.Stop(clearPersistedState);
    }

    /// <summary>Pauses the active download workflow.</summary>
    public void Pause()
    {
        executor.Pause();
    }

    /// <summary>Resumes the active download workflow.</summary>
    public void Resume()
    {
        executor.Resume();
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
                host.ShowRepairConfirmation(localizer.T(LocalizationKeys.RepairWarning));
                return null;
            }

            if (snapshot.RuntimeState is LauncherRuntimeState.IoFailure or LauncherRuntimeState.RemoteUnavailable)
            {
                refreshHandled = await RequestRefresh(GameOperationsRefreshMode.Normal);
                return null;
            }

            if (snapshot.RuntimeState == LauncherRuntimeState.Ready)
            {
                return null;
            }

            var result = await executor.InstallOrUpdateAsync(snapshot, host.ApplyProgress, cancellationToken);
            refreshHandled = await RequestRefresh(GameOperationsRefreshMode.SkipPersistedResume);
            return result;
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Game install/update failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(LocalizationKeys.GameInstallOrUpdateFailed, exception.Message) });
            return new GameOperationResult
            {
                Success = false,
                Message = localizer.T(LocalizationKeys.LauncherStateNotLoaded),
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
            Title = localizer.T(LocalizationKeys.InstallUpdateFailedTitle),
            Message = message,
            Severity = ToastSeverity.Error,
            PrimaryAction = new ToastAction(localizer.T(LocalizationKeys.Retry), RetryInstallOrUpdateAsync, Timeout: null),
            SecondaryAction = new ToastAction(localizer.T(LocalizationKeys.ViewLog), OpenLogViewerAsync)
        });
    }

    private async Task<ToastActionResult> RetryInstallOrUpdateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = lastInstallSnapshot;
        if (snapshot is null)
        {
            return ToastActionResult.Failure(localizer.T(LocalizationKeys.LauncherStateNotLoaded), localizer.T(LocalizationKeys.InstallUpdateFailedTitle));
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
                ? localizer.T(LocalizationKeys.OperationStopped)
                : localizer.T(LocalizationKeys.OperationUnavailableForCurrentState);
        }

        return ToastActionResult.Failure(message, localizer.T(LocalizationKeys.InstallUpdateFailedTitle));
    }

    private async Task<ToastActionResult> OpenLogViewerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await host.ShowLogViewerAsync();
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

    private void ReportUpdateCheck(LauncherStatusSnapshot snapshot)
    {
        var latestVersion = snapshot.Remote.GameConfig?.GameLatestVersion ?? localizer.T(LocalizationKeys.Unknown);
        switch (snapshot.RuntimeState)
        {
            case LauncherRuntimeState.UpdateAvailable:
                toastService.Show(localizer.F(LocalizationKeys.GameCheckUpdateAvailable, latestVersion));
                break;
            case LauncherRuntimeState.Ready:
                toastService.ShowSuccess(localizer.F(LocalizationKeys.GameCheckUpdateUpToDate, latestVersion));
                break;
            case LauncherRuntimeState.RemoteUnavailable:
                toastService.ShowWarning(localizer.T(LocalizationKeys.GameRemoteStateUnavailable));
                break;
            case LauncherRuntimeState.NotInstalled:
                toastService.Show(localizer.T(LocalizationKeys.GameNotInstalled));
                break;
            case LauncherRuntimeState.BelowLowestVersion:
                toastService.ShowWarning(localizer.T(LocalizationKeys.GameBelowLowestVersion));
                break;
            case LauncherRuntimeState.Corrupted:
                toastService.ShowWarning(localizer.T(LocalizationKeys.GameCorruptedInstallationState));
                break;
            case LauncherRuntimeState.IoFailure:
                toastService.ShowWarning(localizer.T(LocalizationKeys.GameInstallationStateReadFailed));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.RuntimeState, null);
        }
    }

    private Task<bool> RequestRefresh(GameOperationsRefreshMode mode) =>
        host.RefreshAsync(mode);

    private bool PrepareShellOnly(LauncherStatusSnapshot? snapshot)
    {
        if (host.IsBusy)
        {
            return false;
        }

        if (snapshot is null)
        {
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

    private static string BuildLaunchDiagnostic(GameLaunchResult launchResult)
    {
        if (string.IsNullOrWhiteSpace(launchResult.DiagnosticMessage)
            || string.Equals(launchResult.Message, launchResult.DiagnosticMessage, StringComparison.Ordinal))
        {
            return launchResult.Message;
        }

        return $"{launchResult.Message}{Environment.NewLine}{launchResult.DiagnosticMessage}";
    }
}
