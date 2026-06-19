using System;
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
