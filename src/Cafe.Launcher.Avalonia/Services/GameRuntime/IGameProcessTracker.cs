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
    /// Whether the game is currently running: a live tracked process wins;
    /// otherwise falls back to a process-name scan for cross-session launches.
    /// </summary>
    Task<bool> IsGameRunningAsync(string exeName, CancellationToken cancellationToken = default);
}
