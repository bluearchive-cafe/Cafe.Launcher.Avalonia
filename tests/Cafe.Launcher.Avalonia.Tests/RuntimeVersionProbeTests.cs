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

        var result = await RuntimeVersionProbe.ProbeAsync(
            dotnetPath!,
            "--version",
            TimeSpan.FromSeconds(60));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Version);
        Assert.Matches("""\d+(\.\d+)+""", result.Version);
    }

    [Fact]
    public async Task ProbeAsync_WhenExecutableDoesNotExist_ReturnsNull()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "cafe-launcher-probe-missing", "umu-run");

        var result = await RuntimeVersionProbe.ProbeAsync(
            missingPath,
            "--version",
            TimeSpan.FromSeconds(30));

        Assert.Equal(RuntimeProbeFailureKind.ProcessStartFailed, result.FailureKind);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ProbeAsync_WhenProcessExitsNonZero_ReturnsNull()
    {
        var dotnetPath = FindDotnet();
        Assert.SkipUnless(dotnetPath is not null, "needs a dotnet executable on PATH");

        // The dotnet muxer exits 1 on an unknown command without touching the
        // filesystem — a deterministic stand-in for a broken "umu-run --version".
        var result = await RuntimeVersionProbe.ProbeAsync(
            dotnetPath!,
            "this-is-not-a-dotnet-command",
            TimeSpan.FromSeconds(60));

        Assert.Equal(RuntimeProbeFailureKind.NonZeroExit, result.FailureKind);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardError));
    }

    [Fact]
    public async Task ProbeAsync_WhenTimeoutElapses_ReturnsNullInsteadOfHanging()
    {
        var dotnetPath = FindDotnet();
        Assert.SkipUnless(dotnetPath is not null, "needs a dotnet executable on PATH");

        // A deliberately impossible budget: process spawn alone outlasts it, so the
        // probe must give up and kill the child rather than wait forever.
        var result = await RuntimeVersionProbe.ProbeAsync(
            dotnetPath!,
            "--version",
            TimeSpan.FromMilliseconds(1));

        Assert.Equal(RuntimeProbeFailureKind.TimedOut, result.FailureKind);
    }

    [Fact]
    public void ParseVersion_WithDottedVersionInFirstLine_ExtractsVersionToken()
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
    public void ParseVersion_WithBothOutputStreams_PrefersStdoutAndFallsBackToStderr()
    {
        Assert.Equal("10.0", RuntimeVersionProbe.ParseVersion("", "wine-10.0\n"));
        Assert.Null(RuntimeVersionProbe.ParseVersion("", ""));
    }

    [Fact]
    public void Describe_WithNonZeroProbeEvidence_IncludesCommandExitCodeAndStderr()
    {
        var result = new RuntimeProbeResult(
            RuntimeProbeFailureKind.NonZeroExit,
            ExitCode: 17,
            StandardError: "runtime is broken");

        var description = result.Describe("/usr/bin/umu-run", "--version");

        Assert.Contains("Command: \"/usr/bin/umu-run\" --version", description, StringComparison.Ordinal);
        Assert.Contains("ExitCode: 17", description, StringComparison.Ordinal);
        Assert.Contains("StandardError: runtime is broken", description, StringComparison.Ordinal);
    }

    private static string? FindDotnet() =>
        ExecutableLocator.FindInPath(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
}
