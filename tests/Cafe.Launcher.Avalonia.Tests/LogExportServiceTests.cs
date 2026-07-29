using System.IO.Compression;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LogExportServiceTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public LogExportServiceTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public async Task ExportAsync_WritesArchiveToSpecifiedDirectory()
    {
        var logDirectory = Path.Combine(tempDir, "source");
        var exportDirectory = Path.Combine(tempDir, "selected");
        using var logger = new UnifiedLogger(logDirectory);
        await logger.LogAsync(LogEntrySeverity.Info, "Test log");
        logger.Dispose(); // flush async sink to disk before reading
        var service = new LogExportService(logger);

        var zipPath = await service.ExportAsync(exportDirectory);

        Assert.Equal(exportDirectory, Path.GetDirectoryName(zipPath));
        Assert.True(File.Exists(zipPath));
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, entry => entry.FullName == "unified.log");
        Assert.Contains(zip.Entries, entry => entry.FullName == "system-info.json");
    }

    [Fact]
    public void DefaultExportDirectory_UsesProductDataExportFolder()
    {
        var expected = Path.Combine(
            LauncherUserDataDirectory.Path,
            LauncherConstants.LogExportFolderName);

        Assert.Equal(expected, LogExportService.DefaultExportDirectory);
        Assert.EndsWith(
            "log-exports",
            LogExportService.DefaultExportDirectory,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_WhenCurrentLogIsMissing_Throws()
    {
        using var logger = new UnifiedLogger(Path.Combine(tempDir, "missing-source"));
        var service = new LogExportService(logger);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.ExportAsync(Path.Combine(tempDir, "selected")));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
