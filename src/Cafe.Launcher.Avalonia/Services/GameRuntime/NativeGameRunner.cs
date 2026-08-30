using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>Executes the game executable directly on the host operating system (Windows native path).</summary>
public sealed class NativeGameRunner : IGameRunner
{
    private readonly IProcessLauncher processLauncher;

    public NativeGameRunner(IProcessLauncher processLauncher)
    {
        this.processLauncher = processLauncher;
    }

    public string Id => "native";

    public bool IsSupportedPlatform => OperatingSystem.IsWindows();

    public Task<GameRunnerAvailability> CheckAvailabilityAsync(
        GameRuntimeOptions options,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new GameRunnerAvailability(
            IsSupportedPlatform
                ? GameRunnerAvailabilityStatus.Available
                : GameRunnerAvailabilityStatus.Unsupported,
            Message: IsSupportedPlatform ? null : "Native execution requires Windows."));

    public Task<GameProcess> StartAsync(
        GameLaunchRequest request,
        GameRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupportedPlatform)
        {
            throw new InvalidOperationException("Native game execution is only supported on Windows.");
        }

        var process = processLauncher.Start(BuildStartInfo(request))
            ?? throw new InvalidOperationException("Failed to start game.");

        return Task.FromResult(new GameProcess(process, Id));
    }

    public string? GetEffectivePrefixPath(GameLaunchRequest request, GameRuntimeOptions options) => null;

    public string? GetEffectiveProtonPath(GameRuntimeOptions options) => null;

    /// <summary>Exposed for tests: verifies launch construction without spawning a process.</summary>
    internal ProcessStartInfo BuildStartInfo(GameLaunchRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
