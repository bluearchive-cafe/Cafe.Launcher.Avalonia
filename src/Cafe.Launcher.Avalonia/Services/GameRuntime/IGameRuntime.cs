using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Why a launch did not start a process. Selection failures return no process
/// and carry candidate evidence; start failures carry the offending exception.
/// </summary>
public enum GameRuntimeLaunchFailure
{
    None,
    NoRunnerSelected,
    StartFailed,

    /// <summary>Availability collection failed before any runner could be chosen.</summary>
    AvailabilityCheckFailed
}

/// <summary>One runner's current availability, as reported by the settings status display.</summary>
public sealed record GameRuntimeStatusEntry(string RunnerId, GameRunnerAvailability Availability);

/// <summary>
/// Outcome of one runtime launch attempt: the selected runner (when any), the
/// started host process, the effective environment snapshot for diagnostics,
/// and the availability evidence collected while choosing.
/// </summary>
public sealed record GameRuntimeLaunchResult(
    bool Success,
    string? RunnerId,
    GameProcess? Process,
    GameRuntimeDiagnosticSnapshot Diagnostic,
    IReadOnlyList<GameRuntimeStatusEntry> Candidates,
    GameRuntimeLaunchFailure Failure = GameRuntimeLaunchFailure.None,
    Exception? FailureException = null);

/// <summary>
/// The deep game-runtime module: resolves a runner, checks availability, starts
/// the game, registers process tracking, and produces the diagnostic snapshot —
/// one interface for launch and status, with every runner rule (custom-path
/// scoping, effective prefix/Proton) inside this module.
/// </summary>
public interface IGameRuntime
{
    /// <summary>
    /// Launches the game through the first available runner. <paramref name="preferredRunnerId"/>
    /// pins one runner automatically selected otherwise; a custom runner path
    /// applies only to that pinned runner.
    /// </summary>
    Task<GameRuntimeLaunchResult> LaunchAsync(
        GameLaunchRequest request,
        GameRuntimeOptions options,
        string? preferredRunnerId,
        CancellationToken cancellationToken = default);

    /// <summary>Collects every runner's availability against one options set (settings status row).</summary>
    Task<IReadOnlyList<GameRuntimeStatusEntry>> GetStatusesAsync(
        string? preferredRunnerId,
        GameRuntimeOptions options,
        CancellationToken cancellationToken = default);
}
