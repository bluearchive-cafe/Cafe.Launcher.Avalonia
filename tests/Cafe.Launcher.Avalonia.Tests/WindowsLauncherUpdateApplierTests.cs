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
}
