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

    public WineGameRunner(IProcessLauncher processLauncher)
        : this(
            processLauncher,
            OperatingSystem.IsLinux,
            explicitPath => ExecutableLocator.FindInPath(WineExecutableName, explicitPath))
    {
    }

    internal WineGameRunner(
        IProcessLauncher processLauncher,
        Func<bool> isSupportedPlatform,
        Func<string?, string?> locateExecutable)
    {
        this.processLauncher = processLauncher;
        this.isSupportedPlatform = isSupportedPlatform;
        this.locateExecutable = locateExecutable;
    }

    public string Id => "wine";

    public bool IsSupportedPlatform => isSupportedPlatform();

    public Task<GameRunnerAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupportedPlatform)
        {
            return Task.FromResult(new GameRunnerAvailability(
                Available: false,
                Message: "Wine requires Linux."));
        }

        var executablePath = locateExecutable(null);
        return Task.FromResult(executablePath is null
            ? new GameRunnerAvailability(
                Available: false,
                Message: $"{WineExecutableName} was not found on PATH.")
            : new GameRunnerAvailability(
                Available: true,
                ExecutablePath: executablePath));
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

        startInfo.Environment["WINEPREFIX"] =
            string.IsNullOrWhiteSpace(options.PrefixPath)
                ? GameCompatibilityPaths.GetDefaultPrefixPath(request.GameId)
                : options.PrefixPath;

        return startInfo;
    }
}
