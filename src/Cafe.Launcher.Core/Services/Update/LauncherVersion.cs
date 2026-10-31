using System;
using System.Text.RegularExpressions;
using Cafe.Launcher.Core.Helpers;

namespace Cafe.Launcher.Core.Services.Update;

/// <summary>
/// Parsed release version and shared SemVer comparison for ordinary launcher updates.
/// </summary>
internal readonly record struct LauncherVersion(
    string CoreVersion,
    bool IsPrerelease,
    string PrereleaseLabel);

internal static partial class LauncherVersionParser
{
    /// <summary>Parses a bare semantic version; a leading <c>v</c> is rejected.</summary>
    public static bool TryParse(string? value, out LauncherVersion version)
    {
        if (value is null)
        {
            version = default;
            return false;
        }

        var match = SemanticVersionRegex().Match(value);
        if (!match.Success)
        {
            version = default;
            return false;
        }

        version = new LauncherVersion(
            $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}",
            match.Groups[4].Success,
            match.Groups[4].Success ? match.Groups[4].Value : "");
        return true;
    }

    /// <summary>Compares two parsed versions per SemVer 2.0.0 precedence.</summary>
    public static int Compare(LauncherVersion left, LauncherVersion right)
    {
        var coreComparison = VersionComparer.Compare(left.CoreVersion, right.CoreVersion);
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        // A stable release outranks a pre-release of the same core version.
        if (left.IsPrerelease != right.IsPrerelease)
        {
            return left.IsPrerelease ? -1 : 1;
        }

        if (!left.IsPrerelease)
        {
            return 0;
        }

        return VersionComparer.ComparePrerelease(left.PrereleaseLabel, right.PrereleaseLabel);
    }

    [GeneratedRegex(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionRegex();
}
