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
            Environment.GetEnvironmentVariable("CAFE_LAUNCHER_PACKAGE_FORMAT"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    internal string GetDefaultGamePath(
        string applicationBaseDirectory,
        string? appImagePath,
        string? packageFormat,
        string userProfileDirectory)
    {
        // Match the official launcher's request-default-download-path (index.js:742-743):
        //   checkPath(path.join(path.dirname(app.getPath("exe")), ".."))
        // The game defaults to the launcher's PARENT directory so it sits sibling to
        // (not inside) the launcher folder, keeping it safe from launcher self-update
        // overwrites. NormalizeGamePath is a faithful port of the official checkPath,
        // so the only requirement is feeding it the same base — the parent directory.
        // Packaged Linux installs are the exception: AppImage uses a temporary
        // read-only mount, while system packages install under a protected prefix
        // such as /opt. Use the user's home directory instead of suggesting a path
        // that cannot be created without elevated permissions.
        var isPackagedInstall = !string.IsNullOrWhiteSpace(appImagePath)
            || !string.IsNullOrWhiteSpace(packageFormat);
        var baseDirectory = isPackagedInstall &&
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
