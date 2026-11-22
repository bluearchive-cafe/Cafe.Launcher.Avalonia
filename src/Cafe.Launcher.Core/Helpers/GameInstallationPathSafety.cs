using System;
using System.IO;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.Core.Helpers;

/// <summary>Rejects shared or nested installation directories without reading or changing files.</summary>
public static class GameInstallationPathSafety
{
    /// <summary>
    /// Compares an operation's fixed destination with every other configured game. Empty paths
    /// are unconfigured, never defaults. Compare effective installation roots, so a legacy base
    /// directory does not accidentally reserve every sibling game installed under that base.
    /// </summary>
    public static GameInstallationPathConflict? FindConflict(string gameId, string gamePath, LauncherSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);
        ArgumentNullException.ThrowIfNull(settings);
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gamePath));
        foreach (var (otherGameId, other) in settings.Games)
        {
            if (string.Equals(gameId, otherGameId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(other.GamePath))
            {
                continue;
            }

            var otherPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(other.GamePath));
            if (LauncherProfiles.TryGetSupportedGame(otherGameId, out var profile) && profile is not null)
            {
                otherPath = new GameInstallationPath(profile).NormalizeGamePath(otherPath);
            }
            if (Overlaps(target, otherPath))
            {
                return new(gameId, otherGameId, target, otherPath);
            }
        }

        return null;
    }

    private static bool Overlaps(string first, string second) =>
        string.Equals(first, second, GamePathValidator.PathComparison)
        || first.StartsWith(WithSeparator(second), GamePathValidator.PathComparison)
        || second.StartsWith(WithSeparator(first), GamePathValidator.PathComparison);

    private static string WithSeparator(string path) => Path.EndsInDirectorySeparator(path)
        ? path : path + Path.DirectorySeparatorChar;
}

/// <summary>Identifies both owners and the concrete conflicting directory pair; contains no UI text.</summary>
public sealed record GameInstallationPathConflict(string GameId, string OtherGameId, string GamePath, string OtherGamePath);

/// <summary>A refused ownership boundary; download retry/cleanup must preserve the existing files.</summary>
public sealed class GameInstallationPathConflictException(GameInstallationPathConflict conflict) : InvalidOperationException
{
    /// <summary>The fixed target and conflicting game directory that caused refusal.</summary>
    public GameInstallationPathConflict Conflict { get; } = conflict;
}
