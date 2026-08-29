using System;
using System.IO;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Locates executables such as umu-run, first honoring an explicitly
/// configured path and otherwise scanning the PATH environment variable.
/// </summary>
public static class ExecutableLocator
{
    /// <summary>
    /// Returns the first existing executable path, or null when nothing matches.
    /// <paramref name="explicitPath"/> wins when set; <paramref name="pathVariable"/>
    /// overrides the PATH environment variable (test seam).
    /// </summary>
    public static string? FindInPath(string fileName, string? explicitPath = null, string? pathVariable = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return File.Exists(explicitPath) ? explicitPath : null;
        }

        var rawPath = pathVariable ?? Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        foreach (var directory in rawPath.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
