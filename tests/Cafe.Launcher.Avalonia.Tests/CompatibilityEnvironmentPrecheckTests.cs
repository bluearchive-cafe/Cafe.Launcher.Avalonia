using System;
using System.IO;
using Cafe.Launcher.Avalonia.Services.GameRuntime;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class CompatibilityEnvironmentPrecheckTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();

    [Fact]
    public void IsNoExecMount_PicksTheMostSpecificMountAndReadsItsOptions()
    {
        Assert.SkipUnless(
            OperatingSystem.IsLinux(),
            "Mount-table matching follows the Unix path separator semantics.");

        const string mounts =
            "/dev/sda1 / ext4 rw,relatime 0 0\n"
            + "/dev/sdb1 /home ext4 rw,noexec 0 0\n"
            + "tmpfs /home/user/ram tmpfs rw,noexec 0 0\n";

        Assert.True(CompatibilityEnvironmentPrecheck.IsNoExecMount(mounts, "/home/user/game/prefix"));
        Assert.True(CompatibilityEnvironmentPrecheck.IsNoExecMount(mounts, "/home/user/ram/prefix"));
        Assert.False(CompatibilityEnvironmentPrecheck.IsNoExecMount(mounts, "/opt/game/prefix"));
    }

    [Fact]
    public void IsNoExecMount_UnescapesSpacesInMountPoints()
    {
        Assert.SkipUnless(
            OperatingSystem.IsLinux(),
            "Mount-table matching follows the Unix path separator semantics.");

        const string mounts = "/dev/sda1 /mnt/My\\040Games vfat rw,noexec 0 0\n";

        Assert.True(CompatibilityEnvironmentPrecheck.IsNoExecMount(mounts, "/mnt/My Games/prefix"));
    }

    [Fact]
    public void ParseDistroName_ReadsPrettyNameAndIgnoresOtherFields()
    {
        Assert.Equal(
            "Arch Linux",
            CompatibilityEnvironmentPrecheck.ParseDistroName("NAME=\"Arch\"\nPRETTY_NAME=\"Arch Linux\"\n"));
        Assert.Null(CompatibilityEnvironmentPrecheck.ParseDistroName("NAME=\"Arch\"\n"));
        Assert.Null(CompatibilityEnvironmentPrecheck.ParseDistroName(null));
    }

    [Fact]
    public void NearestExistingDirectory_WalksUpToTheClosestExistingAncestor()
    {
        var nested = Path.Combine(tempDir, "a", "b", "c");

        Assert.Equal(
            Path.GetFullPath(tempDir.Path),
            CompatibilityEnvironmentPrecheck.NearestExistingDirectory(nested));
    }

    [Fact]
    public void SupportsSymlinks_OnLinux_ReturnsTrueForATemporaryDirectory()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Symlink support differs on Windows.");

        Assert.True(CompatibilityEnvironmentPrecheck.SupportsSymlinks(tempDir.Path));
    }

    [Fact]
    public void IsCaseSensitive_OnLinux_ReturnsTrueForATemporaryDirectory()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Case sensitivity differs on Windows and macOS.");

        Assert.True(CompatibilityEnvironmentPrecheck.IsCaseSensitive(tempDir.Path));
    }

    [Fact]
    public void Check_WhenAnAncestorIsAFile_ReportsThePrefixAsNotWritableAndWritesTheReport()
    {
        var blocker = Path.Combine(tempDir, "blocker");
        File.WriteAllText(blocker, "");
        var precheck = new CompatibilityEnvironmentPrecheck(tempDir.DataRoot);

        var report = precheck.Check(Path.Combine(blocker, "prefix"));

        Assert.Contains(
            report.Findings,
            finding => finding.Code == CompatibilityFindingCode.PrefixNotWritable
                && finding.Severity == CompatibilityFindingSeverity.Error);
        Assert.True(File.Exists(precheck.ReportPath));
    }

    [Fact]
    public void Check_OnALinuxWritablePrefix_ReportsNoBlockingFindingsAndRecordsCaseSensitivity()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The symlink/case probes assume a POSIX filesystem.");
        var precheck = new CompatibilityEnvironmentPrecheck(tempDir.DataRoot);

        var report = precheck.Check(Path.Combine(tempDir, "prefix"));

        Assert.DoesNotContain(
            report.Findings,
            finding => finding.Code is CompatibilityFindingCode.PrefixNotWritable
                or CompatibilityFindingCode.SymlinksUnsupported);
        Assert.True(report.CaseSensitive);
        Assert.True(File.Exists(precheck.ReportPath));
    }

    public void Dispose()
    {
        tempDir.Dispose();
    }
}
