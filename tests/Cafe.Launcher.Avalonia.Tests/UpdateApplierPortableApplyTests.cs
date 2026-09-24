using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Updater;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Real-directory integration coverage for the helper's portable swap. The backup is
/// only consumed after the new version has actually started, so the suite pins four
/// outcomes: a launchable package cleans up and reports success; a package that cannot
/// start restores the previous version; a package without the launcher executable is
/// rejected before the swap; and a blocked swap leaves the previous version in place.
/// Placeholder text files stand in for executables — starting them fails under Windows
/// ShellExecute, which is exactly the launch-failure being exercised.
/// </summary>
public sealed class UpdateApplierPortableApplyTests
{
    private const string LauncherExeName = "Cafe.Launcher.Avalonia.exe";

    [Fact]
    public async Task ApplyAsync_PortableSwap_WhenTheNewVersionStarts_ReplacesDirectoryAndCleansUp()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "The relaunch outcome of a placeholder executable is only deterministic under Windows ShellExecute.");
        if (!OperatingSystem.IsWindows())
        {
            return; // CA1416 平台守卫：平台分析器不识别 SkipUnless，非 Windows 由上一行汇报跳过。
        }

        using var directory = TestDirectory.Create();
        var install = Path.Combine(directory.Path, "install");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, LauncherExeName), "old launcher");
        File.WriteAllText(Path.Combine(install, "old-marker.txt"), "old");

        // cmd.exe starts deterministically under ShellExecute, so the apply flow reaches
        // the success cleanup. The unique file name doubles as the process name for the
        // leftover console process this test has to dispose of.
        const string startableExeName = "cafe-update-launch-test.exe";
        var content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        File.Copy(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Path.Combine(content, startableExeName));
        File.WriteAllText(Path.Combine(content, "new-marker.txt"), "new");
        var (package, sha256) = await PackAsync(directory.Path, content);

        var parentPid = StartExitedParentProcess();
        var logPath = Path.Combine(directory.Path, "update-apply.log");
        var arguments = new UpdaterArguments(
            UpdateApplyMode.Portable,
            package,
            install,
            startableExeName,
            parentPid,
            sha256,
            logPath);

        var exitCode = await UpdateApplier.ApplyAsync(arguments, CancellationToken.None);
        KillLeftoverProcesses(startableExeName);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(install, "new-marker.txt")));
        Assert.False(File.Exists(Path.Combine(install, "old-marker.txt")));
        Assert.False(Directory.Exists(UpdateApplyPlan.StagingDirectory(install, parentPid)));
        Assert.False(Directory.Exists(UpdateApplyPlan.BackupDirectory(install, parentPid)));
        var log = await File.ReadAllTextAsync(logPath);
        Assert.Contains("Apply started (mode=Portable", log);
        Assert.Contains("Apply completed (mode=Portable, elapsed=", log);
    }

    [Fact]
    public async Task ApplyAsync_PortableSwap_WhenTheNewVersionFailsToStart_RestoresThePreviousVersion()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "The relaunch outcome of a placeholder executable is only deterministic under Windows ShellExecute.");
        if (!OperatingSystem.IsWindows())
        {
            return; // CA1416 平台守卫：平台分析器不识别 SkipUnless，非 Windows 由上一行汇报跳过。
        }

        using var directory = TestDirectory.Create();
        var install = Path.Combine(directory.Path, "install");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, LauncherExeName), "old launcher");
        File.WriteAllText(Path.Combine(install, "old-marker.txt"), "old");

        var content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, LauncherExeName), "not a real executable");
        File.WriteAllText(Path.Combine(content, "new-marker.txt"), "new");
        var (package, sha256) = await PackAsync(directory.Path, content);

        var parentPid = StartExitedParentProcess();
        var logPath = Path.Combine(directory.Path, "update-apply.log");
        var arguments = new UpdaterArguments(
            UpdateApplyMode.Portable,
            package,
            install,
            LauncherExeName,
            parentPid,
            sha256,
            logPath);

        var exitCode = await UpdateApplier.ApplyAsync(arguments, CancellationToken.None);

        Assert.Equal(9, exitCode);
        Assert.Equal("old launcher", await File.ReadAllTextAsync(Path.Combine(install, LauncherExeName)));
        Assert.True(File.Exists(Path.Combine(install, "old-marker.txt")));
        Assert.False(File.Exists(Path.Combine(install, "new-marker.txt")));
        Assert.False(Directory.Exists(UpdateApplyPlan.StagingDirectory(install, parentPid)));
        Assert.False(Directory.Exists(UpdateApplyPlan.BackupDirectory(install, parentPid)));
        var log = await File.ReadAllTextAsync(logPath);
        Assert.Contains("The new version did not start; restoring the previous version.", log);
        Assert.Contains("Restored the previous version.", log);
    }

    [Fact]
    public async Task ApplyAsync_PortableSwap_WhenThePackageLacksTheExecutable_KeepsTheInstallationIntact()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "The relaunch outcome of a placeholder executable is only deterministic under Windows ShellExecute.");
        if (!OperatingSystem.IsWindows())
        {
            return; // CA1416 平台守卫：平台分析器不识别 SkipUnless，非 Windows 由上一行汇报跳过。
        }

        using var directory = TestDirectory.Create();
        var install = Path.Combine(directory.Path, "install");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, LauncherExeName), "old launcher");
        File.WriteAllText(Path.Combine(install, "old-marker.txt"), "old");

        var content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, "new-marker.txt"), "new");
        var (package, sha256) = await PackAsync(directory.Path, content);

        var parentPid = StartExitedParentProcess();
        var logPath = Path.Combine(directory.Path, "update-apply.log");
        var arguments = new UpdaterArguments(
            UpdateApplyMode.Portable,
            package,
            install,
            LauncherExeName,
            parentPid,
            sha256,
            logPath);

        var exitCode = await UpdateApplier.ApplyAsync(arguments, CancellationToken.None);

        Assert.Equal(10, exitCode);
        Assert.Equal("old launcher", await File.ReadAllTextAsync(Path.Combine(install, LauncherExeName)));
        Assert.True(File.Exists(Path.Combine(install, "old-marker.txt")));
        Assert.False(Directory.Exists(UpdateApplyPlan.StagingDirectory(install, parentPid)));
        Assert.False(Directory.Exists(UpdateApplyPlan.BackupDirectory(install, parentPid)));
        var log = await File.ReadAllTextAsync(logPath);
        Assert.Contains($"The package does not contain '{LauncherExeName}'", log);
    }

    [Fact]
    public async Task ApplyAsync_PortableSwap_WhenTheSwapIsBlocked_KeepsThePreviousVersionInPlace()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Directory-move sharing violations are only deterministic under Windows.");
        if (!OperatingSystem.IsWindows())
        {
            return; // CA1416 平台守卫：平台分析器不识别 SkipUnless，非 Windows 由上一行汇报跳过。
        }

        using var directory = TestDirectory.Create();
        var install = Path.Combine(directory.Path, "install");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, LauncherExeName), "old launcher");
        var blockedPath = Path.Combine(install, "old-marker.txt");
        File.WriteAllText(blockedPath, "old");

        var content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, LauncherExeName), "new launcher");
        var (package, sha256) = await PackAsync(directory.Path, content);

        var parentPid = StartExitedParentProcess();
        var logPath = Path.Combine(directory.Path, "update-apply.log");
        var arguments = new UpdaterArguments(
            UpdateApplyMode.Portable,
            package,
            install,
            LauncherExeName,
            parentPid,
            sha256,
            logPath);

        // 独占句柄让 install 目录无法改名，交换在第一半就失败。
        using (new FileStream(blockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var exitCode = await UpdateApplier.ApplyAsync(arguments, CancellationToken.None);

            Assert.Equal(7, exitCode);
        }

        Assert.Equal("old launcher", await File.ReadAllTextAsync(Path.Combine(install, LauncherExeName)));
        Assert.True(File.Exists(Path.Combine(install, "old-marker.txt")));
        Assert.False(Directory.Exists(UpdateApplyPlan.StagingDirectory(install, parentPid)));
        Assert.False(Directory.Exists(UpdateApplyPlan.BackupDirectory(install, parentPid)));
        var log = await File.ReadAllTextAsync(logPath);
        Assert.Contains("Failed to move the current installation aside:", log);
    }

    private static async Task<(string PackagePath, string Sha256)> PackAsync(string directoryPath, string contentDirectory)
    {
        var package = Path.Combine(directoryPath, "package.zip");
        ZipFile.CreateFromDirectory(contentDirectory, package);
        var sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(package)));
        return (package, sha256);
    }

    private static int StartExitedParentProcess()
    {
        using var parent = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
        Assert.NotNull(parent);
        parent.WaitForExit();
        return parent.Id;
    }

    private static void KillLeftoverProcesses(string executableName)
    {
        var processName = Path.GetFileNameWithoutExtension(executableName);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // 进程已经自行退出，无需处理。
                }
            }
        }
    }
}
