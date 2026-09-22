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
    private readonly RunnerOutputCapture? runnerOutputCapture;
    private readonly CompatibilityEnvironmentPrecheck? environmentPrecheck;

    public GameRuntime(
        IEnumerable<GameRunnerDefinition> runners,
        IProcessLauncher processLauncher,
        IGameProcessTracker processTracker,
        RunnerOutputCapture? runnerOutputCapture = null,
        CompatibilityEnvironmentPrecheck? environmentPrecheck = null)
        : this(
            runners,
            processLauncher,
            processTracker,
            (name, explicitPath) => ExecutableLocator.FindInPath(name, explicitPath),
            RuntimeVersionProbe.ProbeAsync,
            runnerOutputCapture,
            environmentPrecheck)
    {
    }

    internal GameRuntime(
        IEnumerable<GameRunnerDefinition> runners,
        IProcessLauncher processLauncher,
        IGameProcessTracker processTracker,
        Func<string, string?, string?> locateExecutable,
        Func<string, string, TimeSpan, CancellationToken, Task<RuntimeProbeResult>> probeVersion,
        RunnerOutputCapture? runnerOutputCapture = null,
        CompatibilityEnvironmentPrecheck? environmentPrecheck = null)
    {
        this.runners = runners.ToArray();
        this.processLauncher = processLauncher;
        this.processTracker = processTracker;
        this.locateExecutable = locateExecutable;
        this.probeVersion = probeVersion;
        this.runnerOutputCapture = runnerOutputCapture;
        this.environmentPrecheck = environmentPrecheck;
    }

    public async Task<GameRuntimeLaunchResult> LaunchAsync(
        GameLaunchRequest request,
        GameRuntimeConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var candidates = new List<GameRuntimeStatusEntry>();
        foreach (var runner in SelectionOrder(configuration.PreferredRunnerId))
        {
            var runnerConfiguration = ForSelectedRunner(configuration, runner.Id);
            GameRunnerAvailability availability;
            try
            {
                availability = await CheckAvailabilityAsync(runner, runnerConfiguration, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A launch boundary returns a diagnostic result rather than
                // throwing availability failures through the UI pipeline.
                return new GameRuntimeLaunchResult(
                    Success: false,
                    RunnerId: null,
                    Process: null,
                    Diagnostic: BuildDiagnostic(null, null, request, configuration),
                    Candidates: candidates,
                    Failure: GameRuntimeLaunchFailure.AvailabilityCheckFailed,
                    FailureException: exception);
            }

            candidates.Add(new GameRuntimeStatusEntry(runner.Id, availability));

            // Auto selection deliberately stops at the first usable runner.
            // A broken fallback must not block a launch that UMU/native can run.
            if (!runner.IsSupportedPlatform || !availability.Available)
            {
                continue;
            }

            TryEnvironmentPrecheck(runner, request, runnerConfiguration);

            GameProcess process;
            try
            {
                process = Start(runner, request, runnerConfiguration);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new GameRuntimeLaunchResult(
                    Success: false,
                    RunnerId: runner.Id,
                    Process: null,
                    Diagnostic: BuildDiagnostic(runner, availability, request, configuration),
                    Candidates: candidates,
                    Failure: GameRuntimeLaunchFailure.StartFailed,
                    FailureException: exception);
            }

            processTracker.Register(process);
            return new GameRuntimeLaunchResult(
                Success: true,
                RunnerId: runner.Id,
                Process: process,
                Diagnostic: BuildDiagnostic(runner, availability, request, configuration),
                Candidates: candidates);
        }

        return new GameRuntimeLaunchResult(
            Success: false,
            RunnerId: null,
            Process: null,
            Diagnostic: BuildDiagnostic(null, null, request, configuration),
            Candidates: candidates,
            Failure: GameRuntimeLaunchFailure.NoRunnerSelected);
    }

    public async Task<IReadOnlyList<GameRuntimeStatusEntry>> GetStatusesAsync(
        GameRuntimeConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var statuses = await CollectStatusesAsync(configuration, cancellationToken)
            .ConfigureAwait(false);
        return statuses
            .Select(status => new GameRuntimeStatusEntry(status.Runner.Id, status.Availability))
            .ToArray();
    }

    private async Task<IReadOnlyList<(GameRunnerDefinition Runner, GameRunnerAvailability Availability)>> CollectStatusesAsync(
        GameRuntimeConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var statuses = new List<(GameRunnerDefinition, GameRunnerAvailability)>(runners.Count);
        // Status rows always represent every registered runner. The preferred
        // runner only scopes a configured custom path; it does not hide the
        // fallback status rows from the settings surface.
        foreach (var runner in runners)
        {
            var runnerConfiguration = ForSelectedRunner(configuration, runner.Id);
            var availability = await CheckAvailabilityAsync(runner, runnerConfiguration, cancellationToken)
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
    private static GameRuntimeConfiguration ForSelectedRunner(
        GameRuntimeConfiguration configuration,
        string runnerId) =>
        !string.IsNullOrWhiteSpace(configuration.PreferredRunnerId)
        && string.Equals(configuration.PreferredRunnerId, runnerId, StringComparison.OrdinalIgnoreCase)
            ? configuration
            : configuration with { RunnerPath = null };

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
        GameRuntimeConfiguration configuration,
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

        var executablePath = locateExecutable(runner.ExecutableName, configuration.RunnerPath);
        if (executablePath is null)
        {
            return new GameRunnerAvailability(
                GameRunnerAvailabilityStatus.NotFound,
                Message: configuration.RunnerPath is null
                    ? $"{runner.ExecutableName} was not found on PATH."
                    : $"{runner.ExecutableName} was not found at the configured path: {configuration.RunnerPath}");
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
        GameRuntimeConfiguration configuration)
    {
        var executable = runner.ExecutableName is null
            ? request.ExecutablePath
            : locateExecutable(runner.ExecutableName, configuration.RunnerPath)
                ?? throw new InvalidOperationException(
                    $"{runner.ExecutableName} was not found. Install {runner.DisplayName} or configure its path.");

        var startInfo = BuildStartInfo(runner, executable, request, configuration);
        var process = processLauncher.Start(startInfo)
            ?? throw new InvalidOperationException(StartFailureMessage(runner));

        // Drain the redirected pipes immediately: an unread pipe would block the game once its
        // buffer fills. Only Wine/UMU launches redirect (see BuildStartInfo).
        if (startInfo.RedirectStandardOutput)
        {
            runnerOutputCapture?.Begin(process.StandardOutput, process.StandardError);
        }

        return new GameProcess(process, runner.Id);
    }

    /// <summary>
    /// 记录兼容前缀的启动前环境预检（P1-D），但不改变启动结果：报告没写成或探针读不到不能因此
    /// 拒绝一次本可成功的启动。原生 Windows 启动没有前缀，跳过。
    /// </summary>
    private void TryEnvironmentPrecheck(
        GameRunnerDefinition runner,
        GameLaunchRequest request,
        GameRuntimeConfiguration configuration)
    {
        if (environmentPrecheck is null || runner.EnvironmentStyle == GameRuntimeEnvironmentStyle.Native)
        {
            return;
        }

        try
        {
            environmentPrecheck.Check(GetEffectivePrefixPath(request, runner.Id, configuration));
        }
        catch (Exception)
        {
            // Diagnostic only: never let the precheck change the launch outcome.
        }
    }

    private static ProcessStartInfo BuildStartInfo(
        GameRunnerDefinition runner,
        string executable,
        GameLaunchRequest request,
        GameRuntimeConfiguration configuration)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false
        };

        // Wine/UMU output is the only place a first-run failure explains itself; native Windows
        // launches stay untouched. UseShellExecute is already false, so redirecting is safe.
        var captureOutput = runner.EnvironmentStyle != GameRuntimeEnvironmentStyle.Native;
        startInfo.RedirectStandardOutput = captureOutput;
        startInfo.RedirectStandardError = captureOutput;

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
            case GameRuntimeEnvironmentStyle.Native:
                break;
            case GameRuntimeEnvironmentStyle.Wine:
                startInfo.Environment["WINEPREFIX"] = GetEffectivePrefixPath(request, runner.Id, configuration)!;
                break;
            case GameRuntimeEnvironmentStyle.Umu:
                startInfo.Environment["GAMEID"] = request.GameId;
                startInfo.Environment["WINEPREFIX"] = GetEffectivePrefixPath(request, runner.Id, configuration)!;
                if (!string.IsNullOrWhiteSpace(configuration.ProtonPath))
                {
                    startInfo.Environment["PROTONPATH"] = configuration.ProtonPath;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(runner), runner.EnvironmentStyle, null);
        }

        return startInfo;
    }

    private static string StartFailureMessage(GameRunnerDefinition runner) =>
        runner.EnvironmentStyle == GameRuntimeEnvironmentStyle.Native
            ? "Failed to start game."
            : $"Failed to start {runner.DisplayName}.";

    /// <summary>
    /// The compatibility prefix a launch targets: the configured path when set,
    /// otherwise the runner-managed default isolated per game and runner.
    /// </summary>
    private static string GetEffectivePrefixPath(GameLaunchRequest request, string runnerId, GameRuntimeConfiguration configuration) =>
        string.IsNullOrWhiteSpace(configuration.PrefixPath)
            ? GameCompatibilityPaths.GetDefaultPrefixPath(request.GameId, runnerId)
            : configuration.PrefixPath;

    /// <summary>
    /// The Proton build a launch targets; UMU reports "auto" when it selects a
    /// build itself so the effective choice is never blank in diagnostics.
    /// </summary>
    private static string? GetEffectiveProtonPath(string runnerId, GameRuntimeConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.ProtonPath))
        {
            return string.Equals(runnerId, "umu", StringComparison.Ordinal) ? "auto" : null;
        }

        return configuration.ProtonPath;
    }

    private static GameRuntimeDiagnosticSnapshot BuildDiagnostic(
        GameRunnerDefinition? runner,
        GameRunnerAvailability? availability,
        GameLaunchRequest request,
        GameRuntimeConfiguration configuration) =>
        new(
            RunnerId: runner?.Id ?? "",
            RunnerVersion: availability?.Version,
            RunnerExecutable: availability?.ExecutablePath,
            PrefixPath: runner is null ? null : GetEffectivePrefixPath(request, runner.Id, configuration),
            ProtonPath: runner is null ? null : GetEffectiveProtonPath(runner.Id, configuration),
            GameId: request.GameId,
            GameExecutable: request.ExecutablePath,
            WorkingDirectory: request.WorkingDirectory);
}
