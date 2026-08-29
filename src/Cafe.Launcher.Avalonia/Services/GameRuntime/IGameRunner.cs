using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Strategy for actually executing a game on the current platform:
/// native PE execution, UMU/Proton, Wine, or CrossOver. A runner decides how
/// to build <see cref="System.Diagnostics.ProcessStartInfo"/>, which environment
/// variables to inject, and which host process to hand back for tracking.
/// </summary>
public interface IGameRunner
{
    /// <summary>Stable identifier used by settings and diagnostics ("native", "umu", ...).</summary>
    string Id { get; }

    /// <summary>Whether this runner can operate on the current operating system at all.</summary>
    bool IsSupportedPlatform { get; }

    /// <summary>Checks whether the runner's runtime environment is installed and usable.</summary>
    Task<GameRunnerAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>Builds the host process start information and starts the game.</summary>
    Task<GameProcess> StartAsync(
        GameLaunchRequest request,
        GameRuntimeOptions options,
        CancellationToken cancellationToken = default);
}
