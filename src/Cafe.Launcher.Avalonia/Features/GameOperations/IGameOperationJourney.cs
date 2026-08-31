using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// Drives a game operation from user intent to terminal outcome —
/// validation, execution, retry, refresh, and notification — while the
/// presentation only renders strong-typed state. Confirmation and stopping
/// presentation flows live with the host; this interface exposes operations
/// and their terminal effects only.
/// </summary>
internal interface IGameOperationJourney
{
    /// <summary>Raised when shell state must be refreshed after an operation.</summary>
    event Func<GameOperationsRefreshMode, Task>? RefreshRequested;

    /// <summary>Raised when a failure action should open the log viewer.</summary>
    event Func<Task>? OpenLogViewerRequested;

    /// <summary>Raised when a successful launch should minimize the launcher.</summary>
    event Action? MinimizeRequested;

    /// <summary>Raised when the underlying installation workflow starts or stops running.</summary>
    event Action? IsRunningChanged;

    /// <summary>Gets whether an installation workflow is active.</summary>
    bool IsDownloadRunning { get; }

    /// <summary>Gets whether the active installation workflow is paused.</summary>
    bool IsPaused { get; }

    /// <summary>Starts the game using the supplied status snapshot.</summary>
    Task StartGameAsync(LauncherStatusSnapshot snapshot);

    /// <summary>Refreshes launcher state and reports whether a game update is available.</summary>
    Task CheckForUpdateAsync(LauncherStatusSnapshot snapshot);

    /// <summary>Installs or updates the game using the supplied status snapshot.</summary>
    Task InstallOrUpdateAsync(LauncherStatusSnapshot snapshot);

    /// <summary>Creates the desktop shortcut for the installed game and reports the outcome.</summary>
    Task CreateDesktopShortcutAsync(LauncherStatusSnapshot snapshot);

    /// <summary>Opens the installed game folder in the platform file manager.</summary>
    void OpenGameFolder(LauncherStatusSnapshot snapshot);

    /// <summary>Runs a confirmed repair for the supplied status snapshot.</summary>
    Task RepairAsync(LauncherStatusSnapshot snapshot);

    /// <summary>Validates uninstall eligibility and reports the affected file count.</summary>
    Task<GameOperationResult?> ValidateUninstallAsync(LauncherStatusSnapshot snapshot);

    /// <summary>Runs a confirmed uninstall for the supplied status snapshot.</summary>
    Task ConfirmUninstallAsync(LauncherStatusSnapshot snapshot);

    /// <summary>Attempts to continue a persisted download while respecting cancellation.</summary>
    Task ResumePersistedAsync(LauncherStatusSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>Executes the already-confirmed stop action.</summary>
    void PerformStop();

    /// <summary>Stops work and optionally clears its persisted checkpoint.</summary>
    void Stop(bool clearPersistedState);

    /// <summary>Pauses active installation work.</summary>
    void Pause();

    /// <summary>Resumes active installation work.</summary>
    void Resume();
}
