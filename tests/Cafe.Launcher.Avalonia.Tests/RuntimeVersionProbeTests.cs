using System;
using System.IO;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class RuntimeVersionProbeTests
{
    [Fact]
    public async Task ProbeAsync_WhenRuntimeResponds_ReturnsParsedVersion()
    {
        var dotnetPath = FindDotnet();
        Assert.SkipUnless(dotnetPath is not null, "needs a dotnet executable on PATH");

        var version = await RuntimeVersionProbe.ProbeAsync(
            dotnetPath!,
            "--version",
            TimeSpan.FromSeconds(60));

        Assert.NotNull(version);
        Assert.Matches("""\d+(\.\d+)+""", version);
    }

    [Fact]
    public async Task ProbeAsync_WhenExecutableDoesNotExist_ReturnsNull()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "cafe-launcher-probe-missing", "umu-run");

        var version = await RuntimeVersionProbe.ProbeAsync(
            missingPath,
            "--version",
            TimeSpan.FromSeconds(30));

        Assert.Null(version);
    }

    [Fact]
    public async Task ProbeAsync_WhenProcessExitsNonZero_ReturnsNull()
    {
        var dotnetPath = FindDotnet();
        Assert.SkipUnless(dotnetPath is not null, "needs a dotnet executable on PATH");

        // The dotnet muxer exits 1 on an unknown command without touching the
        // filesystem — a deterministic stand-in for a broken "umu-run --version".
        var version = await RuntimeVersionProbe.ProbeAsync(
            dotnetPath!,
            "this-is-not-a-dotnet-command",
            TimeSpan.FromSeconds(60));

        Assert.Null(version);
    }

    [Fact]
    public async Task ProbeAsync_WhenTimeoutElapses_ReturnsNullInsteadOfHanging()
    {
        var dotnetPath = FindDotnet();
        Assert.SkipUnless(dotnetPath is not null, "needs a dotnet executable on PATH");

        // A deliberately impossible budget: process spawn alone outlasts it, so the
        // probe must give up and kill the child rather than wait forever.
        var version = await RuntimeVersionProbe.ProbeAsync(
            dotnetPath!,
            "--version",
            TimeSpan.FromMilliseconds(1));

        Assert.Null(version);
    }

    [Fact]
    public void ParseVersion_ExtractsDottedVersionFromFirstOutputLine()
    {
        Assert.Equal("1.4.4", RuntimeVersionProbe.ParseVersion("umu-launcher 1.4.4\n", ""));
        Assert.Equal("9.0", RuntimeVersionProbe.ParseVersion("wine-9.0\n", ""));
    }

    [Fact]
    public void ParseVersion_WhenOutputHasNoVersionToken_KeepsTheWholeFirstLine()
    {
        Assert.Equal("some custom runtime build", RuntimeVersionProbe.ParseVersion("some custom runtime build\n", ""));
    }

    [Fact]
    public void ParseVersion_PrefersStdoutAndFallsBackToStderr()
    {
        Assert.Equal("10.0", RuntimeVersionProbe.ParseVersion("", "wine-10.0\n"));
        Assert.Null(RuntimeVersionProbe.ParseVersion("", ""));
    }

    private static string? FindDotnet() =>
        ExecutableLocator.FindInPath(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
}
