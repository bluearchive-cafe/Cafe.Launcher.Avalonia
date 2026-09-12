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
    /// <summary>
    /// Log title of every diagnostic the log export feature writes, shared with the export
    /// dialog so both halves of the feature tag their entries identically.
    /// </summary>
    internal const string LogTitle = "LogExport";

    private const int MaxRetainedLogFiles = 3;

    /// <summary>Launcher-owned files under the user data root that a user-data export bundles.</summary>
    private static readonly string[] UserDataFileNames =
    [
        GamePaths.LauncherSettingsFileName,
        GamePaths.DownloadStateFileName,
        GamePaths.NoticeStateFileName
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
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);
        var zipPath = CreateAvailableZipPath(destinationDirectory);
        var partialPath = $"{zipPath}.{Guid.NewGuid():N}.partial";
        ExportManifest manifest;
        try
        {
            manifest = await Task.Run(() => CreateZip(partialPath, options, ct), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            File.Move(partialPath, zipPath);
        }
        catch
        {
            TryDeletePartialArchive(partialPath);
            throw;
        }

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

    private static void TryDeletePartialArchive(string partialPath)
    {
        try
        {
            File.Delete(partialPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Preserve the original export failure. The partial suffix makes any cleanup failure
            // distinguishable from a completed diagnostic archive.
        }
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

    /// <summary>
    /// Gets whether the log files hold at least one entry inside the window
    /// <paramref name="options"/> resolves to. The dialog asks this before anything is written, so a
    /// range that would leave the package without a single log entry is called out while the user
    /// can still widen it. The unbounded window is also inspected so an empty or missing log is
    /// reported accurately for the default selection.
    /// </summary>
    public async Task<bool> HasLogEntriesAsync(
        LogExportOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var window = options.ResolveWindow(DateTimeOffset.Now);
        return await Task.Run(
            () => LogFiles().Any(log => ContainsAnyEntry(log.FilePath, window, ct)),
            ct).ConfigureAwait(false);
    }

    private static bool ContainsAnyEntry(
        string filePath,
        ExportWindow window,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            return false;

        try
        {
            return ReadLinesInWindow(filePath, window, cancellationToken).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Unreadable means "nothing to show" as far as the hint is concerned; the export
            // itself is where the failure gets reported.
            return false;
        }
    }

    /// <summary>The current log file followed by the rotated files the export considers.</summary>
    private IEnumerable<(string FilePath, string EntryName, bool Required)> LogFiles()
    {
        var logFilePath = diagnostics.LogFilePath;
        yield return (logFilePath, "unified.log", true);

        var logDirectory = Path.GetDirectoryName(logFilePath)!;
        for (var i = 1; i <= MaxRetainedLogFiles; i++)
        {
            var entryName = $"unified_{i:D3}.log";
            yield return (Path.Combine(logDirectory, entryName), entryName, false);
        }
    }

    private ExportManifest CreateZip(
        string zipPath,
        LogExportOptions options,
        CancellationToken cancellationToken)
    {
        var manifest = new ExportManifest(options.ResolveWindow(DateTimeOffset.Now));

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        foreach (var log in LogFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddLogFile(zip, log.FilePath, log.EntryName, log.Required, manifest, cancellationToken);
        }

        if (options.IncludeCrashReports)
            AddCrashReports(zip, CrashReportDirectories(), manifest, cancellationToken);

        if (options.IncludeUserData)
            AddUserData(zip, manifest, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        AddSystemInfo(zip, options, manifest);
        return manifest;
    }

    private static void AddLogFile(
        ZipArchive zip,
        string filePath,
        string entryName,
        bool required,
        ExportManifest manifest,
        CancellationToken cancellationToken)
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
                CopyFileToZip(zip, filePath, entryName, cancellationToken);
            }
            else
            {
                var keptLines = ReadLinesInWindow(filePath, manifest.Window, cancellationToken).ToList();
                // The dialog promises the log file is always part of the export, so the current
                // log is written even when the window holds nothing: an empty file tells the
                // reader the range was empty, while a missing one reads as "this package has no
                // logs at all". Rotated files stay out when they contribute nothing.
                if (keptLines.Count == 0 && !required)
                    return;

                WriteLinesToZip(zip, entryName, keptLines, cancellationToken);
            }

            manifest.Entries.Add(entryName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The current log is the export's reason to exist; a rotated one is not.
            if (required)
                throw;

            manifest.Skipped.Add(new SkippedItem(entryName, exception));
        }
    }

    /// <summary>
    /// Enumerates the lines of every entry inside the window, keeping continuation lines with their
    /// entry. The file is streamed rather than buffered, so callers that stop early (the dialog's
    /// range probe asks for the first hit only) never read past the answer. Shared by the export
    /// filter and the probe so the two cannot disagree about what a window holds.
    /// </summary>
    private static IEnumerable<string> ReadLinesInWindow(
        string filePath,
        ExportWindow window,
        CancellationToken cancellationToken)
    {
        using var source = OpenSharedRead(filePath);
        using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        foreach (var record in LogEntryReader.Read(ReadLines(reader, cancellationToken)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!window.Contains(record.Timestamp))
                continue;

            foreach (var line in record.Lines)
                yield return line;
        }
    }

    private static IEnumerable<string> ReadLines(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return line;
        }
    }

    /// <summary>Writes the lines as one entry; an empty sequence still produces an entry, so the file is present.</summary>
    private static void WriteLinesToZip(
        ZipArchive zip,
        string entryName,
        IEnumerable<string> lines,
        CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var destination = entry.Open();
        using var writer = new StreamWriter(destination, Utf8NoBom);
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteLine(line);
        }
    }

    private static void AddCrashReports(
        ZipArchive zip,
        IEnumerable<string> crashReportDirectories,
        ExportManifest manifest,
        CancellationToken cancellationToken)
    {
        foreach (var directory in crashReportDirectories)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!manifest.Window.ContainsFileWrittenAt(File.GetLastWriteTimeUtc(file)))
                    continue;

                TryCopyOptionalFileToZip(
                    zip,
                    file,
                    $"crash-reports/{Path.GetFileName(file)}",
                    manifest,
                    cancellationToken);
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
    private void AddUserData(
        ZipArchive zip,
        ExportManifest manifest,
        CancellationToken cancellationToken)
    {
        foreach (var fileName in UserDataFileNames)
        {
            TryCopyOptionalFileToZip(
                zip,
                Path.Combine(userDataRoot, fileName),
                $"user-data/{fileName}",
                manifest,
                cancellationToken);
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
        ExportManifest manifest,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            return;

        try
        {
            CopyFileToZip(zip, filePath, entryName, cancellationToken);
            manifest.Entries.Add(entryName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Optional items never fail the export.
            manifest.Skipped.Add(new SkippedItem(entryName, exception));
        }
    }

    private static void CopyFileToZip(
        ZipArchive zip,
        string filePath,
        string entryName,
        CancellationToken cancellationToken)
    {
        // The source is opened before the entry is created: CreateEntry registers the entry in
        // the archive right away, so a failed open would otherwise leave an empty file behind
        // that still looks like the artifact it was meant to be.
        using var source = OpenSharedRead(filePath);
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var destination = entry.Open();
        var buffer = new byte[81920];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            destination.Write(buffer, 0, read);
        }
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
