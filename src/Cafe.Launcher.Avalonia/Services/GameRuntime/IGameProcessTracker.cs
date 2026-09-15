using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Tracks the game host process launched by this launcher session. Process
/// handles are the authoritative running-state source; process-name scanning
/// is only a fallback for games started outside the current session (for
/// example via a desktop shortcut before the launcher was restarted).
/// </summary>
public interface IGameProcessTracker
{
    /// <summary>Starts tracking a host process returned by the game run-time module.</summary>
    void Register(GameProcess process);

    /// <summary>Whether a process registered in this session is still alive.</summary>
    bool HasLiveTrackedProcess { get; }

    /// <summary>Exit details of the most recent tracked process, or null if none has exited yet.</summary>
    GameLaunchExitInfo? LastExit { get; }

    /// <summary>
    /// The game's currently running processes, by name without extension; empty means the game is
    /// not running. <paramref name="knownExeNames"/> is the game's own process family, from
    /// <see cref="GameProcessNames"/>; callers guarantee it is non-empty.
    /// </summary>
    /// <remarks>
    /// A live tracked process wins, because a handle stays authoritative where a name scan cannot
    /// see the process; otherwise the scan covers games started outside this session. The returned
    /// names are what the caller should put in front of the user: naming only the configured host
    /// would be a lie when it is the anti-cheat host that is still holding the install directory.
    /// </remarks>
    Task<IReadOnlyList<string>> FindRunningGameProcessesAsync(
        IReadOnlyList<string> knownExeNames,
        CancellationToken cancellationToken = default);
}
