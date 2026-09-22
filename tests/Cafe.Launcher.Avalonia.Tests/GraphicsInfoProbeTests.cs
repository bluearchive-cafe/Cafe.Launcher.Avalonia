using System;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class GraphicsInfoProbeTests
{
    [Fact]
    public void Probe_KeepsOnlyTheInformativeLines()
    {
        var probe = new GraphicsInfoProbe((tool, _, _) => tool == "vulkaninfo"
            ? "==========\nVULKANINFO\nGPU0:\n\tdeviceName = Test GPU\n\tdriverName = Test Driver\n\tunknown = ignored\n"
            : "name of display: :0\nOpenGL vendor string: Test Vendor\nOpenGL renderer string: Test Renderer\nunrelated\n");

        var info = probe.Probe();

        Assert.NotNull(info);
        Assert.Contains("deviceName = Test GPU", info.Vulkan, StringComparison.Ordinal);
        Assert.Contains("driverName = Test Driver", info.Vulkan, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown = ignored", info.Vulkan, StringComparison.Ordinal);
        Assert.Contains("OpenGL renderer string: Test Renderer", info.OpenGl, StringComparison.Ordinal);
        Assert.DoesNotContain("unrelated", info.OpenGl, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_WhenNeitherToolYieldsAnything_ReturnsNull()
    {
        var probe = new GraphicsInfoProbe((_, _, _) => null);

        Assert.Null(probe.Probe());
    }

    [Fact]
    public void Probe_WhenOnlyOneToolAnswers_KeepsThatHalfAndLeavesTheOtherNull()
    {
        var probe = new GraphicsInfoProbe((tool, _, _) => tool == "glxinfo"
            ? "OpenGL version string: 4.6\n"
            : null);

        var info = probe.Probe();

        Assert.NotNull(info);
        Assert.Null(info.Vulkan);
        Assert.Contains("OpenGL version string: 4.6", info.OpenGl, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_WhenOutputExceedsTheLimit_Truncates()
    {
        var probe = new GraphicsInfoProbe((_, _, _) => "OpenGL version string: " + new string('x', 5000) + "\n");

        var info = probe.Probe();

        Assert.NotNull(info);
        Assert.Equal(GraphicsInfoProbe.MaxCharactersPerTool, info.OpenGl!.Length);
    }
}
