using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LauncherConstantsTests
{
    [Fact]
    public void LauncherVersion_UsesApplicationSemVer()
    {
        Assert.Equal("1.0.0", LauncherConstants.LauncherVersion);
    }

    [Fact]
    public void YostarAuthorizationVersion_MatchesOfficialLauncherVersion()
    {
        Assert.Equal("1.7.2", LauncherConstants.YostarAuthorizationVersion);
    }
}
