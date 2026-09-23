using Cafe.Launcher.Updater;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class UpdateApplyPlanTests
{
    [Theory]
    [InlineData("/opt/app")]
    [InlineData("/opt/app/")]
    public void StagingDirectory_IsASiblingWithoutTrailingSeparators(string installDirectory)
    {
        var staging = UpdateApplyPlan.StagingDirectory(installDirectory, 1234);

        Assert.Equal("/opt/app.update-1234", staging);
    }

    [Theory]
    [InlineData("/opt/app")]
    [InlineData("/opt/app/")]
    public void BackupDirectory_IsASiblingWithoutTrailingSeparators(string installDirectory)
    {
        var backup = UpdateApplyPlan.BackupDirectory(installDirectory, 1234);

        Assert.Equal("/opt/app.backup-1234", backup);
    }

    [Fact]
    public void ExecutablePath_CombinesInstallDirectoryAndName()
    {
        var path = UpdateApplyPlan.ExecutablePath("/opt/app", "Cafe.Launcher.Avalonia.exe");

        Assert.Equal(Path.Combine("/opt/app", "Cafe.Launcher.Avalonia.exe"), path);
    }
}
