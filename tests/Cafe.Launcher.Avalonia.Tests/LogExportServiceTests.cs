using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
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
        var service = new LogExportService(new LocalDiagnostics(logger));

        var zipPath = await service.ExportAsync(exportDirectory, LogExportOptions.Default);

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
            LauncherUserDataDirectory.Root,
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
        var service = new LogExportService(new LocalDiagnostics(logger));

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.ExportAsync(Path.Combine(tempDir, "selected"), LogExportOptions.Default));
    }

    [Fact]
    public async Task ExportAsync_WithoutRange_CopiesLogVerbatim()
    {
        const string content = "2026-09-01T10:00:00.0000000+08:00 [INF] [Test] Old entry\n";
        var logger = WriteDeterministicLog("verbatim-source", content);
        var service = new LogExportService(new LocalDiagnostics(logger));

        var zipPath = await service.ExportAsync(Path.Combine(tempDir, "verbatim-selected"), LogExportOptions.Default);

        Assert.Equal(content, ReadEntry(zipPath, "unified.log"));
    }

    [Fact]
    public async Task ExportAsync_WithCustomRange_KeepsOnlyEntriesInsideTheWindow()
    {
        const string content =
            "2026-09-01T10:00:00.0000000+08:00 [INF] [Test] Old entry\n" +
            "2026-09-01T10:00:01.0000000+08:00 [ERR] [Test] Old failure\n" +
            "old stack line\n" +
            "2026-09-09T10:00:00.0000000+08:00 [INF] [Test] Recent entry\n" +
            "recent detail line\n" +
            "2026-09-09T10:00:01.0000000+08:00 [WRN] [Test] Recent warning\n";
        var logger = WriteDeterministicLog("range-source", content);
        var service = new LogExportService(new LocalDiagnostics(logger));

        var zipPath = await service.ExportAsync(
            Path.Combine(tempDir, "range-selected"),
            new LogExportOptions
            {
                Range = LogExportRangePreset.Custom,
                CustomFrom = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.FromHours(8)),
                CustomTo = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.FromHours(8))
            });

        var filtered = ReadEntry(zipPath, "unified.log");
        Assert.Contains("Recent entry", filtered, StringComparison.Ordinal);
        Assert.Contains("recent detail line", filtered, StringComparison.Ordinal);
        Assert.Contains("Recent warning", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("Old entry", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("old stack line", filtered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_WithRangeMatchingNothing_KeepsTheLogEntry()
    {
        const string content = "2026-09-01T10:00:00.0000000+08:00 [INF] [Test] Old entry\n";
        var logger = WriteDeterministicLog("empty-range-source", content);
        var service = new LogExportService(new LocalDiagnostics(logger));

        var zipPath = await service.ExportAsync(
            Path.Combine(tempDir, "empty-range-selected"),
            new LogExportOptions { Range = LogExportRangePreset.LastHour });

        // The dialog promises the log file is always part of the export, so an empty range still
        // yields an entry: a package without logs would be useless for the diagnosis it exists for.
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, entry => entry.FullName == "unified.log");
        Assert.Contains(zip.Entries, entry => entry.FullName == "system-info.json");
        Assert.Equal("", ReadEntry(zipPath, "unified.log"));
    }

    [Fact]
    public async Task ExportAsync_WithRotatedLogOutsideTheWindow_OmitsThatFile()
    {
        const string inWindow = "2026-09-09T10:00:00.0000000+08:00 [INF] [Test] Recent entry\n";
        const string outOfWindow = "2026-09-01T10:00:00.0000000+08:00 [INF] [Test] Old entry\n";
        var logger = WriteDeterministicLog("rotated-source", inWindow);
        File.WriteAllText(Path.Combine(tempDir, "rotated-source", "unified_001.log"), outOfWindow);
        var service = new LogExportService(new LocalDiagnostics(logger));

        var zipPath = await service.ExportAsync(
            Path.Combine(tempDir, "rotated-selected"),
            new LogExportOptions
            {
                Range = LogExportRangePreset.Custom,
                CustomFrom = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.FromHours(8)),
                CustomTo = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.FromHours(8))
            });

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, entry => entry.FullName == "unified.log");
        // Only the current log carries the "always included" promise; a rotated file that would
        // contribute nothing (and has nothing to say about the window) stays out.
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName == "unified_001.log");
        Assert.Contains("Recent entry", ReadEntry(zipPath, "unified.log"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_WhenCustomRangeIsReversed_Throws()
    {
        var logger = WriteDeterministicLog("reversed-source", "2026-09-09T10:00:00.0000000+08:00 [INF] [Test] Entry\n");
        var service = new LogExportService(new LocalDiagnostics(logger));

        await Assert.ThrowsAsync<ArgumentException>(() => service.ExportAsync(
            Path.Combine(tempDir, "reversed-selected"),
            new LogExportOptions
            {
                Range = LogExportRangePreset.Custom,
                CustomFrom = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero),
                CustomTo = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)
            }));
    }

    [Fact]
    public async Task ExportAsync_WithCrashReports_AddsArtifactsInsideTheWindow()
    {
        var dataRoot = Path.Combine(tempDir, "crash-data");
        var crashDirectory = Path.Combine(dataRoot, CrashReportStore.ReportDirectoryName);
        Directory.CreateDirectory(crashDirectory);
        var recentReport = Path.Combine(crashDirectory, "CR-20260909-120000-ABCD.json");
        var recentAdditional = Path.ChangeExtension(recentReport, ".additional.log");
        var oldReport = Path.Combine(crashDirectory, "CR-20260901-120000-ABCD.json");
        File.WriteAllText(recentReport, "{}");
        File.WriteAllText(oldReport, "{}");
        File.WriteAllText(recentAdditional, "secondary failure");
        // The window is resolved against the wall clock, so every artifact needs an explicit
        // timestamp: one left at its creation time drifts out of range as the calendar moves.
        var recentStamp = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(recentReport, recentStamp);
        File.SetLastWriteTimeUtc(recentAdditional, recentStamp);
        File.SetLastWriteTimeUtc(oldReport, new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));
        var logger = WriteDeterministicLog("crash-source", "2026-09-09T10:00:00.0000000+08:00 [INF] [Test] Entry\n");
        var service = new LogExportService(new LocalDiagnostics(logger), dataRoot);

        var zipPath = await service.ExportAsync(
            Path.Combine(tempDir, "crash-selected"),
            new LogExportOptions
            {
                Range = LogExportRangePreset.Custom,
                CustomFrom = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.FromHours(8)),
                CustomTo = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.FromHours(8)),
                IncludeCrashReports = true
            });

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, entry => entry.FullName == "crash-reports/CR-20260909-120000-ABCD.json");
        Assert.Contains(zip.Entries, entry => entry.FullName == "crash-reports/CR-20260909-120000-ABCD.additional.log");
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName == "crash-reports/CR-20260901-120000-ABCD.json");
    }

    [Fact]
    public async Task ExportAsync_WithUserData_BundlesLauncherStateFilesOnly()
    {
        var dataRoot = Path.Combine(tempDir, "user-data-root");
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(dataRoot, "settings.json"), "{\"logLevel\":\"information\"}");
        File.WriteAllText(Path.Combine(dataRoot, "clickCode"), "abc123");
        // Caches, the compatibility probe, and the export folder itself stay out: bundling a
        // previous archive would nest one ZIP inside the next.
        string[] excludedDirectories = ["image-cache", "compatibility", "log-exports"];
        foreach (var excluded in excludedDirectories)
        {
            var directory = Path.Combine(dataRoot, excluded);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "payload.bin"), "binary");
        }

        var logger = WriteDeterministicLog("user-data-source", "2026-09-09T10:00:00.0000000+08:00 [INF] [Test] Entry\n");
        var service = new LogExportService(new LocalDiagnostics(logger), dataRoot);

        var zipPath = await service.ExportAsync(
            Path.Combine(tempDir, "user-data-selected"),
            new LogExportOptions { IncludeUserData = true });

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, entry => entry.FullName == "user-data/settings.json");
        Assert.Contains(zip.Entries, entry => entry.FullName == "user-data/clickCode");
        foreach (var excluded in excludedDirectories)
        {
            Assert.DoesNotContain(
                zip.Entries,
                entry => entry.FullName.StartsWith($"user-data/{excluded}", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ExportAsync_WhenARequestedCrashReportCannotBeRead_RecordsItAsSkipped()
    {
        var dataRoot = Path.Combine(tempDir, "skipped-data");
        var crashDirectory = Path.Combine(dataRoot, CrashReportStore.ReportDirectoryName);
        Directory.CreateDirectory(crashDirectory);
        const string lockedName = "CR-20260909-120000-ABCD.json";
        const string readableName = "CR-20260909-130000-ABCD.json";
        var lockedReport = Path.Combine(crashDirectory, lockedName);
        File.WriteAllText(lockedReport, "{}");
        File.WriteAllText(Path.Combine(crashDirectory, readableName), "{}");
        // Writing the unified log before the logger opens it keeps a live logger: the sink
        // appends, so the warnings the export raises land in the file it just exported.
        var logDirectory = Path.Combine(tempDir, "skipped-source");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, "unified.log");
        File.WriteAllText(logPath, "2026-09-09T10:00:00.0000000+08:00 [INF] [Test] Entry\n");
        using var logger = new UnifiedLogger(logDirectory);
        var service = new LogExportService(new LocalDiagnostics(logger), dataRoot);

        string zipPath;
        using (File.Open(lockedReport, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            zipPath = await service.ExportAsync(
                Path.Combine(tempDir, "skipped-selected"),
                new LogExportOptions { IncludeCrashReports = true });
        }

        using (var zip = ZipFile.OpenRead(zipPath))
        {
            Assert.DoesNotContain(
                zip.Entries,
                entry => entry.FullName.EndsWith(lockedName, StringComparison.Ordinal));
            Assert.Contains(
                zip.Entries,
                entry => entry.FullName.EndsWith(readableName, StringComparison.Ordinal));

            var systemInfo = zip.Entries.Single(item => item.FullName == "system-info.json");
            using var reader = new StreamReader(systemInfo.Open(), Encoding.UTF8);
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            var skipped = document.RootElement
                .GetProperty("export")
                .GetProperty("skipped")
                .EnumerateArray()
                .ToArray();
            var entry = Assert.Single(
                skipped,
                item => item.GetProperty("entry").GetString() == $"crash-reports/{lockedName}");
            Assert.Equal("IOException", entry.GetProperty("reason").GetString());
        }

        logger.Dispose(); // flush the async sink before reading the diagnostics
        var diagnostics = File.ReadAllText(logPath);
        Assert.Contains("[LogExport]", diagnostics, StringComparison.Ordinal);
        Assert.Contains($"Skipped crash-reports/{lockedName}", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_WritesExportMetadataIntoSystemInfo()
    {
        var dataRoot = Path.Combine(tempDir, "metadata-data");
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(dataRoot, "settings.json"), "{}");
        var logger = WriteDeterministicLog("metadata-source", "2026-09-09T10:00:00.0000000+08:00 [INF] [Test] Entry\n");
        var service = new LogExportService(new LocalDiagnostics(logger), dataRoot);

        var zipPath = await service.ExportAsync(
            Path.Combine(tempDir, "metadata-selected"),
            new LogExportOptions
            {
                Range = LogExportRangePreset.Last7Days,
                IncludeUserData = true
            });

        using var document = JsonDocument.Parse(ReadEntry(zipPath, "system-info.json"));
        var export = document.RootElement.GetProperty("export");
        Assert.Equal("Last7Days", export.GetProperty("range").GetString());
        Assert.False(export.GetProperty("includeCrashReports").GetBoolean());
        Assert.True(export.GetProperty("includeUserData").GetBoolean());
        var entries = export.GetProperty("entries")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Contains("unified.log", entries);
        Assert.Contains("user-data/settings.json", entries);
    }

    [Fact]
    public async Task HasLogEntriesAsync_WithEntriesInsideTheWindow_ReturnsTrue()
    {
        var logger = WriteDeterministicLog(
            "probe-inside-source",
            "2026-09-01T10:00:00.0000000+08:00 [INF] [Test] Old entry\n" +
            "2026-09-09T10:00:00.0000000+08:00 [INF] [Test] Recent entry\n");
        var service = new LogExportService(new LocalDiagnostics(logger));

        var hasEntries = await service.HasLogEntriesAsync(new LogExportOptions
        {
            Range = LogExportRangePreset.Custom,
            CustomFrom = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.FromHours(8)),
            CustomTo = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.FromHours(8))
        });

        Assert.True(hasEntries);
    }

    [Fact]
    public async Task HasLogEntriesAsync_WithEntriesOutsideTheWindow_ReturnsFalse()
    {
        var logger = WriteDeterministicLog(
            "probe-outside-source",
            "2026-09-01T10:00:00.0000000+08:00 [INF] [Test] Old entry\n");
        var service = new LogExportService(new LocalDiagnostics(logger));

        var hasEntries = await service.HasLogEntriesAsync(new LogExportOptions
        {
            Range = LogExportRangePreset.Custom,
            CustomFrom = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.FromHours(8)),
            CustomTo = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.FromHours(8))
        });

        Assert.False(hasEntries);
    }

    [Fact]
    public async Task HasLogEntriesAsync_WithoutARange_ReportsEntriesWithoutReadingTheLog()
    {
        // Only old entries, so a window would say "nothing": an unbounded range keeps whatever the
        // files hold and must not be reported as empty.
        var logger = WriteDeterministicLog(
            "probe-unbounded-source",
            "2026-09-01T10:00:00.0000000+08:00 [INF] [Test] Old entry\n");
        var service = new LogExportService(new LocalDiagnostics(logger));

        Assert.True(await service.HasLogEntriesAsync(LogExportOptions.Default));
    }

    [Fact]
    public async Task HasLogEntriesAsync_WhenTheLogFileIsMissing_ReturnsFalse()
    {
        using var logger = new UnifiedLogger(Path.Combine(tempDir, "probe-missing-source"));
        var service = new LogExportService(new LocalDiagnostics(logger));

        var hasEntries = await service.HasLogEntriesAsync(
            new LogExportOptions { Range = LogExportRangePreset.LastHour });

        Assert.False(hasEntries);
    }

    private UnifiedLogger WriteDeterministicLog(string directoryName, string content)
    {
        var logDirectory = Path.Combine(tempDir, directoryName);
        Directory.CreateDirectory(logDirectory);
        var logger = new UnifiedLogger(logDirectory);
        logger.Dispose(); // release the sink so the file can be replaced with deterministic content
        File.WriteAllText(logger.LogFilePath, content);
        return logger;
    }

    private static string ReadEntry(string zipPath, string entryName)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.Single(item => item.FullName == entryName);
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
