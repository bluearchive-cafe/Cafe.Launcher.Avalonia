using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LogExportServiceTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();

    [Fact]
    public async Task ExportAsync_WritesArchiveToSpecifiedDirectory()
    {
        var logDirectory = Path.Combine(tempDir, "source");
        var exportDirectory = Path.Combine(tempDir, "selected");
        using var logger = new UnifiedLogger(logDirectory);
        await logger.LogAsync(LogEntrySeverity.Info, "Test log");
        logger.Dispose(); // flush async sink to disk before reading
        var service = new LogExportService(new LocalDiagnostics(logger), TestDataRoot.ForCurrentProcess(), new CrashReportStore(TestDataRoot.ForCurrentProcess()) );

        var zipPath = await service.ExportAsync(exportDirectory, LogExportOptions.Default);

        Assert.Equal(exportDirectory, Path.GetDirectoryName(zipPath));
        Assert.True(File.Exists(zipPath));
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, entry => entry.FullName == "unified.log");
        Assert.Contains(zip.Entries, entry => entry.FullName == "system-info.json");
    }

    [Fact]
    public async Task ExportAsync_OnLinux_AddsTheLinuxProcessSnapshotEntry()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The /proc snapshot is Linux-only.");
        var logger = WriteDeterministicLog(
            "snapshot-source",
            $"{DateTimeOffset.Now:O} [INF] [Test] Entry\n");
        var service = new LogExportService(
            new LocalDiagnostics(logger),
            TestDataRoot.ForCurrentProcess(),
            new CrashReportStore(TestDataRoot.ForCurrentProcess()));

        var zipPath = await service.ExportAsync(
            Path.Combine(tempDir, "snapshot-selected"),
            LogExportOptions.Default);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, entry => entry.FullName == LinuxProcessSnapshot.EntryName);
    }

    [Fact]
    public async Task ExportAsync_WithARunnerOutputCapture_IncludesIt()
    {
        var dataRoot = Path.Combine(tempDir, "runner-output-root");
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(
            Path.Combine(dataRoot, GamePaths.RunnerOutputFileName),
            "[out] prefix initialized\n");
        var logger = WriteDeterministicLog(
            "runner-output-source",
            "2026-09-09T10:00:00.0000000+08:00 [INF] [Test] Entry\n");
        var service = new LogExportService(
            new LocalDiagnostics(logger),
            TestDataRoot.ForDirectory(dataRoot),
            new CrashReportStore(TestDataRoot.ForCurrentProcess()));

        var zipPath = await service.ExportAsync(
            Path.Combine(tempDir, "runner-output-selected"),
            LogExportOptions.Default);

        Assert.Equal("[out] prefix initialized\n", ReadEntry(zipPath, GamePaths.RunnerOutputFileName));
    }

    [Fact]
    public async Task ExportAsync_WithACompatibilityReport_IncludesIt()
    {
        var dataRoot = Path.Combine(tempDir, "compat-data");
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(
            Path.Combine(dataRoot, GamePaths.CompatibilityEnvironmentFileName),
            "{\"prefixPath\":\"/home/u/pfx\",\"findings\":[]}");
        var logger = WriteDeterministicLog(
            "compat-source",
            "2026-09-09T10:00:00.0000000+08:00 [INF] [Test] Entry\n");
        var service = new LogExportService(
            new LocalDiagnostics(logger),
            TestDataRoot.ForDirectory(dataRoot),
            new CrashReportStore(TestDataRoot.ForCurrentProcess()));

        var zipPath = await service.ExportAsync(
            Path.Combine(tempDir, "compat-selected"),
            LogExportOptions.Default);

        Assert.Contains(
            "prefixPath",
            ReadEntry(zipPath, GamePaths.CompatibilityEnvironmentFileName),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultExportDirectory_UsesProductDataExportFolder()
    {
        var dataRoot = tempDir.DataRoot;
        using var logger = new UnifiedLogger(Path.Combine(tempDir, "directory-probe"));

        // 目录布局由数据根模块拥有：导出目录就是根下的 log-exports，
        // 服务暴露的正是同一个值。
        Assert.Equal(
            Path.Combine(dataRoot.Root, LauncherConstants.LogExportFolderName),
            dataRoot.LogExportDirectory);
        Assert.EndsWith("log-exports", dataRoot.LogExportDirectory, StringComparison.Ordinal);

        var service = new LogExportService(
            new LocalDiagnostics(logger),
            dataRoot,
            new CrashReportStore(dataRoot));
        Assert.Equal(dataRoot.LogExportDirectory, service.DefaultExportDirectory);
    }

    [Fact]
    public async Task ExportAsync_WhenCurrentLogIsMissing_Throws()
    {
        using var logger = new UnifiedLogger(Path.Combine(tempDir, "missing-source"));
        var service = new LogExportService(new LocalDiagnostics(logger), TestDataRoot.ForCurrentProcess(), new CrashReportStore(TestDataRoot.ForCurrentProcess()) );
        var destination = Path.Combine(tempDir, "selected");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.ExportAsync(destination, LogExportOptions.Default));

        Assert.Empty(Directory.GetFiles(destination));
    }

    [Fact]
    public async Task ExportAsync_WhenCancellationIsRequested_LeavesNoArchive()
    {
        var logger = WriteDeterministicLog(
            "cancelled-source",
            $"{DateTimeOffset.Now:O} [INF] [Test] Entry\n");
        var service = new LogExportService(new LocalDiagnostics(logger), TestDataRoot.ForCurrentProcess(), new CrashReportStore(TestDataRoot.ForCurrentProcess()) );
        var destination = Path.Combine(tempDir, "cancelled-selected");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExportAsync(destination, LogExportOptions.Default, cancellation.Token));

        Assert.Empty(Directory.GetFiles(destination));
    }

    [Fact]
    public async Task ExportAsync_WithoutRange_CopiesLogVerbatim()
    {
        const string content = "2026-09-01T10:00:00.0000000+08:00 [INF] [Test] Old entry\n";
        var logger = WriteDeterministicLog("verbatim-source", content);
        var service = new LogExportService(new LocalDiagnostics(logger), TestDataRoot.ForCurrentProcess(), new CrashReportStore(TestDataRoot.ForCurrentProcess()) );

        var zipPath = await service.ExportAsync(Path.Combine(tempDir, "verbatim-selected"), LogExportOptions.Default);

        Assert.Equal(content, ReadEntry(zipPath, "unified.log"));
    }

    [Fact]
    public async Task ExportAsync_WithPresetRange_KeepsOnlyEntriesInsideTheWindow()
    {
        var now = DateTimeOffset.Now;
        var content =
            $"{now.AddHours(-2):O} [INF] [Test] Old entry\n" +
            $"{now.AddHours(-2).AddSeconds(1):O} [ERR] [Test] Old failure\n" +
            "old stack line\n" +
            $"{now.AddMinutes(-5):O} [INF] [Test] Recent entry\n" +
            "recent detail line\n" +
            $"{now.AddMinutes(-4):O} [WRN] [Test] Recent warning\n" +
            $"{now.AddHours(1):O} [INF] [Test] Future entry\n";
        var logger = WriteDeterministicLog("range-source", content);
        var service = new LogExportService(new LocalDiagnostics(logger), TestDataRoot.ForCurrentProcess(), new CrashReportStore(TestDataRoot.ForCurrentProcess()) );

        var zipPath = await service.ExportAsync(
            Path.Combine(tempDir, "range-selected"),
            new LogExportOptions { Range = LogExportRangePreset.LastHour });

        var filtered = ReadEntry(zipPath, "unified.log");
        Assert.Contains("Recent entry", filtered, StringComparison.Ordinal);
        Assert.Contains("recent detail line", filtered, StringComparison.Ordinal);
        Assert.Contains("Recent warning", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("Old entry", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("old stack line", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("Future entry", filtered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_WithRangeMatchingNothing_KeepsTheLogEntry()
    {
        const string content = "2026-09-01T10:00:00.0000000+08:00 [INF] [Test] Old entry\n";
        var logger = WriteDeterministicLog("empty-range-source", content);
        var service = new LogExportService(new LocalDiagnostics(logger), TestDataRoot.ForCurrentProcess(), new CrashReportStore(TestDataRoot.ForCurrentProcess()) );

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
        var now = DateTimeOffset.Now;
        var inWindow = $"{now.AddMinutes(-5):O} [INF] [Test] Recent entry\n";
        var outOfWindow = $"{now.AddHours(-2):O} [INF] [Test] Old entry\n";
        var logger = WriteDeterministicLog("rotated-source", inWindow);
        File.WriteAllText(Path.Combine(tempDir, "rotated-source", "unified_001.log"), outOfWindow);
        var service = new LogExportService(new LocalDiagnostics(logger), TestDataRoot.ForCurrentProcess(), new CrashReportStore(TestDataRoot.ForCurrentProcess()) );

        var zipPath = await service.ExportAsync(
            Path.Combine(tempDir, "rotated-selected"),
            new LogExportOptions { Range = LogExportRangePreset.LastHour });

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, entry => entry.FullName == "unified.log");
        // Only the current log carries the "always included" promise; a rotated file that would
        // contribute nothing (and has nothing to say about the window) stays out.
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName == "unified_001.log");
        Assert.Contains("Recent entry", ReadEntry(zipPath, "unified.log"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_WithCrashReports_AddsArtifactsInsideTheWindow()
    {
        var dataRoot = Path.Combine(tempDir, "crash-data");
        var crashDirectory = Path.Combine(dataRoot, LauncherDataRoot.CrashReportsFolderName);
        Directory.CreateDirectory(crashDirectory);
        var recentReport = Path.Combine(crashDirectory, "CR-20260909-120000-ABCD.json");
        var recentAdditional = Path.ChangeExtension(recentReport, ".additional.log");
        var oldReport = Path.Combine(crashDirectory, "CR-20260901-120000-ABCD.json");
        File.WriteAllText(recentReport, "{}");
        File.WriteAllText(oldReport, "{}");
        File.WriteAllText(recentAdditional, "secondary failure");
        // The window is resolved against the wall clock, so every artifact needs an explicit
        // timestamp: one left at its creation time drifts out of range as the calendar moves.
        var recentStamp = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(recentReport, recentStamp);
        File.SetLastWriteTimeUtc(recentAdditional, recentStamp);
        File.SetLastWriteTimeUtc(oldReport, DateTime.UtcNow.AddDays(-2));
        var logger = WriteDeterministicLog("crash-source", $"{DateTimeOffset.Now.AddMinutes(-5):O} [INF] [Test] Entry\n");
        var service = new LogExportService(new LocalDiagnostics(logger), TestDataRoot.ForDirectory(dataRoot) , new CrashReportStore(TestDataRoot.ForCurrentProcess()));

        var zipPath = await service.ExportAsync(
            Path.Combine(tempDir, "crash-selected"),
            new LogExportOptions
            {
                Range = LogExportRangePreset.Last24Hours,
                IncludeCrashReports = true
            });

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, entry => entry.FullName == "crash-reports/CR-20260909-120000-ABCD.json");
        Assert.Contains(zip.Entries, entry => entry.FullName == "crash-reports/CR-20260909-120000-ABCD.additional.log");
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName == "crash-reports/CR-20260901-120000-ABCD.json");
    }

    [Fact]
    public void GetCrashReportDirectories_WithCustomRoot_ReturnsPrimaryUnderRootAndTempFallback()
    {
        var directories = new CrashReportStore(TestDataRoot.ForCurrentProcess())
            .GetCrashReportDirectories(Path.Combine(tempDir, "root"))
            .ToList();

        Assert.Equal(2, directories.Count);
        Assert.Equal(
            Path.Combine(tempDir, "root", LauncherDataRoot.CrashReportsFolderName),
            directories[0]);
        Assert.Equal(CrashReportStore.DefaultFallbackDirectory, directories[1]);
    }

    [Fact]
    public async Task ExportAsync_WhenLocatorSuppliesCustomDirectory_CollectsCrashReportsFromIt()
    {
        // 定位器接缝：导出器的崩溃区来源由注入决定，导出器自身无需真实目录约定。
        var customDirectory = Path.Combine(tempDir, "custom-crash-location");
        Directory.CreateDirectory(customDirectory);
        var reportPath = Path.Combine(customDirectory, "CR-20260909-120000-ABCD.json");
        File.WriteAllText(reportPath, "{}");
        File.SetLastWriteTimeUtc(reportPath, DateTime.UtcNow.AddHours(-1));
        var logger = WriteDeterministicLog("locator-source", $"{DateTimeOffset.Now.AddMinutes(-5):O} [INF] [Test] Entry\n");
        var service = new LogExportService(
            new LocalDiagnostics(logger), TestDataRoot.ForCurrentProcess(), new StubCrashReportLocator(customDirectory));

        var zipPath = await service.ExportAsync(
            Path.Combine(tempDir, "locator-selected"),
            new LogExportOptions
            {
                Range = LogExportRangePreset.Last24Hours,
                IncludeCrashReports = true
            });

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(
            zip.Entries,
            entry => entry.FullName == "crash-reports/CR-20260909-120000-ABCD.json");
    }

    private sealed class StubCrashReportLocator(params string[] directories) : ICrashReportLocator
    {
        public IEnumerable<string> GetCrashReportDirectories(string userDataRoot) => directories;
    }

    [Fact]
    public async Task ExportAsync_WithUserData_BundlesLauncherStateFilesOnly()
    {
        var dataRoot = Path.Combine(tempDir, "user-data-root");
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(dataRoot, "settings.json"), "{\"logLevel\":\"information\"}");
        File.WriteAllText(Path.Combine(dataRoot, "download_state.json"), "{}");
        File.WriteAllText(Path.Combine(dataRoot, "shown_notices.json"), "[]");
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
        var service = new LogExportService(new LocalDiagnostics(logger), TestDataRoot.ForDirectory(dataRoot) , new CrashReportStore(TestDataRoot.ForCurrentProcess()));

        var zipPath = await service.ExportAsync(
            Path.Combine(tempDir, "user-data-selected"),
            new LogExportOptions { IncludeUserData = true });

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, entry => entry.FullName == "user-data/settings.json");
        Assert.Contains(zip.Entries, entry => entry.FullName == "user-data/download_state.json");
        Assert.Contains(zip.Entries, entry => entry.FullName == "user-data/shown_notices.json");
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
        var crashDirectory = Path.Combine(dataRoot, LauncherDataRoot.CrashReportsFolderName);
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
        var service = new LogExportService(new LocalDiagnostics(logger), TestDataRoot.ForDirectory(dataRoot) , new CrashReportStore(TestDataRoot.ForCurrentProcess()));

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
        var service = new LogExportService(new LocalDiagnostics(logger), TestDataRoot.ForDirectory(dataRoot) , new CrashReportStore(TestDataRoot.ForCurrentProcess()));

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
    public async Task ExportAsync_RecordsTheDesktopSessionInSystemInfo()
    {
        const string variable = "XDG_SESSION_TYPE";
        var original = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "wayland");
        try
        {
            var dataRoot = Path.Combine(tempDir, "session-data");
            Directory.CreateDirectory(dataRoot);
            var logger = WriteDeterministicLog(
                "session-source",
                "2026-09-09T10:00:00.0000000+08:00 [INF] [Test] Entry\n");
            var service = new LogExportService(
                new LocalDiagnostics(logger),
                TestDataRoot.ForDirectory(dataRoot),
                new CrashReportStore(TestDataRoot.ForCurrentProcess()));

            var zipPath = await service.ExportAsync(
                Path.Combine(tempDir, "session-selected"),
                LogExportOptions.Default);

            using var document = JsonDocument.Parse(ReadEntry(zipPath, "system-info.json"));
            var session = document.RootElement.GetProperty("session");
            Assert.Equal("wayland", session.GetProperty("type").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }
    }

    [Fact]
    public async Task ExportAsync_WithAGraphicsProbe_RecordsTheGpuAndOpenGlLines()
    {
        var dataRoot = Path.Combine(tempDir, "graphics-data");
        Directory.CreateDirectory(dataRoot);
        var logger = WriteDeterministicLog(
            "graphics-source",
            "2026-09-09T10:00:00.0000000+08:00 [INF] [Test] Entry\n");
        var probe = new GraphicsInfoProbe((tool, _, _) => tool == "vulkaninfo"
            ? "GPU0:\n\tdeviceName = Test GPU\n"
            : "OpenGL renderer string: Test Renderer\n");
        var service = new LogExportService(
            new LocalDiagnostics(logger),
            TestDataRoot.ForDirectory(dataRoot),
            new CrashReportStore(TestDataRoot.ForCurrentProcess()),
            probe);

        var zipPath = await service.ExportAsync(
            Path.Combine(tempDir, "graphics-selected"),
            LogExportOptions.Default);

        using var document = JsonDocument.Parse(ReadEntry(zipPath, "system-info.json"));
        var graphics = document.RootElement.GetProperty("graphics");
        Assert.Contains(
            "deviceName = Test GPU",
            graphics.GetProperty("vulkan").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "Test Renderer",
            graphics.GetProperty("opengl").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task HasLogEntriesAsync_WithEntriesInsideTheWindow_ReturnsTrue()
    {
        var now = DateTimeOffset.Now;
        var logger = WriteDeterministicLog(
            "probe-inside-source",
            $"{now.AddHours(-2):O} [INF] [Test] Old entry\n" +
            $"{now.AddMinutes(-5):O} [INF] [Test] Recent entry\n");
        var service = new LogExportService(new LocalDiagnostics(logger), TestDataRoot.ForCurrentProcess(), new CrashReportStore(TestDataRoot.ForCurrentProcess()) );

        var hasEntries = await service.HasLogEntriesAsync(
            new LogExportOptions { Range = LogExportRangePreset.LastHour });

        Assert.True(hasEntries);
    }

    [Fact]
    public async Task HasLogEntriesAsync_WithEntriesOutsideTheWindow_ReturnsFalse()
    {
        var logger = WriteDeterministicLog(
            "probe-outside-source",
            $"{DateTimeOffset.Now.AddHours(-2):O} [INF] [Test] Old entry\n");
        var service = new LogExportService(new LocalDiagnostics(logger), TestDataRoot.ForCurrentProcess(), new CrashReportStore(TestDataRoot.ForCurrentProcess()) );

        var hasEntries = await service.HasLogEntriesAsync(
            new LogExportOptions { Range = LogExportRangePreset.LastHour });

        Assert.False(hasEntries);
    }

    [Fact]
    public async Task HasLogEntriesAsync_WithoutARange_WhenTheLogHasEntries_ReturnsTrue()
    {
        // Only old entries, so a window would say "nothing": an unbounded range keeps whatever the
        // files hold and must not be reported as empty.
        var logger = WriteDeterministicLog(
            "probe-unbounded-source",
            "2026-09-01T10:00:00.0000000+08:00 [INF] [Test] Old entry\n");
        var service = new LogExportService(new LocalDiagnostics(logger), TestDataRoot.ForCurrentProcess(), new CrashReportStore(TestDataRoot.ForCurrentProcess()) );

        Assert.True(await service.HasLogEntriesAsync(LogExportOptions.Default));
    }

    [Fact]
    public async Task HasLogEntriesAsync_WithoutARange_WhenTheLogIsEmpty_ReturnsFalse()
    {
        var logger = WriteDeterministicLog("probe-empty-source", "");
        var service = new LogExportService(new LocalDiagnostics(logger), TestDataRoot.ForCurrentProcess(), new CrashReportStore(TestDataRoot.ForCurrentProcess()) );

        Assert.False(await service.HasLogEntriesAsync(LogExportOptions.Default));
    }

    [Fact]
    public async Task HasLogEntriesAsync_WhenTheLogFileIsMissing_ReturnsFalse()
    {
        using var logger = new UnifiedLogger(Path.Combine(tempDir, "probe-missing-source"));
        var service = new LogExportService(new LocalDiagnostics(logger), TestDataRoot.ForCurrentProcess(), new CrashReportStore(TestDataRoot.ForCurrentProcess()) );

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
        tempDir.Dispose();
    }
}
