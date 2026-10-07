using Cafe.Launcher.Testing;
using Cafe.Launcher.Updater.Core;

namespace Cafe.Launcher.Tests;

public sealed class UpdateApplyPlanTests
{
    /// <summary>改名前的启动器文件名（v1.1.0 及更早）——只作为 helper 命令行输入出现在测试里。</summary>
    private const string LegacyLauncherExeName = "Cafe.Launcher.Avalonia.exe";

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
        var path = UpdateApplyPlan.ExecutablePath("/opt/app", "Cafe.Launcher.exe");

        Assert.Equal(Path.Combine("/opt/app", "Cafe.Launcher.exe"), path);
    }

    [Fact]
    public void ResolveLauncherExecutableName_PrefersTheNameTheHelperWasStartedWith()
    {
        using var directory = TestDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, LegacyLauncherExeName), "legacy");
        File.WriteAllText(Path.Combine(directory.Path, UpdateApplyPlan.LauncherExecutableName), "current");

        var resolved = UpdateApplyPlan.ResolveLauncherExecutableName(directory.Path, LegacyLauncherExeName);

        // 同一目录里两个 exe 并存时以「helper 被下发的名字」为准：那是这次安装实际在跑的启动器。
        Assert.Equal(LegacyLauncherExeName, resolved);
    }

    [Fact]
    public void ResolveLauncherExecutableName_FallsBackToTheProductTokenNameAfterARename()
    {
        // v1.1.0 → v1.1.1（issue #23）：helper 由旧版启动，命令行里带的是改名前的名字，
        // 而安装目录与安装包只剩产品 token 那个名字。
        using var directory = TestDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, UpdateApplyPlan.LauncherExecutableName), "current");

        var resolved = UpdateApplyPlan.ResolveLauncherExecutableName(directory.Path, LegacyLauncherExeName);

        Assert.Equal(UpdateApplyPlan.LauncherExecutableName, resolved);
    }

    [Fact]
    public void ResolveLauncherExecutableName_ReturnsNullWhenNeitherNameExists()
    {
        using var directory = TestDirectory.Create();

        var resolved = UpdateApplyPlan.ResolveLauncherExecutableName(directory.Path, LegacyLauncherExeName);

        // 没有候选就返回 null：应用流程不许猜一个没人指定过的目标。
        Assert.Null(resolved);
    }
}
