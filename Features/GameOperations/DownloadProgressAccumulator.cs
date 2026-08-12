using System;
using System.Diagnostics;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// Accumulates every transferred byte while emitting progress snapshots at a
/// lower frequency suitable for the UI.
/// </summary>
internal sealed class DownloadProgressAccumulator
{
    private readonly object sync = new();
    private readonly long totalSize;
    private readonly long timestampFrequency;
    private readonly long reportIntervalTicks;
    private long downloadedSize;
    private long bytesSinceLastReport;
    private long lastReportTimestamp;
    private bool completionReported;

    internal DownloadProgressAccumulator(
        long totalSize,
        long initialDownloadedSize,
        TimeSpan reportInterval)
        : this(
            totalSize,
            initialDownloadedSize,
            Stopwatch.GetTimestamp(),
            Stopwatch.Frequency,
            Math.Max(1, (long)(reportInterval.TotalSeconds * Stopwatch.Frequency)))
    {
    }

    internal DownloadProgressAccumulator(
        long totalSize,
        long initialDownloadedSize,
        long initialTimestamp,
        long timestampFrequency,
        long reportIntervalTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalSize);
        ArgumentOutOfRangeException.ThrowIfNegative(initialDownloadedSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timestampFrequency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reportIntervalTicks);

        this.totalSize = totalSize;
        this.timestampFrequency = timestampFrequency;
        this.reportIntervalTicks = reportIntervalTicks;
        downloadedSize = Math.Min(initialDownloadedSize, totalSize);
        lastReportTimestamp = initialTimestamp;
    }

    internal bool TryRecord(
        long transferredBytes,
        long downloadedBytesDelta,
        bool paused,
        out DownloadProgressSnapshot snapshot)
    {
        lock (sync)
        {
            return TryRecordCore(
                transferredBytes,
                downloadedBytesDelta,
                paused,
                Stopwatch.GetTimestamp(),
                out snapshot);
        }
    }

    /// <summary>Records a transfer at a supplied timestamp for deterministic tests.</summary>
    internal bool TryRecordAt(
        long transferredBytes,
        long downloadedBytesDelta,
        bool paused,
        long timestamp,
        out DownloadProgressSnapshot snapshot)
    {
        lock (sync)
        {
            return TryRecordCore(
                transferredBytes,
                downloadedBytesDelta,
                paused,
                timestamp,
                out snapshot);
        }
    }

    internal DownloadProgressSnapshot GetCurrentSnapshot()
    {
        lock (sync)
        {
            return new DownloadProgressSnapshot(downloadedSize, 0);
        }
    }

    internal void Pause()
    {
        lock (sync)
        {
            bytesSinceLastReport = 0;
        }
    }

    internal void Resume()
    {
        lock (sync)
        {
            bytesSinceLastReport = 0;
            lastReportTimestamp = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>Resumes sampling at a supplied timestamp for deterministic tests.</summary>
    internal void ResumeAt(long timestamp)
    {
        lock (sync)
        {
            bytesSinceLastReport = 0;
            lastReportTimestamp = timestamp;
        }
    }

    private bool TryRecordCore(
        long transferredBytes,
        long downloadedBytesDelta,
        bool paused,
        long timestamp,
        out DownloadProgressSnapshot snapshot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(transferredBytes);

        downloadedSize = Math.Clamp(downloadedSize + downloadedBytesDelta, 0, totalSize);
        bytesSinceLastReport += transferredBytes;

        var elapsedTicks = timestamp - lastReportTimestamp;
        if (downloadedSize < totalSize)
        {
            completionReported = false;
        }

        var completed = downloadedSize >= totalSize && !completionReported;
        var progressRolledBack = downloadedBytesDelta < 0;
        if (!completed && !progressRolledBack && elapsedTicks < reportIntervalTicks)
        {
            snapshot = default;
            return false;
        }

        var bytesPerSecond = paused || elapsedTicks <= 0
            ? 0
            : (long)(bytesSinceLastReport * (double)timestampFrequency / elapsedTicks);
        snapshot = new DownloadProgressSnapshot(downloadedSize, bytesPerSecond);

        bytesSinceLastReport = 0;
        lastReportTimestamp = timestamp;
        completionReported |= completed;
        return true;
    }
}

internal readonly record struct DownloadProgressSnapshot(
    long DownloadedSize,
    long BytesPerSecond);
