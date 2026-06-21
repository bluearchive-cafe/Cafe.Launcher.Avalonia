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
        Assert.Equal(
            2,
            File.ReadLines(logger.LogFilePath).Count(line => line.Contains("[SESSION_START]", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task DetectCrashAsync_WhenSessionStartWasRotated_ReturnsTrue()
    {
        using var logger = new UnifiedLogger(tempDir, new LogRotationManager());
        var service = new CrashRecoveryService(logger);
        await service.BeginSessionAsync();
        await logger.LogAsync(
            LogEntrySeverity.Error,
            "Large failure",
            new string('x', checked((int)LogRotationManager.MaxFileSize)));

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
        Assert.Contains(
            "[SESSION_END]",
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

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
