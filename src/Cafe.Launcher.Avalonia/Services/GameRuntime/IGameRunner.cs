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

    /// <summary>
    /// Checks whether the runner's runtime environment is installed and usable,
    /// evaluated against the same <paramref name="options"/> a subsequent
    /// <see cref="StartAsync"/> would use — availability and launch must agree
    /// on the configured runtime, or a valid custom runner path would be
    /// rejected before it is ever tried.
    /// </summary>
    Task<GameRunnerAvailability> CheckAvailabilityAsync(
        GameRuntimeOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Builds the host process start information and starts the game.</summary>
    Task<GameProcess> StartAsync(
        GameLaunchRequest request,
        GameRuntimeOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The compatibility prefix this runner would apply to a launch: the configured
    /// <see cref="GameRuntimeOptions.PrefixPath"/> when set, otherwise the runner's
    /// managed default for the game — or null when the runner does not use a prefix.
    /// Diagnostics report the prefix a launch actually targets, not just the config.
    /// </summary>
    string? GetEffectivePrefixPath(GameLaunchRequest request, GameRuntimeOptions options);

    /// <summary>
    /// The Proton build this runner would apply to a launch: the configured
    /// <see cref="GameRuntimeOptions.ProtonPath"/>, a marker for runtime-managed
    /// selection, or null when the runner does not use Proton.
    /// </summary>
    string? GetEffectiveProtonPath(GameRuntimeOptions options);
}
