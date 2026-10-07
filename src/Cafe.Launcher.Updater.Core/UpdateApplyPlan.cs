using System.IO;

namespace Cafe.Launcher.Updater.Core;

/// <summary>
/// Pure path planning for the portable apply step. The staging and backup directories
/// must live on the same volume as the installation directory (they are siblings), because
/// directory rename/move is how the swap stays atomic-ish and recoverable.
/// </summary>
public static class UpdateApplyPlan
{
    /// <summary>
    /// Launcher file name the current product token produces. Releases up to v1.1.0 shipped
    /// <c>Cafe.Launcher.Avalonia.exe</c>; v1.1.1 renamed the executable to this name (ADR-043)
    /// while deliberately keeping the Inno <c>AppId</c>, so an installation upgraded across
    /// that boundary can hold both files. ADR-047 makes this the helper's fallback and pins it
    /// against the installer's <c>EXECUTABLE_NAME</c> and the host assembly name.
    /// </summary>
    public const string LauncherExecutableName = "Cafe.Launcher.exe";

    /// <summary>Sibling directory the new package is extracted into before the swap.</summary>
    public static string StagingDirectory(string installDirectory, int parentProcessId) =>
        TrimTrailingSeparator(installDirectory) + $".update-{parentProcessId}";

    /// <summary>Sibling directory the old installation is renamed to before the swap.</summary>
    public static string BackupDirectory(string installDirectory, int parentProcessId) =>
        TrimTrailingSeparator(installDirectory) + $".backup-{parentProcessId}";

    /// <summary>Absolute path of the executable to launch after applying.</summary>
    public static string ExecutablePath(string installDirectory, string executableName) =>
        Path.Combine(installDirectory, executableName);

    /// <summary>
    /// Name of the launcher executable inside <paramref name="directory"/>: the name the helper
    /// was started with when that file is there, otherwise <see cref="LauncherExecutableName"/>.
    /// Returns <c>null</c> when the directory holds neither.
    /// </summary>
    /// <remarks>
    /// The helper that runs an update is always the one shipped with the installed version (it
    /// is copied out of the installation directory before the launcher exits), so its command
    /// line can name an executable the package no longer produces. Falling back to the
    /// product-token name is what lets a rename cross versions; <c>null</c> keeps the apply from
    /// guessing a target nobody named.
    /// </remarks>
    public static string? ResolveLauncherExecutableName(string directory, string preferredExecutableName)
    {
        if (File.Exists(ExecutablePath(directory, preferredExecutableName)))
        {
            return preferredExecutableName;
        }

        return File.Exists(ExecutablePath(directory, LauncherExecutableName))
            ? LauncherExecutableName
            : null;
    }

    private static string TrimTrailingSeparator(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ? path : trimmed;
    }
}
