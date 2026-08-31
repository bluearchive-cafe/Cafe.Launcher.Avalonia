using System;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>How a runner injects compatibility environment variables into a launch.</summary>
public enum GameRuntimeEnvironmentStyle
{
    /// <summary>The game executable is the host process (Windows native).</summary>
    None,

    /// <summary>Wine: a compatibility prefix is applied (WINEPREFIX).</summary>
    Wine,

    /// <summary>UMU: GAMEID, WINEPREFIX, and an optional PROTONPATH are applied.</summary>
    Umu
}

/// <summary>
/// Declarative description of one runtime environment. The <see cref="GameRuntime"/>
/// module owns every rule (location, probe, environment, prefix/proton decisions),
/// so adding a runner is writing one spec — not another adapter class.
/// </summary>
public sealed record GameRunnerDefinition(
    string Id,
    bool IsSupportedPlatform,
    string RequiredPlatformName,
    string DisplayName,
    string? ExecutableName,
    string VersionArgument,
    GameRuntimeEnvironmentStyle EnvironmentStyle)
{
    /// <summary>Windows: runs the game PE directly; no runtime executable or probe.</summary>
    public static GameRunnerDefinition Native { get; } = new(
        "native",
        OperatingSystem.IsWindows(),
        "Windows",
        "Native execution",
        ExecutableName: null,
        VersionArgument: "",
        GameRuntimeEnvironmentStyle.None);

    /// <summary>Linux: umu-run provides a standardized Proton environment.</summary>
    public static GameRunnerDefinition Umu { get; } = new(
        "umu",
        OperatingSystem.IsLinux(),
        "Linux",
        "UMU",
        "umu-run",
        "--version",
        GameRuntimeEnvironmentStyle.Umu);

    /// <summary>Linux fallback: plain Wine for custom setups or Proton issues.</summary>
    public static GameRunnerDefinition Wine { get; } = new(
        "wine",
        OperatingSystem.IsLinux(),
        "Linux",
        "Wine",
        "wine",
        "--version",
        GameRuntimeEnvironmentStyle.Wine);
}
