using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Launches Windows game executables on Linux through umu-run, which provides
/// a standardized Proton execution environment for launchers outside Steam.
/// Experimental: game compatibility (XIGNCODE3) must be verified independently
/// of runner availability.
/// </summary>
public sealed class UmuGameRunner : IGameRunner
{
    private const string UmuExecutableName = "umu-run";

    private readonly IProcessLauncher processLauncher;
    private readonly Func<bool> isSupportedPlatform;
    private readonly Func<string?, string?> locateExecutable;
    private readonly Func<string, CancellationToken, Task<string?>> probeVersion;

    public UmuGameRunner(IProcessLauncher processLauncher)
        : this(
            processLauncher,
            OperatingSystem.IsLinux,
            explicitPath => ExecutableLocator.FindInPath(UmuExecutableName, explicitPath),
            ProbeVersion)
    {
    }

    internal UmuGameRunner(
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

    public string Id => "umu";

    public bool IsSupportedPlatform => isSupportedPlatform();

    public async Task<GameRunnerAvailability> CheckAvailabilityAsync(
        GameRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupportedPlatform)
        {
            return new GameRunnerAvailability(
                GameRunnerAvailabilityStatus.Unsupported,
                Message: "UMU requires Linux.");
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
                    ? $"{UmuExecutableName} was not found on PATH."
                    : $"{UmuExecutableName} was not found at the configured path: {options.RunnerPath}");
        }

        // Finding the file proves nothing about the runtime working; run its version
        // command so broken installations surface before an actual launch attempt.
        var version = await probeVersion(executablePath, cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            return new GameRunnerAvailability(
                GameRunnerAvailabilityStatus.Broken,
                ExecutablePath: executablePath,
                Message: $"{UmuExecutableName} exists but did not respond to its version probe.",
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
            throw new InvalidOperationException("UMU game execution is only supported on Linux.");
        }

        var umuExecutable = locateExecutable(options.RunnerPath)
            ?? throw new InvalidOperationException(
                $"{UmuExecutableName} was not found. Install UMU or configure its path.");

        var process = processLauncher.Start(BuildStartInfo(umuExecutable, request, options))
            ?? throw new InvalidOperationException("Failed to start UMU.");

        return Task.FromResult(new GameProcess(process, Id));
    }

    /// <summary>Exposed for tests: verifies launch construction without spawning a process.</summary>
    internal ProcessStartInfo BuildStartInfo(
        string umuExecutable,
        GameLaunchRequest request,
        GameRuntimeOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = umuExecutable,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(request.ExecutablePath);
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GAMEID"] = request.GameId;

        // A launcher-managed prefix keeps game files, manifests, and the
        // compatibility environment decoupled even when the user did not configure one.
        startInfo.Environment["WINEPREFIX"] = GetEffectivePrefixPath(request, options)!;

        if (!string.IsNullOrWhiteSpace(options.ProtonPath))
        {
            startInfo.Environment["PROTONPATH"] = options.ProtonPath;
        }

        return startInfo;
    }

    public string? GetEffectivePrefixPath(GameLaunchRequest request, GameRuntimeOptions options) =>
        string.IsNullOrWhiteSpace(options.PrefixPath)
            ? GameCompatibilityPaths.GetDefaultPrefixPath(request.GameId, Id)
            : options.PrefixPath;

    public string? GetEffectiveProtonPath(GameRuntimeOptions options) =>
        // UMU selects a Proton build itself when none is configured; diagnostics
        // surface that as "auto" so the effective choice is never reported blank.
        string.IsNullOrWhiteSpace(options.ProtonPath) ? "auto" : options.ProtonPath;

    private static Task<string?> ProbeVersion(string executablePath, CancellationToken cancellationToken) =>
        RuntimeVersionProbe.ProbeAsync(executablePath, "--version", RuntimeVersionProbe.DefaultTimeout, cancellationToken);
}
