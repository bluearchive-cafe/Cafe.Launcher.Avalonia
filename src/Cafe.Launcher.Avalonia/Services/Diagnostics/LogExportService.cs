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
    /// <summary>Log title of every diagnostic this service writes.</summary>
    private const string LogTitle = "LogExport";

    private const int MaxRetainedLogFiles = 3;

    /// <summary>Launcher-owned files under the user data root that a user-data export bundles.</summary>
    private static readonly string[] UserDataFileNames =
    [
        GamePaths.LauncherSettingsFileName,
        GamePaths.DownloadStateFileName,
        GamePaths.NoticeStateFileName,
        GamePaths.ClickCodeFileName
    ];

    private readonly LocalDiagnostics diagnostics;
    private readonly string userDataRoot;

    public static string DefaultExportDirectory => Path.Combine(
        LauncherUserDataDirectory.Root,
        LauncherConstants.LogExportFolderName);

    public LogExportService(LocalDiagnostics diagnostics)
        : this(diagnostics, LauncherUserDataDirectory.Root)
    {
    }

    internal LogExportService(LocalDiagnostics diagnostics, string userDataRoot)
    {
        this.diagnostics = diagnostics;
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

        var manifest = await Task.Run(() => CreateZip(zipPath, options), ct).ConfigureAwait(false);

        // Reported once the archive is closed: the manifest lists what was skipped so whoever
        // opens the ZIP can see the omission, and the log keeps the reason behind it.
        foreach (var skip in manifest.Skipped)
        {
            await diagnostics.WarningAsync(
                LogTitle,
                $"Skipped {skip.EntryName}: {skip.Failure.Message}",
                CancellationToken.None).ConfigureAwait(false);
        }

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

    private ExportManifest CreateZip(string zipPath, LogExportOptions options)
    {
        var logFilePath = diagnostics.LogFilePath;
        var logDir = Path.GetDirectoryName(logFilePath)!;
        var manifest = new ExportManifest(options.ResolveWindow(DateTimeOffset.Now));

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        // Current log file, then the rotated files (Serilog naming: unified_001.log, …).
        AddLogFile(zip, logFilePath, "unified.log", required: true, manifest);
        for (var i = 1; i <= MaxRetainedLogFiles; i++)
        {
            var rotatedPath = Path.Combine(logDir, $"unified_{i:D3}.log");
            if (File.Exists(rotatedPath))
                AddLogFile(zip, rotatedPath, $"unified_{i:D3}.log", required: false, manifest);
        }

        if (options.IncludeCrashReports)
            AddCrashReports(zip, CrashReportDirectories(), manifest);

        if (options.IncludeUserData)
            AddUserData(zip, manifest);

        AddSystemInfo(zip, options, manifest);
        return manifest;
    }

    private static void AddLogFile(
        ZipArchive zip,
        string filePath,
        string entryName,
        bool required,
        ExportManifest manifest)
    {
        if (!File.Exists(filePath))
        {
            if (required)
                throw new FileNotFoundException("The current unified log file was not found.", filePath);
            return;
        }

        try
        {
            if (manifest.Window.IsUnbounded)
            {
                CopyFileToZip(zip, filePath, entryName);
            }
            else if (!AddFilteredLogToZip(zip, filePath, entryName, manifest))
            {
                // Nothing in range: the export stays truthful by omitting the file.
                return;
            }

            manifest.Entries.Add(entryName);
        }
        catch (Exception exception)
        {
            // The current log is the export's reason to exist; a rotated one is not.
            if (required)
                throw;

            manifest.Skipped.Add(new SkippedItem(entryName, exception));
        }
    }

    /// <summary>Writes only the entries inside the window, keeping continuation lines with their entry.</summary>
    private static bool AddFilteredLogToZip(
        ZipArchive zip,
        string filePath,
        string entryName,
        ExportManifest manifest)
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
                if (manifest.Window.Contains(record.Timestamp))
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
        ExportManifest manifest)
    {
        foreach (var directory in crashReportDirectories)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.Ordinal))
            {
                if (!manifest.Window.ContainsFileWrittenAt(File.GetLastWriteTimeUtc(file)))
                    continue;

                TryCopyOptionalFileToZip(zip, file, $"crash-reports/{Path.GetFileName(file)}", manifest);
            }
        }
    }

    /// <summary>
    /// Crash artifacts live under the user data root, with a temp fallback when that root is
    /// unwritable. The fallback comes from <see cref="CrashReportStore"/> so the exporter and the
    /// store cannot drift apart and silently lose the reports written there. The primary
    /// directory is derived from this service's own root, which tests override.
    /// </summary>
    private IEnumerable<string> CrashReportDirectories()
    {
        yield return Path.Combine(userDataRoot, CrashReportStore.ReportDirectoryName);
        yield return CrashReportStore.DefaultFallbackDirectory;
    }

    /// <summary>
    /// Bundles the current user-data snapshot. Unlike logs and crash reports these files are
    /// state rather than a time series, so the export range does not filter them.
    /// </summary>
    private void AddUserData(ZipArchive zip, ExportManifest manifest)
    {
        foreach (var fileName in UserDataFileNames)
        {
            TryCopyOptionalFileToZip(
                zip,
                Path.Combine(userDataRoot, fileName),
                $"user-data/{fileName}",
                manifest);
        }
    }

    private static void AddSystemInfo(
        ZipArchive zip,
        LogExportOptions options,
        ExportManifest manifest)
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
                from = manifest.Window.From?.ToString("O"),
                to = manifest.Window.To?.ToString("O"),
                includeCrashReports = options.IncludeCrashReports,
                includeUserData = options.IncludeUserData,
                entries = manifest.Entries,
                skipped = manifest.Skipped.Select(skip => new
                {
                    entry = skip.EntryName,
                    reason = skip.Failure.GetType().Name
                })
            }
        };
        var json = JsonSerializer.Serialize(systemInfo, JsonDefaults.Indented);
        var entry = zip.CreateEntry("system-info.json");
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(json);
    }

    /// <summary>
    /// Adds an artifact whose loss must not fail the export. A missing file is a normal absence
    /// (a fresh install has no crash reports); a file that cannot be read is recorded as skipped.
    /// </summary>
    private static void TryCopyOptionalFileToZip(
        ZipArchive zip,
        string filePath,
        string entryName,
        ExportManifest manifest)
    {
        if (!File.Exists(filePath))
            return;

        try
        {
            CopyFileToZip(zip, filePath, entryName);
            manifest.Entries.Add(entryName);
        }
        catch (Exception exception)
        {
            // Optional items never fail the export.
            manifest.Skipped.Add(new SkippedItem(entryName, exception));
        }
    }

    private static void CopyFileToZip(ZipArchive zip, string filePath, string entryName)
    {
        // The source is opened before the entry is created: CreateEntry registers the entry in
        // the archive right away, so a failed open would otherwise leave an empty file behind
        // that still looks like the artifact it was meant to be.
        using var source = OpenSharedRead(filePath);
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var destination = entry.Open();
        source.CopyTo(destination);
    }

    private static FileStream OpenSharedRead(string filePath) => new(
        filePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete);

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// The window every exported artifact is measured against, plus the running record of what the
    /// archive holds. Threaded through the add helpers so the window and the record cannot fall
    /// out of step with each other.
    /// </summary>
    private sealed class ExportManifest(ExportWindow window)
    {
        /// <summary>Gets the window every exported artifact is measured against.</summary>
        public ExportWindow Window { get; } = window;

        /// <summary>Gets the names of the entries written to the archive.</summary>
        public List<string> Entries { get; } = [];

        /// <summary>Gets the requested artifacts that could not be read.</summary>
        public List<SkippedItem> Skipped { get; } = [];
    }

    /// <summary>One requested artifact absent from the archive and the failure that kept it out.</summary>
    private sealed record SkippedItem(string EntryName, Exception Failure);
}
