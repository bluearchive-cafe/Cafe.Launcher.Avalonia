using Cafe.Launcher.Core;

namespace Cafe.Launcher.Constants;

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
}
