using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services.GameRuntime;

namespace Cafe.Launcher.UI.Services;

/// <summary>
/// Resolves a per-user game data path, either directly on Windows or inside the
/// configured Wine/UMU prefix on Linux. The caller supplies the same relative
/// segments the game uses under the Windows user profile.
/// </summary>
internal static class GameUserDataPathResolver
{
    public static string ResolveWindows(IReadOnlyList<string> relativeSegments) =>
        Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), relativeSegments);

    public static string ResolveLinux(
        GameRuntimeSettings settings,
        string userName,
        Func<string, string> defaultPrefix,
        IReadOnlyList<string> relativeSegments)
    {
        if (relativeSegments.Count == 0)
        {
            throw new NotSupportedException("No user data path is defined for this game.");
        }

        string[] runners = settings.Runner == GameRuntimeRunners.Wine
            ? [GameRuntimeRunners.Wine]
            : settings.Runner == GameRuntimeRunners.Umu
                ? [GameRuntimeRunners.Umu]
                : [GameRuntimeRunners.Umu, GameRuntimeRunners.Wine];
        string[] prefixes = !string.IsNullOrWhiteSpace(settings.PrefixPath)
            ? [settings.PrefixPath]
            : runners.Select(defaultPrefix).ToArray();
        var fallback = Combine(Path.Combine(prefixes[0], "drive_c", "users", userName), relativeSegments);
        foreach (var prefix in prefixes)
        {
            var users = Path.Combine(prefix, "drive_c", "users");
            var currentUserPath = Combine(Path.Combine(users, userName), relativeSegments);
            if (File.Exists(currentUserPath))
            {
                return currentUserPath;
            }

            if (!Directory.Exists(users))
            {
                continue;
            }

            foreach (var profile in Directory.EnumerateDirectories(users).Order(StringComparer.Ordinal))
            {
                var path = Combine(profile, relativeSegments);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return fallback;
    }

    private static string Combine(string profile, IReadOnlyList<string> relativeSegments) =>
        Path.Combine([profile, .. relativeSegments]);
}
