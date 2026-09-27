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
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Core;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Helpers;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>Synchronously persists share-safe crash snapshots for the isolated reporter.</summary>
public sealed class CrashReportStore : ICrashReportLocator
{
    internal const int RetainedReportCount = 10;
    internal static readonly TimeSpan RetentionAge = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string primaryDirectory;
    private readonly string fallbackDirectory;
    private readonly LauncherBuildIdentity? buildIdentity;

    /// <summary>Temp location the store falls back to when the user-data root is unwritable.</summary>
    public static string DefaultFallbackDirectory => Path.Combine(
        Path.GetTempPath(),
        "Cafe.Launcher",
        LauncherDataRoot.CrashReportsFolderName);

    /// <summary>
    /// 生产接线显式传入 <see cref="DefaultFallbackDirectory"/>；测试通常省略，
    /// 让回退目录等于传入的根。
    /// </summary>
    /// <param name="buildIdentity">
    /// 宿主注入的构建标识：崩溃报告的版本/提交以前读宿主 <c>BuildInfo</c>，迁出宿主前改为注入。
    /// </param>
    public CrashReportStore(
        LauncherDataRoot dataRoot,
        string? fallbackDirectory = null,
        LauncherBuildIdentity? buildIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        this.primaryDirectory = dataRoot.CrashReportsDirectory;
        this.fallbackDirectory = Path.GetFullPath(fallbackDirectory ?? this.primaryDirectory);
        this.buildIdentity = buildIdentity;
    }

    internal string PrimaryDirectory => primaryDirectory;

    /// <inheritdoc />
    public IEnumerable<string> GetCrashReportDirectories(string userDataRoot)
    {
        yield return Path.Combine(userDataRoot, LauncherDataRoot.CrashReportsFolderName);
        yield return DefaultFallbackDirectory;
    }

    /// <summary>Creates and synchronously writes a crash snapshot, falling back to the temp directory.</summary>
    public CrashReport Create(CrashOrigin origin, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var source = origin.ToSourceLabel();
        var now = DateTimeOffset.Now;
        var id = string.Create(
            CultureInfo.InvariantCulture,
            $"CR-{now:yyyyMMdd-HHmmss}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(2))}");
        var report = CrashReport.Build(
            id,
            now,
            source,
            $"{RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture}",
            exception.GetType().FullName ?? exception.GetType().Name,
            BuildTechnicalDetails(id, now, source, exception),
            buildIdentity);

        Exception? primaryFailure = null;
        try
        {
            return Write(report, primaryDirectory);
        }
        catch (Exception writeException) when (StorageFailure.IsRecoverable(writeException))
        {
            primaryFailure = writeException;
        }

        try
        {
            return Write(report, fallbackDirectory);
        }
        catch (Exception fallbackFailure) when (StorageFailure.IsRecoverable(fallbackFailure))
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
        catch (Exception exception) when (StorageFailure.IsRecoverable(exception))
        {
            return report;
        }
    }

    /// <summary>Reads a snapshot created by <see cref="Create"/> without depending on application DI.</summary>
    public static CrashReport? TryRead(string? path)
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
        catch (Exception appendException) when (StorageFailure.IsRecoverable(appendException))
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
        catch (Exception cleanupException) when (StorageFailure.IsRecoverable(cleanupException))
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

    private string BuildTechnicalDetails(
        string id,
        DateTimeOffset occurredAt,
        string source,
        Exception exception)
    {
        var details = new StringBuilder()
            .Append("Crash ID: ").AppendLine(id)
            .Append("Time: ").AppendLine(occurredAt.ToString("O", CultureInfo.InvariantCulture))
            .Append("Version: ").AppendLine(buildIdentity?.LauncherVersion ?? "")
            .Append("Commit: ").AppendLine(buildIdentity?.CommitSha ?? "")
            .Append("OS: ").Append(RuntimeInformation.OSDescription).Append(" · ")
            .AppendLine(RuntimeInformation.OSArchitecture.ToString())
            .Append("Source: ").AppendLine(source)
            .AppendLine()
            .Append(exception)
            .ToString();
        return Sanitize(details);
    }
}
