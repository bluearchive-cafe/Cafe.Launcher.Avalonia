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
    private readonly Func<string, CancellationToken, Task<RuntimeProbeResult>> probeVersion;

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
        Func<string, CancellationToken, Task<RuntimeProbeResult>> probeVersion)
    {
        this.processLauncher = processLauncher;
        this.isSupportedPlatform = isSupportedPlatform;
        this.locateExecutable = locateExecutable;
        this.probeVersion = probeVersion;
    }

    public string Id => "wine";

    public bool IsSupportedPlatform => isSupportedPlatform();

    public Task<GameRunnerAvailability> CheckAvailabilityAsync(
        GameRuntimeOptions options,
        CancellationToken cancellationToken = default) =>
        GameRuntimeAvailabilityProbe.CheckAsync(
            IsSupportedPlatform,
            "Wine",
            "Linux",
            WineExecutableName,
            options.RunnerPath,
            locateExecutable,
            probeVersion,
            cancellationToken);

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

    private static Task<RuntimeProbeResult> ProbeVersion(string executablePath, CancellationToken cancellationToken) =>
        RuntimeVersionProbe.ProbeAsync(executablePath, "--version", RuntimeVersionProbe.DefaultTimeout, cancellationToken);
}
