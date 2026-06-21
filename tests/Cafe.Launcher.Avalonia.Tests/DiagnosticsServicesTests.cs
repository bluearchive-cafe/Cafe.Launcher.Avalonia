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
    public async Task DetectCrashAsync_WhenLastSessionExceedsTailBuffer_ReturnsTrue()
    {
        using var logger = new UnifiedLogger(tempDir);
        var service = new CrashRecoveryService(logger);
        await logger.WriteSessionStartAsync();
        await logger.LogAsync(LogEntrySeverity.Error, "Large failure", new string('x', 8192));

        var crashed = await service.DetectCrashAsync();

        Assert.True(crashed);
    }

    [Fact]
    public async Task BeginSessionAsync_DetectsPreviousCrashBeforeWritingCurrentStart()
    {
        using var logger = new UnifiedLogger(tempDir);
        var service = new CrashRecoveryService(logger);
        await logger.WriteSessionStartAsync();

        var crashed = await service.BeginSessionAsync();

        Assert.True(crashed);
        Assert.Equal(
            2,
            File.ReadLines(logger.LogFilePath).Count(line => line.Contains("[SESSION_START]", StringComparison.Ordinal)));
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
