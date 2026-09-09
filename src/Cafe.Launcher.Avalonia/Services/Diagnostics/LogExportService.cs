using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>
/// Creates a ZIP archive containing log files, a system-info summary, and — when the caller
/// opts in — crash reports and launcher user data, for offline diagnostic review.
/// </summary>
public sealed class LogExportService
{
    private const int MaxRetainedLogFiles = 3;

    /// <summary>Launcher-owned files under the user data root that a user-data export bundles.</summary>
    private static readonly string[] UserDataFileNames =
    [
        GamePaths.LauncherSettingsFileName,
        GamePaths.DownloadStateFileName,
        "shown_notices.json",
        "clickCode"
    ];

    private readonly UnifiedLogger logger;
    private readonly string userDataRoot;

    public static string DefaultExportDirectory => Path.Combine(
        LauncherUserDataDirectory.Root,
        LauncherConstants.LogExportFolderName);

    public LogExportService(UnifiedLogger logger)
        : this(logger, LauncherUserDataDirectory.Root)
    {
    }

    internal LogExportService(UnifiedLogger logger, string userDataRoot)
    {
        this.logger = logger;
        this.userDataRoot = userDataRoot;
    }

    /// <summary>
    /// Exports the items selected by <paramref name="options"/> to a timestamped ZIP in
    /// <paramref name="destinationDirectory"/>. Returns the created ZIP path.
    /// </summary>
    public async Task<string> ExportAsync(
        string destinationDirectory,
        LogExportOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsCustomRangeValid)
        {
            throw new ArgumentException(
                "The export range start must not be later than its end.",
                nameof(options));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);
        var zipPath = CreateAvailableZipPath(destinationDirectory);

