using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Cafe.Launcher.Core;

/// <summary>
/// Immutable identity of the shipped launcher host. The host creates it from
/// its own assembly and passes it into modules, so a class library never
/// accidentally reports its implementation assembly's version.
/// </summary>
public sealed record LauncherBuildIdentity(
    string LauncherVersion,
    string CommitSha,
    string BuildTime,
    string BuildConfiguration)
{
    /// <summary>
    /// Whether this build is a pre-release (SemVer hyphen suffix). The single place that
    /// decides "am I a beta build"; defaults that depend on the release channel consume this
    /// instead of reflecting over the entry assembly.
    /// </summary>
    public bool IsPrerelease => LauncherVersion.Contains('-', StringComparison.Ordinal);

    public static LauncherBuildIdentity FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "1.0.0";
        var commitSha = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "CommitSha")
            ?.Value
            ?? "unknown";
        var rawBuildTime = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "BuildTime")
            ?.Value;

        return new LauncherBuildIdentity(
            version,
            commitSha,
            FormatBuildTime(rawBuildTime),
            ResolveBuildConfiguration());
    }

    private static string FormatBuildTime(string? rawBuildTime)
    {
        if (string.IsNullOrWhiteSpace(rawBuildTime))
        {
            return "";
        }

        return DateTimeOffset.TryParse(rawBuildTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var commitTime)
            ? commitTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : rawBuildTime;
    }

    private static string ResolveBuildConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }
}
