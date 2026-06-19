using System.Reflection;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LauncherConstantsTests
{
    [Fact]
    public void LauncherVersion_UsesApplicationSemVer()
    {
        var expected = typeof(LauncherConstants).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        Assert.NotNull(expected);
        Assert.Equal(expected, BuildInfo.LauncherVersion);
    }

    [Fact]
    public void CommitSha_UsesSevenCharacterLowercaseGitHash()
    {
        Assert.Matches("^[0-9a-f]{7}$", BuildInfo.CommitSha);
    }

    [Fact]
    public void YostarAuthorizationVersion_MatchesOfficialLauncherVersion()
    {
        Assert.Equal("1.7.2", ApiConfig.YostarAuthorizationVersion);
    }
}
