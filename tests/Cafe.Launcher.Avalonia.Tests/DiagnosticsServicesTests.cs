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
    public async Task DetectCrashAsync_WhenLogGrowsAfterSessionStart_ReturnsTrue()
    {
        using var logger = new UnifiedLogger(tempDir);
        var service = new CrashRecoveryService(logger);
        await service.BeginSessionAsync();
        await logger.LogAsync(LogEntrySeverity.Error, "Large failure", new string('x', 8192));

        var crashed = await service.DetectCrashAsync();

        Assert.True(crashed);
    }

    [Fact]
    public async Task BeginSessionAsync_DetectsPreviousCrashBeforeWritingCurrentStart()
    {
        using var logger = new UnifiedLogger(tempDir);
        var service = new CrashRecoveryService(logger);
        var firstSessionCrashed = await service.BeginSessionAsync();

        var crashed = await service.BeginSessionAsync();

        Assert.False(firstSessionCrashed);
        Assert.True(crashed);
        logger.Dispose(); // release Serilog file handle before reading
        Assert.Equal(
            2,
            File.ReadLines(logger.LogFilePath).Count(line => line.Contains("Session started", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task DetectCrashAsync_WhenSessionStartWasRotated_ReturnsTrue()
    {
        using var logger = new UnifiedLogger(tempDir);
        var service = new CrashRecoveryService(logger);
        await service.BeginSessionAsync();
        await logger.LogAsync(
            LogEntrySeverity.Error,
            "Large failure",
            new string('x', 5 * 1024 * 1024));

        var crashed = await service.DetectCrashAsync();

        Assert.True(crashed);
    }

    [Fact]
    public async Task CompleteSessionAsync_RemovesActiveSessionMarker()
    {
        using var logger = new UnifiedLogger(tempDir);
        var service = new CrashRecoveryService(logger);
        await service.BeginSessionAsync();

        await service.CompleteSessionAsync();

        Assert.False(await service.DetectCrashAsync());
        logger.Dispose(); // release Serilog file handle before reading
        Assert.Contains(
            "Session ended",
            File.ReadAllText(logger.LogFilePath),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunSession_WhenApplicationReturns_CompletesSession()
    {
        using var logger = new UnifiedLogger(tempDir);
        var service = new CrashRecoveryService(logger);

        Program.RunSession(service, () => { });

        Assert.False(await service.DetectCrashAsync());
    }

    [Fact]
    public async Task RunSession_WhenApplicationThrows_LeavesActiveSessionMarker()
    {
        using var logger = new UnifiedLogger(tempDir);
        var service = new CrashRecoveryService(logger);

        Assert.Throws<InvalidOperationException>(
            () => Program.RunSession(
                service,
                () => throw new InvalidOperationException("fatal")));

        Assert.True(await service.DetectCrashAsync());
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
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Cafe Launcher"),
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
