using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Updater;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Real-directory integration coverage for the helper's portable swap: extract, backup,
/// swap, cleanup, and the apply log. The relaunch step fails by design here — the package
/// carries a placeholder executable — so exit code 9 pins "swap succeeded, restart failed";
/// the directory and log assertions describe the swap itself.
/// </summary>
public sealed class UpdateApplierPortableApplyTests
{
    [Fact]
    public async Task ApplyAsync_PortableSwap_ReplacesDirectoryAndLogsCompletion()
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
        File.WriteAllText(Path.Combine(install, "Cafe.Launcher.Avalonia.exe"), "old launcher");
        File.WriteAllText(Path.Combine(install, "old-marker.txt"), "old");

        var content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, "Cafe.Launcher.Avalonia.exe"), "new launcher");
        File.WriteAllText(Path.Combine(content, "new-marker.txt"), "new");
        var package = Path.Combine(directory.Path, "package.zip");
        ZipFile.CreateFromDirectory(content, package);
        var sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(package)));

        using var parent = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
        Assert.NotNull(parent);
        parent.WaitForExit();
        var parentPid = parent.Id;

        var logPath = Path.Combine(directory.Path, "update-apply.log");
        var arguments = new UpdaterArguments(
            UpdateApplyMode.Portable,
            package,
            install,
            "Cafe.Launcher.Avalonia.exe",
            parentPid,
            sha256,
            logPath);

        var exitCode = await UpdateApplier.ApplyAsync(arguments, CancellationToken.None);

        Assert.Equal(9, exitCode);
        Assert.True(File.Exists(Path.Combine(install, "new-marker.txt")));
        Assert.False(File.Exists(Path.Combine(install, "old-marker.txt")));
        Assert.False(Directory.Exists(UpdateApplyPlan.StagingDirectory(install, parentPid)));
        Assert.False(Directory.Exists(UpdateApplyPlan.BackupDirectory(install, parentPid)));
        var log = await File.ReadAllTextAsync(logPath);
        Assert.Contains("Apply started (mode=Portable", log);
        Assert.Contains("Apply completed (mode=Portable, elapsed=", log);
    }
}
