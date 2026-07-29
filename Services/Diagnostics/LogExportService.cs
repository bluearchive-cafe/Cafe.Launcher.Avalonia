using System;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>
/// Creates a ZIP archive containing all log files and system information
/// for offline diagnostic review.
/// </summary>
public sealed class LogExportService
{
    private readonly UnifiedLogger logger;

    public static string DefaultExportDirectory => Path.Combine(
        LauncherUserDataDirectory.Path,
        LauncherConstants.LogExportFolderName);

    public LogExportService(UnifiedLogger logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Exports all log files and a system-info summary to a timestamped ZIP
    /// in <paramref name="destinationDirectory"/>. Returns the created ZIP path.
    /// </summary>
    public async Task<string> ExportAsync(
        string destinationDirectory,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);
        var zipPath = CreateAvailableZipPath(destinationDirectory);

        await Task.Run(() => CreateZip(zipPath), ct).ConfigureAwait(false);
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

    private const int MaxRetainedLogFiles = 3;

    private void CreateZip(string zipPath)
    {
        var logDir = Path.GetDirectoryName(logger.LogFilePath)!;
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        // Add current log file
        AddFileToZip(zip, logger.LogFilePath, "unified.log", required: true);

        // Add rotated log files (Serilog naming: unified_001.log, unified_002.log, …)
        for (var i = 1; i <= MaxRetainedLogFiles; i++)
        {
            var rotatedPath = Path.Combine(logDir, $"unified_{i:D3}.log");
            if (File.Exists(rotatedPath))
                AddFileToZip(zip, rotatedPath, $"unified_{i:D3}.log", required: false);
        }

        // Add system-info summary
        var systemInfo = new
        {
            timestamp = DateTimeOffset.Now.ToString("O"),
            version = BuildInfo.LauncherVersion,
            commitSha = BuildInfo.CommitSha,
            os = Environment.OSVersion.ToString(),
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            buildConfig = BuildInfo.BuildConfiguration
        };
        var json = JsonSerializer.Serialize(systemInfo, JsonDefaults.Indented);
        var entry = zip.CreateEntry("system-info.json");
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(json);
    }

    private static void AddFileToZip(
        ZipArchive zip,
        string filePath,
        string entryName,
        bool required)
    {
        if (!File.Exists(filePath))
        {
            if (required)
                throw new FileNotFoundException("The current unified log file was not found.", filePath);
            return;
        }

        try
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var source = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var destination = entry.Open();
            source.CopyTo(destination);
        }
        catch
        {
            if (required)
                throw;
        }
    }
}
