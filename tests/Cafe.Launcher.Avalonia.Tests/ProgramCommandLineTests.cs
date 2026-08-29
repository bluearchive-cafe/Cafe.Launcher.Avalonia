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
}
