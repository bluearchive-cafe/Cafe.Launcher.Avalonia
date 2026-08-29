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
    private readonly LocalizationService localizer;

    public GameRunnerResolver(IEnumerable<IGameRunner> runners)
        : this(runners, new LocalizationService())
    {
    }

    public GameRunnerResolver(IEnumerable<IGameRunner> runners, LocalizationService localizer)
    {
        this.runners = runners.ToArray();
        this.localizer = localizer;
    }

    /// <summary>
    /// Resolves a usable runner, or null when the current platform has no available
    /// runtime environment. <paramref name="preferredRunnerId"/> is the user-selected
    /// runner id, or null for auto mode.
    /// </summary>
    public async Task<IGameRunner?> ResolveAsync(
        string? preferredRunnerId = null,
        CancellationToken cancellationToken = default) =>
        (await ResolveWithDiagnosticsAsync(preferredRunnerId, cancellationToken).ConfigureAwait(false)).Runner;

    /// <summary>
    /// Resolves a usable runner while retaining the availability evidence needed to
    /// diagnose why a launch environment could not be selected.
    /// </summary>
    internal async Task<GameRunnerResolution> ResolveWithDiagnosticsAsync(
        string? preferredRunnerId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(preferredRunnerId))
        {
            var preferred = runners.FirstOrDefault(runner =>
                string.Equals(runner.Id, preferredRunnerId, StringComparison.OrdinalIgnoreCase));
            if (preferred is null)
            {
                return new GameRunnerResolution(
                    null,
                    $"{localizer.T("gameRuntimeRunner")}: {preferredRunnerId}{Environment.NewLine}" +
                    localizer.T("unknown"));
            }

            var availability = await preferred
                .CheckAvailabilityAsync(cancellationToken)
                .ConfigureAwait(false);
            return availability.Available
                ? new GameRunnerResolution(preferred, $"{localizer.T("gameRuntimeRunner")}: {preferred.Id}")
                : new GameRunnerResolution(
                    null,
                    $"{localizer.T("gameRuntimeRunner")}: {preferredRunnerId}{Environment.NewLine}" +
                    $"{preferred.Id}: {AvailabilityReason(availability)}");
        }

        var candidates = new List<string>();
        foreach (var runner in runners)
        {
            if (!runner.IsSupportedPlatform)
            {
                var unsupportedAvailability = await runner
                    .CheckAvailabilityAsync(cancellationToken)
                    .ConfigureAwait(false);
                candidates.Add($"{runner.Id}: {AvailabilityReason(unsupportedAvailability)}");
                continue;
            }

            var availability = await runner
                .CheckAvailabilityAsync(cancellationToken)
                .ConfigureAwait(false);
            if (availability.Available)
            {
                return new GameRunnerResolution(runner, $"{localizer.T("gameRuntimeRunner")}: {runner.Id}");
            }

            candidates.Add($"{runner.Id}: {AvailabilityReason(availability)}");
        }

        var details = candidates.Count == 0
            ? localizer.T("unknown")
            : string.Join(Environment.NewLine, candidates.Select(candidate => $"- {candidate}"));
        return new GameRunnerResolution(
            null,
            $"{localizer.T("gameRuntimeRunner")}: {localizer.T("gameRuntimeRunnerAuto")}{Environment.NewLine}{details}");
    }

    private string AvailabilityReason(GameRunnerAvailability availability) =>
        string.IsNullOrWhiteSpace(availability.Message)
            ? localizer.T("unknown")
            : availability.Message;
}

/// <summary>Resolved runner and the evidence collected while choosing it.</summary>
internal sealed record GameRunnerResolution(IGameRunner? Runner, string DiagnosticMessage);
