using Cafe.Launcher.Core;

namespace Cafe.Launcher.Avalonia.Constants;

/// <summary>
/// Build-time metadata: version, commit SHA, and configuration.
/// </summary>
public static class BuildInfo
{
    // BuildInfo still lives in the WinExe during the first extraction stage.
    // Subsequent Core consumers receive this identity from the composition root;
    // using the declaring host assembly keeps test-host loading from reporting
    // vstest's version as the launcher version in the meantime.
    public static readonly LauncherBuildIdentity Identity =
        LauncherBuildIdentity.FromAssembly(typeof(BuildInfo).Assembly);

    public static readonly string LauncherVersion = Identity.LauncherVersion;

    public static readonly string CommitSha = Identity.CommitSha;

    public static readonly string BuildTime = Identity.BuildTime;

    public static readonly string BuildConfiguration = Identity.BuildConfiguration;

    /// <summary>
    /// Avalonia framework version resolved at runtime from the Avalonia assembly.
    /// Falls back to "0.0.0.0" if the runtime value cannot be read.
    /// </summary>
    public static readonly string AvaloniaVersion = ResolveAvaloniaVersion();

    private static string ResolveAvaloniaVersion()
    {
        try
        {
            var assembly = typeof(global::Avalonia.Application).Assembly;
            var version = assembly.GetName().Version;
            return version is not null
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }
}
