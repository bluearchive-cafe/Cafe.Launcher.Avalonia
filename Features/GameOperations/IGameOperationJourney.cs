using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// Drives a game operation from user intent to terminal outcome —
/// validation, confirmation, execution, retry, refresh, and notification —
/// while the presentation only renders strong-typed state.
/// </summary>
internal interface IGameOperationJourney
{
    event Func<GameOperationsRefreshMode, Task>? RefreshRequested;
    event Func<Task>? OpenLogViewerRequested;
    event Action? MinimizeRequested;

    bool IsDownloadRunning { get; }
    bool IsPaused { get; }

    Task StartGameAsync(LauncherStatusSnapshot snapshot);
    Task InstallOrUpdateAsync(LauncherStatusSnapshot snapshot);
    Task RequestRepairAsync(LauncherStatusSnapshot snapshot);
    Task RepairAsync(LauncherStatusSnapshot snapshot);
    Task RequestUninstallAsync(LauncherStatusSnapshot snapshot);
    Task ConfirmUninstallAsync(LauncherStatusSnapshot snapshot);
    void RequestStop();
    Task ResumePersistedAsync(LauncherStatusSnapshot snapshot, CancellationToken cancellationToken);
    void PerformStop();
    void Stop(bool clearPersistedState);
    void Pause();
    void Resume();
}
