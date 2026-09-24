using System;
using System.Collections.Generic;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// Observable lifecycle of the game session this launcher started. A successful
/// launch report only means the host process (the game itself natively, the
/// runner under Wine/UMU) was spawned; this monitor turns what happens after
/// that spawn into a state the UI can show.
/// </summary>
public enum GameSessionState
{
    /// <summary>No launch has happened yet in this launcher session.</summary>
    Idle,

    /// <summary>The host process was spawned; the game's own process family has not been seen yet.</summary>
    Starting,

    /// <summary>A process of the game's name family is running.</summary>
    Running,

    /// <summary>The tracked host process exited and the game's process family never appeared.</summary>
    StartFailed,

    /// <summary>
    /// The game had been seen running and is no longer: either the tracked host exited after
    /// the family had appeared (<see cref="LastSessionExit"/> carries its exit), or the family
    /// stopped being visible to consecutive scans once the host was gone (no exit info — the
    /// host's exit code belongs to the host, not to this moment).
    /// </summary>
    Exited
}

/// <summary>
/// Watches the game session started by a successful launch. The exit of the tracked host
/// process alone says nothing about whether the game ever ran — under a runner it is the
/// runner that dies — so the family scan decides between <see cref="GameSessionState.StartFailed"/>
/// and <see cref="GameSessionState.Exited"/>, per ADR-035.
/// </summary>
public interface IGameSessionMonitor
{
    /// <summary>Gets the current session state.</summary>
    GameSessionState State { get; }

    /// <summary>Gets the exit details of the current session's host process, once it has exited.</summary>
    Services.GameRuntime.GameLaunchExitInfo? LastSessionExit { get; }

    /// <summary>
    /// Raised on a worker thread when the session reaches a state worth reporting; rapid
    /// transitions inside one BeginSession round coalesce into a single notification that
    /// carries the final state.
    /// </summary>
    event Action? StateChanged;

    /// <summary>
    /// Starts watching a freshly launched session. <paramref name="knownExeNames"/> is the
    /// game's process family (same derivation the destructive gates use); <paramref name="runnerId"/>
    /// distinguishes a native launch, where the tracked host already is the game, from a
    /// runner launch, where the game has to be seen first.
    /// </summary>
    void BeginSession(string runnerId, IReadOnlyList<string> knownExeNames);
}