        await Task.Run(() => CreateZip(zipPath, options), ct).ConfigureAwait(false);
        return zipPath;
    }

    private static string CreateAvailableZipPath(string destinationDirectory)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var zipPath = Path.Combine(destinationDirectory, $"CafeLauncher_Logs_{timestamp}.zip");
        for (var suffix = 2; File.Exists(zipPath); suffix++)
        {
            zipPath = Path.Combine(
                destinationDirectory,
                $"CafeLauncher_Logs_{timestamp}_{suffix}.zip");
        }

        return zipPath;
    }

    private void CreateZip(string zipPath, LogExportOptions options)
    {
        var logDir = Path.GetDirectoryName(logger.LogFilePath)!;
        var (from, to) = options.ResolveWindow(DateTimeOffset.Now);
        var includedEntries = new List<string>();

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        // Current log file, then the rotated files (Serilog naming: unified_001.log, …).
        AddLogFile(zip, logger.LogFilePath, "unified.log", required: true, from, to, includedEntries);
        for (var i = 1; i <= MaxRetainedLogFiles; i++)
        {
            var rotatedPath = Path.Combine(logDir, $"unified_{i:D3}.log");
            if (File.Exists(rotatedPath))
                AddLogFile(zip, rotatedPath, $"unified_{i:D3}.log", required: false, from, to, includedEntries);
        }

        if (options.IncludeCrashReports)
            AddCrashReports(zip, CrashReportDirectories(), from, to, includedEntries);

        if (options.IncludeUserData)
            AddUserData(zip, includedEntries);

        AddSystemInfo(zip, options, from, to, includedEntries);
    }

    private static void AddLogFile(
        ZipArchive zip,
        string filePath,
        string entryName,
        bool required,
        DateTimeOffset? from,
        DateTimeOffset? to,
        List<string> includedEntries)
    {
        if (!File.Exists(filePath))
        {
            if (required)
                throw new FileNotFoundException("The current unified log file was not found.", filePath);
            return;
        }

        try
        {
            if (from is null && to is null)
            {
                CopyFileToZip(zip, filePath, entryName);
            }
            else if (!AddFilteredLogToZip(zip, filePath, entryName, from, to))
            {
                // Nothing in range: the export stays truthful by omitting the file.
                return;
            }

            includedEntries.Add(entryName);
        }
        catch
        {
            if (required)
                throw;
        }
    }

    /// <summary>Writes only the entries inside the window, keeping continuation lines with their entry.</summary>
    private static bool AddFilteredLogToZip(
        ZipArchive zip,
        string filePath,
        string entryName,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        var keptLines = new List<string>();
        using (var source = OpenSharedRead(filePath))
        using (var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
                lines.Add(line);

            foreach (var record in LogEntryReader.Read(lines))
            {
                if (LogExportOptions.Contains(record.Timestamp, from, to))
                    keptLines.AddRange(record.Lines);
            }
        }

        if (keptLines.Count == 0)
            return false;

        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var destination = entry.Open();
        using var writer = new StreamWriter(destination, Utf8NoBom);
        foreach (var line in keptLines)
            writer.WriteLine(line);

        return true;
    }

    private static void AddCrashReports(
        ZipArchive zip,
        IEnumerable<string> crashReportDirectories,
        DateTimeOffset? from,
        DateTimeOffset? to,
        List<string> includedEntries)
    {
        foreach (var directory in crashReportDirectories)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.Ordinal))
            {
                if (!IsWithinWindow(File.GetLastWriteTimeUtc(file), from, to))
                    continue;

                var entryName = $"crash-reports/{Path.GetFileName(file)}";
                if (TryCopyFileToZip(zip, file, entryName))
                    includedEntries.Add(entryName);
            }
        }
    }

    /// <summary>Crash artifacts live under the user data root, with a temp fallback when that root is unwritable.</summary>
    private IEnumerable<string> CrashReportDirectories()
    {
        yield return Path.Combine(userDataRoot, CrashReportStore.ReportDirectoryName);
        yield return Path.Combine(Path.GetTempPath(), "Cafe.Launcher", CrashReportStore.ReportDirectoryName);
    }

    /// <summary>
    /// Bundles the current user-data snapshot. Unlike logs and crash reports these files are
    /// state rather than a time series, so the export range does not filter them.
    /// </summary>
    private void AddUserData(ZipArchive zip, List<string> includedEntries)
    {
        foreach (var fileName in UserDataFileNames)
        {
            var entryName = $"user-data/{fileName}";
            if (TryCopyFileToZip(zip, Path.Combine(userDataRoot, fileName), entryName))
                includedEntries.Add(entryName);
        }
    }

    private static void AddSystemInfo(
        ZipArchive zip,
        LogExportOptions options,
        DateTimeOffset? from,
        DateTimeOffset? to,
        IReadOnlyList<string> includedEntries)
    {
        var systemInfo = new
        {
            timestamp = DateTimeOffset.Now.ToString("O"),
            version = BuildInfo.LauncherVersion,
            commitSha = BuildInfo.CommitSha,
            os = Environment.OSVersion.ToString(),
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            buildConfig = BuildInfo.BuildConfiguration,
            export = new
            {
                range = options.Range.ToString(),
                from = from?.ToString("O"),
                to = to?.ToString("O"),
                includeCrashReports = options.IncludeCrashReports,
                includeUserData = options.IncludeUserData,
                entries = includedEntries
            }
        };
        var json = JsonSerializer.Serialize(systemInfo, JsonDefaults.Indented);
        var entry = zip.CreateEntry("system-info.json");
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(json);
    }

    private static bool TryCopyFileToZip(ZipArchive zip, string filePath, string entryName)
    {
        if (!File.Exists(filePath))
            return false;

        try
        {
            CopyFileToZip(zip, filePath, entryName);
            return true;
        }
        catch
        {
            // Optional items never fail the export.
            return false;
        }
    }

    private static void CopyFileToZip(ZipArchive zip, string filePath, string entryName)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var source = OpenSharedRead(filePath);
        using var destination = entry.Open();
        source.CopyTo(destination);
    }

    private static FileStream OpenSharedRead(string filePath) => new(
        filePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete);

    private static bool IsWithinWindow(DateTime lastWriteUtc, DateTimeOffset? from, DateTimeOffset? to) =>
        LogExportOptions.Contains(new DateTimeOffset(lastWriteUtc, TimeSpan.Zero), from, to);

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
