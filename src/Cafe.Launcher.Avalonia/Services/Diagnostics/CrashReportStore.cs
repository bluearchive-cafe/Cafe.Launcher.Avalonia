using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>Synchronously persists share-safe crash snapshots for the isolated reporter.</summary>
public sealed class CrashReportStore
{
    internal const string ReportDirectoryName = "CrashReports";
    internal const int RetainedReportCount = 10;
    internal static readonly TimeSpan RetentionAge = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string primaryDirectory;
    private readonly string fallbackDirectory;

    public CrashReportStore()
        : this(
            Path.Combine(LauncherUserDataDirectory.Root, ReportDirectoryName),
            Path.Combine(Path.GetTempPath(), "Cafe.Launcher", ReportDirectoryName))
    {
    }

    internal CrashReportStore(string primaryDirectory, string? fallbackDirectory = null)
    {
        this.primaryDirectory = Path.GetFullPath(primaryDirectory);
        this.fallbackDirectory = Path.GetFullPath(fallbackDirectory ?? primaryDirectory);
    }

    internal string PrimaryDirectory => primaryDirectory;

    /// <summary>Creates and synchronously writes a crash snapshot, falling back to the temp directory.</summary>
    public CrashReport Create(CrashOrigin origin, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var source = origin.ToSourceLabel();
        var now = DateTimeOffset.Now;
        var id = string.Create(
            CultureInfo.InvariantCulture,
            $"CR-{now:yyyyMMdd-HHmmss}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(2))}");
        var report = new CrashReport
        {
            Id = id,
            OccurredAt = now,
            Source = source,
            AppVersion = BuildInfo.LauncherVersion,
            OperatingSystem = $"{RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture}",
            UiCulture = CultureInfo.CurrentUICulture.Name,
            ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
            TechnicalDetails = BuildTechnicalDetails(id, now, source, exception)
        };

        Exception? primaryFailure = null;
        try
        {
            return Write(report, primaryDirectory);
        }
        catch (Exception writeException) when (writeException is IOException or UnauthorizedAccessException)
        {
            primaryFailure = writeException;
        }

        try
        {
            return Write(report, fallbackDirectory);
        }
        catch (Exception fallbackFailure) when (fallbackFailure is IOException or UnauthorizedAccessException)
        {
            throw new AggregateException("Crash snapshot could not be persisted.", primaryFailure, fallbackFailure);
        }
    }

    /// <summary>
    /// Persists an in-memory snapshot after <see cref="Create"/> failed, so the isolated
    /// reporter still receives a readable file. Returns the report unchanged when the
    /// temp directory is also unwritable; the caller then reports without a snapshot.
    /// </summary>
    internal CrashReport TryPersistTransient(CrashReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        try
        {
            return Write(report, fallbackDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return report;
        }
    }

    /// <summary>Reads a snapshot created by <see cref="Create"/> without depending on application DI.</summary>
    public static CrashReport? TryRead(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var json = File.ReadAllText(fullPath, Encoding.UTF8);
            var report = JsonSerializer.Deserialize<CrashReport>(json, SerializerOptions);
            return report is null ? null : report with { SnapshotPath = fullPath };
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or JsonException
                                           or NotSupportedException
                                           or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Records a later failure without replacing or multiplying the primary snapshot.</summary>
    public void AppendAdditionalFailure(CrashReport report, CrashOrigin origin, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(exception);

        if (string.IsNullOrWhiteSpace(report.SnapshotPath))
        {
            return;
        }

        try
        {
            var additionalPath = Path.ChangeExtension(report.SnapshotPath, ".additional.log");
            var entry = new StringBuilder()
                .Append("--- ")
                .Append(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture))
                .Append(" [")
                .Append(origin.ToSourceLabel())
                .AppendLine("] ---")
                .AppendLine(Sanitize(exception.ToString()))
                .ToString();
            File.AppendAllText(additionalPath, entry, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception appendException) when (appendException is IOException or UnauthorizedAccessException)
        {
            // The primary snapshot already exists; a secondary failure must never recurse.
        }
    }

    /// <summary>Removes stale snapshots during a later healthy startup, never on the crash path.</summary>
    public void CleanupOldReports(DateTimeOffset? now = null)
    {
        PruneDirectory(primaryDirectory, now);
        if (!string.Equals(primaryDirectory, fallbackDirectory, StringComparison.OrdinalIgnoreCase))
        {
            // Snapshots written while the primary directory was unwritable live in the
            // fallback directory and would otherwise never expire.
            PruneDirectory(fallbackDirectory, now);
        }
    }

    private static void PruneDirectory(string directoryPath, DateTimeOffset? now)
    {
        try
        {
            var directory = new DirectoryInfo(directoryPath);
            if (!directory.Exists)
            {
                return;
            }

            var cutoff = (now ?? DateTimeOffset.Now) - RetentionAge;
            var reports = directory
                .EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();

            var retained = 0;
            foreach (var report in reports)
            {
                var isExpired = report.LastWriteTimeUtc < cutoff.UtcDateTime;
                if (!isExpired && retained < RetainedReportCount)
                {
                    retained++;
                    continue;
                }

                report.Delete();
                var additionalPath = Path.ChangeExtension(report.FullName, ".additional.log");
                if (File.Exists(additionalPath))
                {
                    File.Delete(additionalPath);
                }
            }
        }
        catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
        {
            // Retention is maintenance only and must never prevent startup.
        }
    }

    internal static string Sanitize(string value, string? userProfileOverride = null)
    {
        var userProfile = userProfileOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return value;
        }

        return value.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    private static CrashReport Write(CrashReport report, string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{report.Id}.json");
        var persisted = report with { SnapshotPath = path };
        var json = JsonSerializer.Serialize(persisted, SerializerOptions);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return persisted;
    }

    private static string BuildTechnicalDetails(
        string id,
        DateTimeOffset occurredAt,
        string source,
        Exception exception)
    {
        var details = new StringBuilder()
            .Append("Crash ID: ").AppendLine(id)
            .Append("Time: ").AppendLine(occurredAt.ToString("O", CultureInfo.InvariantCulture))
            .Append("Version: ").AppendLine(BuildInfo.LauncherVersion)
            .Append("OS: ").Append(RuntimeInformation.OSDescription).Append(" · ")
            .AppendLine(RuntimeInformation.OSArchitecture.ToString())
            .Append("Source: ").AppendLine(source)
            .AppendLine()
            .Append(exception)
            .ToString();
        return Sanitize(details);
    }
}
