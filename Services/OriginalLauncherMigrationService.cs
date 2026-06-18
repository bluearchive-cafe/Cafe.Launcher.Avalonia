using System;
using System.Collections.Generic;
using System.IO;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Reads game installation path from the original Yostar launcher's localStorage
/// (backed by Chromium LevelDB) for first-run migration.
/// </summary>
public static class OriginalLauncherMigrationService
{
    /// <summary>
    /// Attempts to read the game installation path from the original Yostar launcher.
    /// </summary>
    /// <returns>The game path if found and the directory exists on disk; otherwise null.</returns>
    public static string? TryGetGamePath()
    {
        var userDataPath = OldLauncherDetectionService.ResolveOldUserDataPath();
        if (userDataPath is null)
            return null;

        var levelDbPath = Path.Combine(userDataPath, "Local Storage", "leveldb");
        if (!Directory.Exists(levelDbPath))
            return null;

        try
        {
            var values = LevelDbReader.TryReadValues(levelDbPath) ?? new Dictionary<string, string>();
            if (values.TryGetValue("downloadPath", out var gamePath)
                && !string.IsNullOrWhiteSpace(gamePath)
                && Directory.Exists(gamePath))
            {
                return gamePath;
            }
        }
        catch
        {
            // Migration failure is non-fatal — user can set the path manually.
        }

        return null;
    }
}
