using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>One runner's current availability, as reported by the settings status display.</summary>
public sealed record GameRuntimeStatusEntry(string RunnerId, GameRunnerAvailability Availability);

/// <summary>
/// Collects every registered runner's availability against one set of runtime options,
/// so the settings page can show which environments actually work (executable path,
/// version, or why they do not). Availability runs the real version probe, so callers
/// should treat this as a background refresh, not a synchronous read.
/// </summary>
public sealed class GameRuntimeStatusService
{
    private readonly IReadOnlyList<IGameRunner> runners;

    public GameRuntimeStatusService(IEnumerable<IGameRunner> runners)
    {
        this.runners = runners.ToArray();
    }

    public async Task<IReadOnlyList<GameRuntimeStatusEntry>> GetStatusesAsync(
        string? preferredRunnerId,
        GameRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<GameRuntimeStatusEntry>(runners.Count);
        foreach (var runner in runners)
        {
            var runnerOptions = options.ForStatusCheck(preferredRunnerId, runner.Id);
            var availability = await runner
                .CheckAvailabilityAsync(runnerOptions, cancellationToken)
                .ConfigureAwait(false);
            entries.Add(new GameRuntimeStatusEntry(runner.Id, availability));
        }

        return entries;
    }
}
