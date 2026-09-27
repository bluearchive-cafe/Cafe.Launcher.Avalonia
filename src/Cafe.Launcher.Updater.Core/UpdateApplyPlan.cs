using System.IO;

namespace Cafe.Launcher.Updater;

/// <summary>
/// Pure path planning for the portable apply step. The staging and backup directories
/// must live on the same volume as the installation directory (they are siblings), because
/// directory rename/move is how the swap stays atomic-ish and recoverable.
/// </summary>
public static class UpdateApplyPlan
{
    /// <summary>Sibling directory the new package is extracted into before the swap.</summary>
    public static string StagingDirectory(string installDirectory, int parentProcessId) =>
        TrimTrailingSeparator(installDirectory) + $".update-{parentProcessId}";

    /// <summary>Sibling directory the old installation is renamed to before the swap.</summary>
    public static string BackupDirectory(string installDirectory, int parentProcessId) =>
        TrimTrailingSeparator(installDirectory) + $".backup-{parentProcessId}";

    /// <summary>Absolute path of the executable to launch after applying.</summary>
    public static string ExecutablePath(string installDirectory, string executableName) =>
        Path.Combine(installDirectory, executableName);

    private static string TrimTrailingSeparator(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ? path : trimmed;
    }
}
