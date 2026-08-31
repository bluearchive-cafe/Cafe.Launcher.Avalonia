using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Deep game-runtime module: owns runner selection, availability probing,
/// process start, process tracking, effective prefix/Proton decisions, and the
/// diagnostic snapshot. Runners are declarative <see cref="GameRunnerDefinition"/>
/// specs; every rule lives here, in one place, across launch and status paths.
/// </summary>
public sealed class GameRuntime : IGameRuntime
{
    private readonly IReadOnlyList<GameRunnerDefinition> runners;
    private readonly IProcessLauncher processLauncher;
    private readonly IGameProcessTracker processTracker;
    private readonly Func<string, string?, string?> locateExecutable;
    private readonly Func<string, string, TimeSpan, CancellationToken, Task<RuntimeProbeResult>> probeVersion;

    public GameRuntime(
        IEnumerable<GameRunnerDefinition> runners,
        IProcessLauncher processLauncher,
        IGameProcessTracker processTracker)
        : this(
            runners,
            processLauncher,
            processTracker,
            (name, explicitPath) => ExecutableLocator.FindInPath(name, explicitPath),
            RuntimeVersionProbe.ProbeAsync)
    {
    }

    internal GameRuntime(
        IEnumerable<GameRunnerDefinition> runners,
        IProcessLauncher processLauncher,
        IGameProcessTracker processTracker,
        Func<string, string?, string?> locateExecutable,
        Func<string, string, TimeSpan, CancellationToken, Task<RuntimeProbeResult>> probeVersion)
    {
        this.runners = runners.ToArray();
        this.processLauncher = processLauncher;
        this.processTracker = processTracker;
        this.locateExecutable = locateExecutable;
        this.probeVersion = probeVersion;
    }

    public async Task<GameRuntimeLaunchResult> LaunchAsync(
        GameLaunchRequest request,
        GameRuntimeOptions options,
        string? preferredRunnerId,
        CancellationToken cancellationToken = default)
    {
        var runtimeOptions = options ?? new GameRuntimeOptions();
        var statuses = await CollectStatusesAsync(preferredRunnerId, runtimeOptions, cancellationToken)
            .ConfigureAwait(false);
        var candidates = statuses
            .Select(status => new GameRuntimeStatusEntry(status.Runner.Id, status.Availability))
            .ToArray();

        foreach (var (runner, availability) in statuses)
        {
            // An unsupported platform must never be chosen even if its record claims otherwise.
            if (!runner.IsSupportedPlatform || !availability.Available)
            {
                continue;
            }

            GameProcess process;
            try
            {
                var startOptions = ScopedOptions(preferredRunnerId, runner.Id, runtimeOptions);
                process = Start(runner, request, startOptions);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new GameRuntimeLaunchResult(
                    Success: false,
                    RunnerId: runner.Id,
                    Process: null,
                    Diagnostic: BuildDiagnostic(runner, availability, request, runtimeOptions),
                    Candidates: candidates,
                    Failure: GameRuntimeLaunchFailure.StartFailed,
                    FailureException: exception);
            }

            processTracker.Register(process);
            return new GameRuntimeLaunchResult(
                Success: true,
                RunnerId: runner.Id,
                Process: process,
                Diagnostic: BuildDiagnostic(runner, availability, request, runtimeOptions),
                Candidates: candidates);
        }

        return new GameRuntimeLaunchResult(
            Success: false,
            RunnerId: null,
            Process: null,
            Diagnostic: BuildDiagnostic(null, null, request, runtimeOptions),
            Candidates: candidates,
            Failure: GameRuntimeLaunchFailure.NoRunnerSelected);
    }

    public async Task<IReadOnlyList<GameRuntimeStatusEntry>> GetStatusesAsync(
        string? preferredRunnerId,
        GameRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        var statuses = await CollectStatusesAsync(preferredRunnerId, options ?? new GameRuntimeOptions(), cancellationToken)
            .ConfigureAwait(false);
        return statuses
            .Select(status => new GameRuntimeStatusEntry(status.Runner.Id, status.Availability))
            .ToArray();
    }

    private async Task<IReadOnlyList<(GameRunnerDefinition Runner, GameRunnerAvailability Availability)>> CollectStatusesAsync(
        string? preferredRunnerId,
        GameRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        var statuses = new List<(GameRunnerDefinition, GameRunnerAvailability)>(runners.Count);
        foreach (var runner in SelectionOrder(preferredRunnerId))
        {
            var runnerOptions = ScopedOptions(preferredRunnerId, runner.Id, options);
            var availability = await CheckAvailabilityAsync(runner, runnerOptions, cancellationToken)
                .ConfigureAwait(false);
            statuses.Add((runner, availability));
        }

        return statuses;
    }

    /// <summary>
    /// A custom runner path applies only when the user explicitly selected that
    /// runner; auto mode discovers each candidate independently, otherwise a Wine
    /// executable could satisfy UMU's generic version probe (or vice versa) and
    /// the resolver would report the wrong runtime. One rule, everywhere.
    /// </summary>
    private static GameRuntimeOptions ScopedOptions(
        string? preferredRunnerId,
        string runnerId,
        GameRuntimeOptions options) =>
        !string.IsNullOrWhiteSpace(preferredRunnerId)
        && string.Equals(preferredRunnerId, runnerId, StringComparison.OrdinalIgnoreCase)
            ? options
            : options with { RunnerPath = null };

