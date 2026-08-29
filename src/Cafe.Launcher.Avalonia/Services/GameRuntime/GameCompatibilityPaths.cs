using System;
using System.IO;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Filesystem layout for launcher-managed compatibility environments. Prefixes
/// live outside the game directory so game manifests, repairs, and compatibility
/// state stay decoupled (see the cross-platform launch design, §10).
/// On Windows the launcher-central user data root is used; on Unix the XDG data
/// home is namespaced with "cafe-launcher".
/// </summary>
public static class GameCompatibilityPaths
{
    /// <summary>Root of launcher-managed compatibility data.</summary>
    public static string GetDefaultCompatibilityRoot() =>
        Path.Combine(GetLauncherDataRoot(), "compatibility");

    /// <summary>Default Wine prefix for a game, e.g. &lt;dataRoot&gt;/compatibility/&lt;gameId&gt;/prefix.</summary>
    public static string GetDefaultPrefixPath(string gameId) =>
        Path.Combine(GetDefaultCompatibilityRoot(), gameId, "prefix");

    private static string GetLauncherDataRoot() =>
        OperatingSystem.IsWindows()
            ? LauncherUserDataDirectory.Root
            : Path.Combine(GetUnixDataHome(), "cafe-launcher");

    private static string GetUnixDataHome()
    {
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            return xdgDataHome;
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        return string.IsNullOrWhiteSpace(home)
            ? "."
            : Path.Combine(home, ".local", "share");
    }
}
