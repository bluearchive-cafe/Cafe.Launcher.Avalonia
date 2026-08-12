using System;
using System.Diagnostics;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>Limits aggregate transfer bytes using active (non-paused) elapsed time.</summary>
internal sealed class DownloadTransferThrottle
{
    private readonly object sync = new();
    private readonly int bytesPerSecond;
    private readonly long timestampFrequency;
    private readonly long startedAtTimestamp;
    private long totalBytes;
    private long pausedAtTimestamp = -1;
    private long pausedTicks;

    internal DownloadTransferThrottle(int bytesPerSecond)
        : this(bytesPerSecond, Stopwatch.GetTimestamp(), Stopwatch.Frequency)
    {
    }

    internal DownloadTransferThrottle(
        int bytesPerSecond,
        long initialTimestamp,
        long timestampFrequency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timestampFrequency);

        this.bytesPerSecond = bytesPerSecond;
        this.timestampFrequency = timestampFrequency;
        startedAtTimestamp = initialTimestamp;
    }

    internal TimeSpan RecordBytes(long bytes)
    {
        lock (sync)
        {
            return RecordBytesCore(bytes, Stopwatch.GetTimestamp());
        }
    }

    internal TimeSpan RecordBytesAt(long bytes, long timestamp)
    {
        lock (sync)
        {
            return RecordBytesCore(bytes, timestamp);
        }
    }

    internal void Pause()
    {
        lock (sync)
        {
            PauseCore(Stopwatch.GetTimestamp());
        }
    }

    internal void Resume()
    {
        lock (sync)
        {
            ResumeCore(Stopwatch.GetTimestamp());
        }
    }

    internal void PauseAt(long timestamp)
    {
        lock (sync)
        {
            PauseCore(timestamp);
        }
    }

    internal void ResumeAt(long timestamp)
    {
        lock (sync)
        {
            ResumeCore(timestamp);
        }
    }

    private TimeSpan RecordBytesCore(long bytes, long timestamp)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        totalBytes += bytes;

        var currentPausedTicks = pausedAtTimestamp >= 0
            ? Math.Max(0, timestamp - pausedAtTimestamp)
            : 0;
        var activeTicks = Math.Max(
            0,
            timestamp - startedAtTimestamp - pausedTicks - currentPausedTicks);
        var activeSeconds = activeTicks / (double)timestampFrequency;
        var targetSeconds = totalBytes / (double)bytesPerSecond;
        return TimeSpan.FromSeconds(Math.Max(0, targetSeconds - activeSeconds));
    }

    private void PauseCore(long timestamp)
    {
        if (pausedAtTimestamp < 0)
        {
            pausedAtTimestamp = timestamp;
        }
    }

    private void ResumeCore(long timestamp)
    {
        if (pausedAtTimestamp < 0)
        {
            return;
        }

        pausedTicks += Math.Max(0, timestamp - pausedAtTimestamp);
        pausedAtTimestamp = -1;
    }
}
