using System.Globalization;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ProgramCommandLineTests
{
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
    public void HasLaunchGameArgument_MatchesExactArgumentOnly()
    {
        Assert.True(Program.HasLaunchGameArgument([Program.LaunchGameArgument]));
        Assert.True(Program.HasLaunchGameArgument(["--other", Program.LaunchGameArgument]));
        Assert.False(Program.HasLaunchGameArgument([]));
        Assert.False(Program.HasLaunchGameArgument(["--launch-games"]));
        Assert.False(Program.HasLaunchGameArgument(["--Launch-Game"]));
        Assert.False(Program.HasLaunchGameArgument(["launch-game"]));
    }
}
