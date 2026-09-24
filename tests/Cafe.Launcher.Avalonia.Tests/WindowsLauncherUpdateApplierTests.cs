using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.Update;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class WindowsLauncherUpdateApplierTests
{
    [Fact]
    public void TryStartApply_WhenPreparationIsNotReady_ReturnsFalse()
    {
        using var directory = TestDirectory.Create();
        var applier = new WindowsLauncherUpdateApplier(directory.DataRoot, new LocalDiagnostics());

        Assert.False(applier.TryStartApply(LauncherSelfUpdatePreparation.External()));
        Assert.False(applier.TryStartApply(LauncherSelfUpdatePreparation.Failed("verification failed")));
    }

    [Fact]
    public void TryStartApply_WhenHelperExecutableIsMissing_ReturnsFalse()
    {
        using var directory = TestDirectory.Create();
        var applier = new WindowsLauncherUpdateApplier(directory.DataRoot, new LocalDiagnostics());
        var preparation = LauncherSelfUpdatePreparation.Ready(
            LauncherUpdateTarget.WindowsPortable,
            Path.Combine(directory, "pkg.zip"),
            new string('a', 64));

        Assert.False(applier.TryStartApply(preparation));
    }

    [Fact]
    public void CleanupAbandonedHelperDirectories_RemovesOnlyGuidNamedSubdirectories()
    {
        using var directory = TestDirectory.Create();
        var tempRoot = Path.Combine(directory.Path, "temp");
        var abandoned = Path.Combine(tempRoot, "CafeLauncherUpdate", Guid.NewGuid().ToString("N"));
        var abandonedToo = Path.Combine(tempRoot, "CafeLauncherUpdate", Guid.NewGuid().ToString("N"));
        var foreign = Path.Combine(tempRoot, "CafeLauncherUpdate", "keep-me");
        Directory.CreateDirectory(abandoned);
        Directory.CreateDirectory(abandonedToo);
        Directory.CreateDirectory(foreign);
        File.WriteAllText(
            Path.Combine(abandoned, UpdateHelperCommand.HelperExecutableName),
            "stale helper copy");
        var applier = new WindowsLauncherUpdateApplier(directory.DataRoot, new LocalDiagnostics(), tempRoot);

        applier.CleanupAbandonedHelperDirectories();

        Assert.False(Directory.Exists(abandoned));
        Assert.False(Directory.Exists(abandonedToo));
        Assert.True(Directory.Exists(foreign));
    }

    [Fact]
    public void CleanupAbandonedHelperDirectories_WhenTempRootDoesNotExist_DoesNothing()
    {
        using var directory = TestDirectory.Create();
        var applier = new WindowsLauncherUpdateApplier(
            directory.DataRoot,
            new LocalDiagnostics(),
            Path.Combine(directory.Path, "absent"));

        applier.CleanupAbandonedHelperDirectories();
    }
}
