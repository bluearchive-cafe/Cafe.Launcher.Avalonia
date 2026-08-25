using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class DiagnosticsServicesTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public DiagnosticsServicesTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public void LocalDiagnostics_ParameterlessConstructor_UsesTemporaryDirectory()
    {
        var diagnostics = new LocalDiagnostics();

        Assert.StartsWith(
            Path.GetTempPath(),
            diagnostics.LogFilePath,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            LauncherUserDataDirectory.Root,
            diagnostics.LogFilePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnrichedTemplate_RendersLogTitleTag()
    {
        using var logger = new UnifiedLogger(tempDir);
        await logger.LogAsync(LogEntrySeverity.Info, "TestTitle", "TestMessage");

        logger.Dispose();
        var text = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("[TestTitle]", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DebugLevel_WrittenWhenMinLevelIsVerbose()
    {
        using var logger = new UnifiedLogger(tempDir);
        logger.SetMinimumLevel(Serilog.Events.LogEventLevel.Verbose);
        await logger.LogAsync(LogEntrySeverity.Debug, "DebugTest");

        logger.Dispose();
        var text = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("[DebugTest]", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DebugLevel_SuppressedWhenMinLevelIsInfo()
    {
        using var logger = new UnifiedLogger(tempDir);
        logger.SetMinimumLevel(Serilog.Events.LogEventLevel.Information);
        // Seed an Info event so the log file exists even if the Debug event is suppressed.
        await logger.LogAsync(LogEntrySeverity.Info, "SeedEvent");
        await logger.LogAsync(LogEntrySeverity.Debug, "ShouldNotAppear");

        logger.Dispose();
        var text = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("[SeedEvent]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[ShouldNotAppear]", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FatalLevel_PassesFatalSwitch()
    {
        using var logger = new UnifiedLogger(tempDir);
        logger.SetMinimumLevel(Serilog.Events.LogEventLevel.Fatal);
        await logger.LogAsync(
            LogEntrySeverity.Fatal,
            "FatalTest",
            exception: new InvalidOperationException("boom"));

        logger.Dispose();
        var text = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("[FatalTest]", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalDiagnostics_NewFacades_WriteExpectedLevels()
    {
        using var logger = new UnifiedLogger(tempDir);
        logger.SetMinimumLevel(Serilog.Events.LogEventLevel.Verbose);
        var diagnostics = new LocalDiagnostics(logger);

        await diagnostics.DebugAsync("DebugFacade", "debug msg");
        await diagnostics.VerboseAsync("VerboseFacade", "verbose msg");
        await diagnostics.WarningAsync("WarningFacade", "warning msg");
        await diagnostics.FatalAsync("FatalFacade", new InvalidOperationException("fatal"));

        logger.Dispose();
        var text = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("[DebugFacade]", text, StringComparison.Ordinal);
        Assert.Contains("[VerboseFacade]", text, StringComparison.Ordinal);
        Assert.Contains("[WarningFacade]", text, StringComparison.Ordinal);
        Assert.Contains("[FatalFacade]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RunSession_WhenActionReturns_WritesSessionStartAndEnd()
    {
        using var logger = new UnifiedLogger(tempDir);
        var ran = false;

        Program.RunSession(logger, () => ran = true);

        Assert.True(ran);
        logger.Dispose();
        var text = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("Session started", text, StringComparison.Ordinal);
        Assert.Contains("Session ended", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RunSession_WhenActionThrows_LogsCrashAndRethrows()
    {
        using var logger = new UnifiedLogger(tempDir);

        var exception = Assert.Throws<InvalidOperationException>(
            () => Program.RunSession(logger, () => throw new InvalidOperationException("fatal")));

        Assert.Equal("fatal", exception.Message);
        logger.Dispose();
        var text = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("Session started", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Session ended", text, StringComparison.Ordinal);
        Assert.Contains("[Main]", text, StringComparison.Ordinal);
        Assert.Contains("fatal", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LogSyncSeverityOverload_DoesNotThrow()
    {
        // LogSync uses a Volatile-read static reference. This at minimum verifies the
        // Debug.WriteLine fallback path does not throw. The integrated path (writing
        // through the DI-resolved UnifiedLogger) is exercised by the instance-level facade tests.
        LocalDiagnostics.LogSync(LogEntrySeverity.Debug, "SyncDebug", "sync msg");
        LocalDiagnostics.LogSync("SyncInfo", "info msg");
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
