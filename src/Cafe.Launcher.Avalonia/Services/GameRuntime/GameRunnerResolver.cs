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
        GameRuntimeOptions? options = null,
        CancellationToken cancellationToken = default) =>
        (await ResolveWithDiagnosticsAsync(preferredRunnerId, options, cancellationToken).ConfigureAwait(false)).Runner;

    /// <summary>
    /// Resolves a usable runner while retaining the availability evidence needed to
    /// diagnose why a launch environment could not be selected. Availability checks
    /// run against the same <paramref name="options"/> the launch itself will use.
    /// </summary>
    internal async Task<GameRunnerResolution> ResolveWithDiagnosticsAsync(
        string? preferredRunnerId = null,
        GameRuntimeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var runtimeOptions = (options ?? new GameRuntimeOptions())
            .ForRunnerSelection(preferredRunnerId);

        if (!string.IsNullOrWhiteSpace(preferredRunnerId))
        {
            var preferred = runners.FirstOrDefault(runner =>
                string.Equals(runner.Id, preferredRunnerId, StringComparison.OrdinalIgnoreCase));
            if (preferred is null)
            {
                return new GameRunnerResolution(
                    null,
                    $"{localizer.T("gameRuntimeRunner")}: {preferredRunnerId}{Environment.NewLine}" +
                    localizer.T("unknown"),
                    runtimeOptions);
            }

            var availability = await preferred
                .CheckAvailabilityAsync(runtimeOptions, cancellationToken)
                .ConfigureAwait(false);
            return availability.Available
                ? new GameRunnerResolution(
                    preferred,
                    $"{localizer.T("gameRuntimeRunner")}: {preferred.Id}",
                    runtimeOptions,
                    availability)
                : new GameRunnerResolution(
                    null,
                    $"{localizer.T("gameRuntimeRunner")}: {preferredRunnerId}{Environment.NewLine}" +
                    $"{preferred.Id}: {AvailabilityReason(availability)}",
                    runtimeOptions,
                    availability);
        }

        var candidates = new List<string>();
        foreach (var runner in runners)
        {
            var availability = await runner
                .CheckAvailabilityAsync(runtimeOptions, cancellationToken)
                .ConfigureAwait(false);

            // A runner is only selectable when it both supports the current platform
            // and reports an available runtime — an unsupported runner must never be
            // chosen, even if its availability record were to claim otherwise.
            if (runner.IsSupportedPlatform && availability.Available)
            {
                return new GameRunnerResolution(
                    runner,
                    $"{localizer.T("gameRuntimeRunner")}: {runner.Id}",
                    runtimeOptions,
                    availability);
            }

            candidates.Add($"{runner.Id}: {AvailabilityReason(availability)}");
        }

        var details = candidates.Count == 0
            ? localizer.T("unknown")
            : string.Join(Environment.NewLine, candidates.Select(candidate => $"- {candidate}"));
        return new GameRunnerResolution(
            null,
            $"{localizer.T("gameRuntimeRunner")}: {localizer.T("gameRuntimeRunnerAuto")}{Environment.NewLine}{details}",
            runtimeOptions);
    }

    private string AvailabilityReason(GameRunnerAvailability availability) =>
        string.IsNullOrWhiteSpace(availability.Message)
            ? localizer.T("unknown")
            : string.IsNullOrWhiteSpace(availability.TechnicalDetail)
                ? availability.Message
                : $"{availability.Message}{Environment.NewLine}{availability.TechnicalDetail}";
}

/// <summary>
/// Resolved runner and the evidence collected while choosing it. Availability is
/// null only when no runner was checked at all (unknown preferred id).
/// </summary>
internal sealed record GameRunnerResolution(
    IGameRunner? Runner,
    string DiagnosticMessage,
    GameRuntimeOptions Options,
    GameRunnerAvailability? Availability = null);
