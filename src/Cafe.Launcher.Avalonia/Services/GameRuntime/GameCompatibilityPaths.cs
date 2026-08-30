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

    /// <summary>
    /// Default Wine prefix for a game, isolated per runner, e.g.
    /// &lt;dataRoot&gt;/compatibility/&lt;gameId&gt;/&lt;runnerId&gt;/prefix.
    /// UMU and Wine keep separate prefixes so switching runners cannot corrupt the
    /// other environment. Prefixes from the earlier shared layout
    /// (compatibility/&lt;gameId&gt;/prefix) are NOT migrated automatically — moving
    /// compatibility state requires explicit user confirmation, so existing
    /// prefixes stay in place and the new default applies going forward.
    /// </summary>
    public static string GetDefaultPrefixPath(string gameId, string runnerId) =>
        Path.Combine(GetDefaultCompatibilityRoot(), gameId, runnerId, "prefix");

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
