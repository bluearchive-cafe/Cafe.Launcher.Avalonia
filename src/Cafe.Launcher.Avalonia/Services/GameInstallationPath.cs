using System;
using System.IO;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class GameInstallationPath
{
    public string GetDefaultGamePath()
    {
        return GetDefaultGamePath(
            AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable("APPIMAGE"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    internal string GetDefaultGamePath(
        string applicationBaseDirectory,
        string? appImagePath,
        string userProfileDirectory)
    {
        // Match the official launcher's request-default-download-path (index.js:742-743):
        //   checkPath(path.join(path.dirname(app.getPath("exe")), ".."))
        // The game defaults to the launcher's PARENT directory so it sits sibling to
        // (not inside) the launcher folder, keeping it safe from launcher self-update
        // overwrites. NormalizeGamePath is a faithful port of the official checkPath,
        // so the only requirement is feeding it the same base — the parent directory.
        // AppImage is the exception: AppContext.BaseDirectory lives under APPDIR,
        // which is a temporary read-only mount. Use the user's home directory as a
        // stable writable base instead of suggesting a path that cannot be created.
        var baseDirectory = !string.IsNullOrWhiteSpace(appImagePath) &&
            !string.IsNullOrWhiteSpace(userProfileDirectory)
            ? userProfileDirectory
            : Path.Combine(applicationBaseDirectory, "..");
        return NormalizeGamePath(baseDirectory);
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
