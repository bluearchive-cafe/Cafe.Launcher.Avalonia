using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>Coordinates installation, repair, resume, pause, and stop behavior.</summary>
internal interface IGameInstallationWorkflow
{
    /// <summary>Raised when <see cref="IsRunning"/> changes.</summary>
    event Action? IsRunningChanged;

    /// <summary>Gets whether an installation operation is active.</summary>
    bool IsRunning { get; }

    /// <summary>Gets whether the active installation operation is paused.</summary>
    bool IsPaused { get; }

    /// <summary>Installs or updates the game represented by the snapshot.</summary>
    Task<GameOperationResult> InstallOrUpdateAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default);

    /// <summary>Repairs the game represented by the snapshot.</summary>
    Task<GameOperationResult> RepairAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress);

    /// <summary>Resumes a persisted installation operation when one exists.</summary>
    Task<GameOperationResult?> ResumePersistedAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken);

    /// <summary>Stops the active operation and optionally clears persisted state.</summary>
    void Stop(bool clearPersistedState);

    /// <summary>Pauses the active operation.</summary>
    void Pause();

    /// <summary>Resumes the active operation.</summary>
    void Resume();
}
