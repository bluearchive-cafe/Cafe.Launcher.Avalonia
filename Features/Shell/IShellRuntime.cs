using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.Shell;

/// <summary>
/// The shell runtime interface used by the window presentation and its tests.
/// </summary>
public interface IShellRuntime : IDisposable
{
    /// <summary>Raised when shell presentation state changes.</summary>
    event Action? PresentationChanged;

    /// <summary>Raised when the configured status-detail mode changes.</summary>
    event Action? StatusDetailModeChanged;

    /// <summary>Gets whether the shell is currently processing an operation.</summary>
    bool IsBusy { get; }

    /// <summary>Gets whether reduced motion is currently effective.</summary>
    bool IsMotionReduced { get; }

    /// <summary>Gets the startup update-check task, if one was scheduled.</summary>
    Task PendingStartupUpdateCheck { get; }

    /// <summary>Initializes the shell state after the main window opens.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Refreshes launcher state and its shell presentation.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels lifecycle work and waits for active refreshes to finish before shutdown.
    /// Returns <see langword="false"/> when pending settings could not be persisted.
    /// </summary>
    Task<bool> PrepareForShutdownAsync();

    /// <summary>Re-evaluates the system motion preference and updates presentation state.</summary>
    void RefreshSystemMotionPreference();

    /// <summary>Refreshes shell state after a completed game operation.</summary>
    Task HandleOperationsRefreshRequestedAsync(GameOperationsRefreshMode mode);

    /// <summary>Attempts to handle Escape through the active shell surface.</summary>
    bool TryHandleEscape();
}
