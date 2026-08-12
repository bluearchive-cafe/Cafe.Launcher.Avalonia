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

    internal DownloadProgressAccumulator(long totalSize, TimeSpan reportInterval)
        : this(
            totalSize,
            Stopwatch.GetTimestamp(),
            Stopwatch.Frequency,
            Math.Max(1, (long)(reportInterval.TotalSeconds * Stopwatch.Frequency)))
    {
    }

    internal DownloadProgressAccumulator(
        long totalSize,
        long initialTimestamp,
        long timestampFrequency,
        long reportIntervalTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timestampFrequency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reportIntervalTicks);

        this.totalSize = totalSize;
        this.timestampFrequency = timestampFrequency;
        this.reportIntervalTicks = reportIntervalTicks;
        lastReportTimestamp = initialTimestamp;
    }

    internal bool TryRecord(
        long bytes,
        bool paused,
        out DownloadProgressSnapshot snapshot)
    {
        lock (sync)
        {
            return TryRecordCore(bytes, paused, Stopwatch.GetTimestamp(), out snapshot);
        }
    }

    /// <summary>Records a transfer at a supplied timestamp for deterministic tests.</summary>
    internal bool TryRecordAt(
        long bytes,
        bool paused,
        long timestamp,
        out DownloadProgressSnapshot snapshot)
    {
        lock (sync)
        {
            return TryRecordCore(bytes, paused, timestamp, out snapshot);
        }
    }

    private bool TryRecordCore(
        long bytes,
        bool paused,
        long timestamp,
        out DownloadProgressSnapshot snapshot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        downloadedSize += bytes;
        bytesSinceLastReport += bytes;

        var elapsedTicks = timestamp - lastReportTimestamp;
        var completed = downloadedSize >= totalSize && !completionReported;
        if (!completed && elapsedTicks < reportIntervalTicks)
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
