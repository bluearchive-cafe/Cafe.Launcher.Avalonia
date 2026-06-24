using System;
using System.IO;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class GameInstallationPath
{
    public string GetDefaultGamePath()
    {
        // Match the official launcher (path.dirname(app.getPath("exe"))): default the game
        // location to the launcher's own directory so both launchers resolve the same path
        // and don't install the game twice in different places.
        return NormalizeGamePath(AppContext.BaseDirectory);
    }

    public string NormalizeGamePath(string path)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var segments = normalized.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        if (EndsWithSegments(segments, [GamePaths.RootFolderName, GamePaths.GameFolderName]))
        {
            return normalized;
        }

        if (EndsWithSegments(segments, [GamePaths.RootFolderName]))
        {
            return Path.Combine(normalized, GamePaths.GameFolderName);
        }

        return Path.Combine(normalized, GamePaths.RootFolderName, GamePaths.GameFolderName);
    }

    private static bool EndsWithSegments(string[] value, string[] suffix)
    {
        if (value.Length < suffix.Length)
        {
            return false;
        }

        var offset = value.Length - suffix.Length;
        for (var i = 0; i < suffix.Length; i++)
        {
            if (!string.Equals(value[offset + i], suffix[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
