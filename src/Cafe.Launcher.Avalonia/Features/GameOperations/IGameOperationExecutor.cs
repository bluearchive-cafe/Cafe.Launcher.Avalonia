using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// Executes game operations at the service layer — launch, install/update,
/// repair, uninstall, and download state control. The journey owns the rules;
/// this seam owns the pipelines behind them, one fake for tests instead of
/// three parallel workflow adapters.
/// </summary>
internal interface IGameOperationExecutor
{
    /// <summary>Gets whether a download or repair workflow is currently running.</summary>
    bool IsDownloadRunning { get; }

    /// <summary>Gets whether the active download workflow is paused.</summary>
    bool IsPaused { get; }

    /// <summary>Raised when the underlying download workflow starts or stops running.</summary>
    event Action? IsRunningChanged;

    /// <summary>Starts the game after applying launcher validation rules.</summary>
    Task<GameLaunchResult> LaunchAsync(
        LauncherStatusSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>Installs or updates the game using the supplied status snapshot.</summary>
    Task<GameOperationResult> InstallOrUpdateAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default);

    /// <summary>Runs a confirmed repair for the supplied status snapshot.</summary>
    Task<GameOperationResult> RepairAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress);

    /// <summary>Attempts to resume persisted work while respecting cancellation.</summary>
    Task<GameOperationResult?> ResumePersistedAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken);

    /// <summary>Validates uninstall eligibility for the supplied game path.</summary>
    Task<GameOperationResult> ValidateUninstallAsync(string gamePath);

    /// <summary>Runs a confirmed uninstall for the supplied status snapshot.</summary>
    Task<GameOperationResult> UninstallAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress);

    /// <summary>Stops work and optionally clears its persisted checkpoint.</summary>
    void Stop(bool clearPersistedState);

    /// <summary>Pauses active installation work.</summary>
    void Pause();

    /// <summary>Resumes active installation work.</summary>
    void Resume();
}
