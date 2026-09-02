using System;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Complete game runtime configuration for launch and availability checks.
/// Auto selection is represented by a null preferred runner; all paths are
/// optional and fall back to the selected runner's platform defaults.
/// </summary>
public sealed record GameRuntimeConfiguration(
    string? PreferredRunnerId = null,
    string? RunnerPath = null,
    string? PrefixPath = null,
    string? ProtonPath = null)
{
    /// <summary>Maps persisted runtime settings to the runtime module's single configuration.</summary>
    public static GameRuntimeConfiguration FromSettings(GameRuntimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new GameRuntimeConfiguration(
            PreferredRunnerId: settings.Runner is GameRuntimeRunners.Auto or "" ? null : settings.Runner,
            RunnerPath: settings.RunnerPath,
            PrefixPath: settings.PrefixPath,
            ProtonPath: settings.ProtonPath);
    }
}
