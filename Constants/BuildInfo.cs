using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Cafe.Launcher.Avalonia.Constants;

/// <summary>
/// Build-time metadata: version, commit SHA, and configuration.
/// </summary>
public static class BuildInfo
{
    public static readonly string LauncherVersion =
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "1.0.0";
    public static readonly string CommitSha =
        typeof(BuildInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "CommitSha")
            ?.Value
        ?? "unknown";

    public static readonly string BuildTime = ResolveBuildTime();

    private static string ResolveBuildTime()
    {
        var raw = typeof(BuildInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "BuildTime")
            ?.Value;
        if (string.IsNullOrWhiteSpace(raw)) return "";

        // The .csproj embeds BuildTime as UTC (yyyy-MM-dd HH:mm).
        // Parse as UTC then convert to local time for display.
        if (DateTime.TryParse(raw, null, out var utcTime))
            return DateTime.SpecifyKind(utcTime, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        return raw;
    }

#if DEBUG
    public const string BuildConfiguration = "Debug";
#else
    public const string BuildConfiguration = "Release";
#endif

    /// <summary>
    /// Keep in sync with .csproj PackageReference for Avalonia.
    /// </summary>
    public const string AvaloniaVersion = "12.0.4";
}
