using System;
using System.IO;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// Shared path-safety validation used by GameDownloadService and GameUninstallService.
/// Ensures constructed file paths never escape the root game directory (path-traversal prevention).
/// </summary>
public static class GamePathValidator
{
    /// <summary>
    /// Resolves a relative file path against the game root directory, throwing
    /// <see cref="InvalidOperationException"/> if the resulting path escapes the root.
    /// </summary>
    public static string GetSafePath(string gameRoot, string relativePath)
    {
        var root = Path.GetFullPath(gameRoot);

        // Strip leading directory separators — Path.Combine treats them as rooted paths
        var sanitized = relativePath.Replace('/', Path.DirectorySeparatorChar)
                                     .TrimStart(Path.DirectorySeparatorChar, '/');

        var target = Path.GetFullPath(Path.Combine(root, sanitized));
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Path escapes game directory: {relativePath}");
        }

        return target;
    }
}
