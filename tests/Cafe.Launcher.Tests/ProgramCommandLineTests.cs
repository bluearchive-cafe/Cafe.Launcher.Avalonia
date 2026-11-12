using System.Globalization;
using System.IO;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

public sealed class ProgramCommandLineTests
{
    [Fact]
    public void ResolveDataRootAndReportStore_WhenRootResolutionFails_DegradesToTempRoot()
    {
        // 守卫（AUD-ARCH-020）：根解析失败不得在崩溃处理就位前逸出 Main；
        // 快照存储退化为临时根，报告路径仍然可用。
        var (root, store) = Program.ResolveDataRootAndReportStore(
            () => throw new IOException("data root is unavailable"));

        Assert.Equal(
            Path.Combine(Path.GetTempPath(), "Cafe.Launcher"),
            root.Root);
        Assert.Equal(root.CrashReportsDirectory, store.PrimaryDirectory);
    }

    [Fact]
    public void ResolveDataRootAndReportStore_WhenRootResolutionSucceeds_UsesTheResolvedRoot()
    {
        using var directory = TestDirectory.Create();
        var expected = new LauncherDataRoot(directory.Sub("data"));

        var (root, store) = Program.ResolveDataRootAndReportStore(() => expected);

        Assert.Equal(expected.Root, root.Root);
        Assert.Equal(expected.CrashReportsDirectory, store.PrimaryDirectory);
    }

    [Fact]
    public void TryHandleCommandLine_WhenVersionRequested_WritesVersionAndExits()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var handled = Program.TryHandleCommandLine(["--version"], output);

        Assert.True(handled);
        Assert.False(string.IsNullOrWhiteSpace(output.ToString()));
    }

    [Fact]
    public void TryHandleCommandLine_WhenArgumentIsUnknown_DoesNotHandleIt()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var handled = Program.TryHandleCommandLine(["--unknown"], output);

        Assert.False(handled);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public void TryHandleCommandLine_WhenLaunchGameRequested_DoesNotSwallowIt()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var handled = Program.TryHandleCommandLine([Program.LaunchGameArgument], output);

        // --launch-game must reach the normal startup path, not the short-circuit output.
        Assert.False(handled);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public void HasLaunchGameArgument_WithMixedArguments_MatchesExactArgumentOnly()
    {
        Assert.True(Program.HasLaunchGameArgument([Program.LaunchGameArgument]));
        Assert.True(Program.HasLaunchGameArgument(["--other", Program.LaunchGameArgument]));
        Assert.False(Program.HasLaunchGameArgument([]));
        Assert.False(Program.HasLaunchGameArgument(["--launch-games"]));
        Assert.False(Program.HasLaunchGameArgument(["--Launch-Game"]));
        Assert.False(Program.HasLaunchGameArgument(["launch-game"]));
    }

    [Fact]
    public void HasShowHiddenSettingsArgument_WithMixedArguments_MatchesExactArgumentOnly()
    {
        Assert.True(Program.HasShowHiddenSettingsArgument([Program.ShowHiddenSettingsArgument]));
        Assert.True(Program.HasShowHiddenSettingsArgument(["--other", Program.ShowHiddenSettingsArgument]));
        Assert.False(Program.HasShowHiddenSettingsArgument([]));
        Assert.False(Program.HasShowHiddenSettingsArgument(["--show-hidden-setting"]));
        Assert.False(Program.HasShowHiddenSettingsArgument(["--Show-Hidden-Settings"]));
        Assert.False(Program.HasShowHiddenSettingsArgument(["show-hidden-settings"]));
    }
}
