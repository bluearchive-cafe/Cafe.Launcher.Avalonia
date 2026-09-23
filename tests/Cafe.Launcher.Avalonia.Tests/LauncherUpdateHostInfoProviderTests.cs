using Cafe.Launcher.Avalonia.Services.Update;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LauncherUpdateHostInfoProviderTests
{
    [Fact]
    public void GetHostInfo_OnNonWindowsHost_ReportsNonWindowsAndNotInstaller()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The installed-vs-portable marker only applies to Windows.");

        var info = new LauncherUpdateHostInfoProvider().GetHostInfo();

        Assert.False(info.IsWindows);
        Assert.False(info.IsInstallerInstall);
    }
}
