using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Launches Windows game executables on Linux through plain Wine. Intended as
/// a fallback for users with a custom Wine setup, or when Proton builds have
/// compatibility issues — auto mode only picks it when umu-run is unavailable.
/// </summary>
public sealed class WineGameRunner : IGameRunner
{
    private const string WineExecutableName = "wine";

    private readonly IProcessLauncher processLauncher;
    private readonly Func<bool> isSupportedPlatform;
    private readonly Func<string?, string?> locateExecutable;
    private readonly Func<string, CancellationToken, Task<string?>> probeVersion;

    public WineGameRunner(IProcessLauncher processLauncher)
        : this(
            processLauncher,
            OperatingSystem.IsLinux,
            explicitPath => ExecutableLocator.FindInPath(WineExecutableName, explicitPath),
            ProbeVersion)
    {
    }

    internal WineGameRunner(
        IProcessLauncher processLauncher,
        Func<bool> isSupportedPlatform,
        Func<string?, string?> locateExecutable,
        Func<string, CancellationToken, Task<string?>> probeVersion)
    {
        this.processLauncher = processLauncher;
        this.isSupportedPlatform = isSupportedPlatform;
        this.locateExecutable = locateExecutable;
        this.probeVersion = probeVersion;
    }

    public string Id => "wine";

    public bool IsSupportedPlatform => isSupportedPlatform();

    public async Task<GameRunnerAvailability> CheckAvailabilityAsync(
        GameRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupportedPlatform)
        {
            return new GameRunnerAvailability(
                GameRunnerAvailabilityStatus.Unsupported,
                Message: "Wine requires Linux.");
        }

        // Availability must honor the configured runner path exactly like StartAsync
        // does: an explicit but invalid path fails here instead of silently falling
        // back to PATH, so resolution never disagrees with the actual launch.
        var executablePath = locateExecutable(options.RunnerPath);
        if (executablePath is null)
        {
            return new GameRunnerAvailability(
                GameRunnerAvailabilityStatus.NotFound,
                Message: options.RunnerPath is null
                    ? $"{WineExecutableName} was not found on PATH."
                    : $"{WineExecutableName} was not found at the configured path: {options.RunnerPath}");
        }

        // Finding the file proves nothing about the runtime working; run its version
        // command so broken installations surface before an actual launch attempt.
        var version = await probeVersion(executablePath, cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            return new GameRunnerAvailability(
                GameRunnerAvailabilityStatus.Broken,
                ExecutablePath: executablePath,
                Message: $"{WineExecutableName} exists but did not respond to its version probe.",
                TechnicalDetail: RuntimeVersionProbe.DescribeProbeFailure(executablePath));
        }

        return new GameRunnerAvailability(
            GameRunnerAvailabilityStatus.Available,
            Version: version,
            ExecutablePath: executablePath);
    }

    public Task<GameProcess> StartAsync(
        GameLaunchRequest request,
        GameRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupportedPlatform)
        {
            throw new InvalidOperationException("Wine game execution is only supported on Linux.");
        }

        var wineExecutable = locateExecutable(options.RunnerPath)
            ?? throw new InvalidOperationException(
                $"{WineExecutableName} was not found. Install Wine or configure its path.");

        var process = processLauncher.Start(BuildStartInfo(wineExecutable, request, options))
            ?? throw new InvalidOperationException("Failed to start Wine.");

        return Task.FromResult(new GameProcess(process, Id));
    }

    /// <summary>Exposed for tests: verifies launch construction without spawning a process.</summary>
    internal ProcessStartInfo BuildStartInfo(
        string wineExecutable,
        GameLaunchRequest request,
        GameRuntimeOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = wineExecutable,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(request.ExecutablePath);
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["WINEPREFIX"] = GetEffectivePrefixPath(request, options)!;

        return startInfo;
    }

    public string? GetEffectivePrefixPath(GameLaunchRequest request, GameRuntimeOptions options) =>
        string.IsNullOrWhiteSpace(options.PrefixPath)
            ? GameCompatibilityPaths.GetDefaultPrefixPath(request.GameId, Id)
            : options.PrefixPath;

    public string? GetEffectiveProtonPath(GameRuntimeOptions options) => null;

    private static Task<string?> ProbeVersion(string executablePath, CancellationToken cancellationToken) =>
        RuntimeVersionProbe.ProbeAsync(executablePath, "--version", RuntimeVersionProbe.DefaultTimeout, cancellationToken);
}
