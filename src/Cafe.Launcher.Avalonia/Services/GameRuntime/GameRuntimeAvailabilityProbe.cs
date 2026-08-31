using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>Shared executable discovery and health-probe flow for compatibility runners.</summary>
internal static class GameRuntimeAvailabilityProbe
{
    public static async Task<GameRunnerAvailability> CheckAsync(
        bool isSupportedPlatform,
        string runtimeName,
        string requiredPlatform,
        string executableName,
        string? configuredPath,
        Func<string?, string?> locateExecutable,
        Func<string, CancellationToken, Task<RuntimeProbeResult>> probeVersion,
        CancellationToken cancellationToken)
    {
        if (!isSupportedPlatform)
        {
            return new GameRunnerAvailability(
                GameRunnerAvailabilityStatus.Unsupported,
                Message: $"{runtimeName} requires {requiredPlatform}.");
        }

        var executablePath = locateExecutable(configuredPath);
        if (executablePath is null)
        {
            return new GameRunnerAvailability(
                GameRunnerAvailabilityStatus.NotFound,
                Message: configuredPath is null
                    ? $"{executableName} was not found on PATH."
                    : $"{executableName} was not found at the configured path: {configuredPath}");
        }

        var probeResult = await probeVersion(executablePath, cancellationToken).ConfigureAwait(false);
        if (!probeResult.Succeeded || string.IsNullOrWhiteSpace(probeResult.Version))
        {
            return new GameRunnerAvailability(
                GameRunnerAvailabilityStatus.Broken,
                ExecutablePath: executablePath,
                Message: $"{executableName} exists but did not respond to its version probe.",
                TechnicalDetail: probeResult.Describe(executablePath, "--version"));
        }

        return new GameRunnerAvailability(
            GameRunnerAvailabilityStatus.Available,
            Version: probeResult.Version,
            ExecutablePath: executablePath);
    }
}
