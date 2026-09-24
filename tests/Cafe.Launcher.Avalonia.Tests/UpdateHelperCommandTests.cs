using Cafe.Launcher.Avalonia.Services.Update;
using Cafe.Launcher.Updater;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class UpdateHelperCommandTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData(LauncherUpdateTarget.WindowsInstaller, "installer")]
    [InlineData(LauncherUpdateTarget.WindowsPortable, "portable")]
    public void ModeName_MapsWindowsTargets(LauncherUpdateTarget target, string expected)
    {
        Assert.Equal(expected, UpdateHelperCommand.ModeName(target));
    }

    [Theory]
    [InlineData(LauncherUpdateTarget.ExternalDownload)]
    public void ModeName_WhenTargetHasNoApplyMode_Throws(LauncherUpdateTarget target)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UpdateHelperCommand.ModeName(target));
    }

    [Theory]
    [InlineData(LauncherUpdateTarget.WindowsInstaller)]
    [InlineData(LauncherUpdateTarget.WindowsPortable)]
    public void BuildArguments_RoundTripsThroughTheHelperParser(LauncherUpdateTarget target)
    {
        var arguments = UpdateHelperCommand.BuildArguments(
            target,
            packagePath: "/opt/downloads/pkg",
            installDirectory: "/opt/app",
            executableName: "Cafe.Launcher.Avalonia.exe",
            parentProcessId: 4242,
            expectedSha256: Sha,
            logPath: "/opt/data/update-apply.log");

        Assert.True(UpdaterArguments.TryParse(arguments, out var parsed, out var error), error);
        Assert.NotNull(parsed);
        Assert.Equal(UpdateHelperCommand.ModeName(target), parsed!.Mode.ToString().ToLowerInvariant());
        Assert.Equal("/opt/downloads/pkg", parsed.PackagePath);
        Assert.Equal("/opt/app", parsed.InstallDirectory);
        Assert.Equal("Cafe.Launcher.Avalonia.exe", parsed.ExecutableName);
        Assert.Equal(4242, parsed.ParentProcessId);
        Assert.Equal(Sha, parsed.ExpectedSha256);
        Assert.Equal("/opt/data/update-apply.log", parsed.LogPath);
    }
}
