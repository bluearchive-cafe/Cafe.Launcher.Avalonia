using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Picks the <see cref="IGameRunner"/> to use for a launch. When the user pinned a
/// specific runner (later phases), that runner wins; otherwise auto mode walks the
/// registered runners in priority order and returns the first whose environment is
/// available on the current platform.
/// </summary>
public sealed class GameRunnerResolver
{
    private readonly IReadOnlyList<IGameRunner> runners;

    public GameRunnerResolver(IEnumerable<IGameRunner> runners)
    {
        this.runners = runners.ToArray();
    }

    /// <summary>
    /// Resolves a usable runner, or null when the current platform has no available
    /// runtime environment. <paramref name="preferredRunnerId"/> is the user-selected
    /// runner id, or null for auto mode.
    /// </summary>
    public async Task<IGameRunner?> ResolveAsync(
        string? preferredRunnerId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(preferredRunnerId))
        {
            var preferred = runners.FirstOrDefault(runner =>
                string.Equals(runner.Id, preferredRunnerId, StringComparison.OrdinalIgnoreCase));
            if (preferred is null)
            {
                return null;
            }

            var availability = await preferred
                .CheckAvailabilityAsync(cancellationToken)
                .ConfigureAwait(false);
            return availability.Available ? preferred : null;
        }

        foreach (var runner in runners)
        {
            if (!runner.IsSupportedPlatform)
            {
                continue;
            }

            var availability = await runner
                .CheckAvailabilityAsync(cancellationToken)
                .ConfigureAwait(false);
            if (availability.Available)
            {
                return runner;
            }
        }

        return null;
    }
}