    private IEnumerable<GameRunnerDefinition> SelectionOrder(string? preferredRunnerId)
    {
        if (string.IsNullOrWhiteSpace(preferredRunnerId))
        {
            return runners;
        }

        return runners.Where(runner =>
            string.Equals(runner.Id, preferredRunnerId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<GameRunnerAvailability> CheckAvailabilityAsync(
        GameRunnerDefinition runner,
        GameRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        if (!runner.IsSupportedPlatform)
        {
            return new GameRunnerAvailability(
                GameRunnerAvailabilityStatus.Unsupported,
                Message: $"{runner.DisplayName} requires {runner.RequiredPlatformName}.");
        }

        if (runner.ExecutableName is null)
        {
            return new GameRunnerAvailability(GameRunnerAvailabilityStatus.Available);
        }

        var executablePath = locateExecutable(runner.ExecutableName, options.RunnerPath);
        if (executablePath is null)
        {
            return new GameRunnerAvailability(
                GameRunnerAvailabilityStatus.NotFound,
                Message: options.RunnerPath is null
                    ? $"{runner.ExecutableName} was not found on PATH."
                    : $"{runner.ExecutableName} was not found at the configured path: {options.RunnerPath}");
        }

        var probeResult = await probeVersion(
                executablePath,
                runner.VersionArgument,
                RuntimeVersionProbe.DefaultTimeout,
                cancellationToken)
            .ConfigureAwait(false);
        if (!probeResult.Succeeded || string.IsNullOrWhiteSpace(probeResult.Version))
        {
            return new GameRunnerAvailability(
                GameRunnerAvailabilityStatus.Broken,
                ExecutablePath: executablePath,
                Message: $"{runner.ExecutableName} exists but did not respond to its version probe.",
                TechnicalDetail: probeResult.Describe(executablePath, runner.VersionArgument));
        }

        return new GameRunnerAvailability(
            GameRunnerAvailabilityStatus.Available,
            Version: probeResult.Version,
            ExecutablePath: executablePath);
    }

    private GameProcess Start(
        GameRunnerDefinition runner,
        GameLaunchRequest request,
        GameRuntimeOptions options)
    {
        var executable = runner.ExecutableName is null
            ? request.ExecutablePath
            : locateExecutable(runner.ExecutableName, options.RunnerPath)
                ?? throw new InvalidOperationException(
                    $"{runner.ExecutableName} was not found. Install {runner.DisplayName} or configure its path.");

        var process = processLauncher.Start(BuildStartInfo(runner, executable, request, options))
            ?? throw new InvalidOperationException(StartFailureMessage(runner));

        return new GameProcess(process, runner.Id);
    }

    private static ProcessStartInfo BuildStartInfo(
        GameRunnerDefinition runner,
        string executable,
        GameLaunchRequest request,
        GameRuntimeOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false
        };

        if (runner.ExecutableName is not null)
        {
            startInfo.ArgumentList.Add(request.ExecutablePath);
        }

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        switch (runner.EnvironmentStyle)
        {
            case GameRuntimeEnvironmentStyle.None:
                break;
            case GameRuntimeEnvironmentStyle.Wine:
                startInfo.Environment["WINEPREFIX"] = GetEffectivePrefixPath(request, runner.Id, options)!;
                break;
            case GameRuntimeEnvironmentStyle.Umu:
                startInfo.Environment["GAMEID"] = request.GameId;
                startInfo.Environment["WINEPREFIX"] = GetEffectivePrefixPath(request, runner.Id, options)!;
                if (!string.IsNullOrWhiteSpace(options.ProtonPath))
                {
                    startInfo.Environment["PROTONPATH"] = options.ProtonPath;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(runner), runner.EnvironmentStyle, null);
        }

        return startInfo;
    }

    private static string StartFailureMessage(GameRunnerDefinition runner) =>
        runner.EnvironmentStyle == GameRuntimeEnvironmentStyle.None
            ? "Failed to start game."
            : $"Failed to start {runner.DisplayName}.";

    /// <summary>
    /// The compatibility prefix a launch targets: the configured path when set,
    /// otherwise the runner-managed default isolated per game and runner.
    /// </summary>
    private static string GetEffectivePrefixPath(GameLaunchRequest request, string runnerId, GameRuntimeOptions options) =>
        string.IsNullOrWhiteSpace(options.PrefixPath)
            ? GameCompatibilityPaths.GetDefaultPrefixPath(request.GameId, runnerId)
            : options.PrefixPath;

    /// <summary>
    /// The Proton build a launch targets; UMU reports "auto" when it selects a
    /// build itself so the effective choice is never blank in diagnostics.
    /// </summary>
    private static string? GetEffectiveProtonPath(string runnerId, GameRuntimeOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ProtonPath))
        {
            return string.Equals(runnerId, "umu", StringComparison.Ordinal) ? "auto" : null;
        }

        return options.ProtonPath;
    }

    private static GameRuntimeDiagnosticSnapshot BuildDiagnostic(
        GameRunnerDefinition? runner,
        GameRunnerAvailability? availability,
        GameLaunchRequest request,
        GameRuntimeOptions options) =>
        new(
            RunnerId: runner?.Id ?? "",
            RunnerVersion: availability?.Version,
            RunnerExecutable: availability?.ExecutablePath,
            PrefixPath: runner is null ? null : GetEffectivePrefixPath(request, runner.Id, options),
            ProtonPath: runner is null ? null : GetEffectiveProtonPath(runner.Id, options),
            GameId: request.GameId,
            GameExecutable: request.ExecutablePath,
            WorkingDirectory: request.WorkingDirectory);
}
