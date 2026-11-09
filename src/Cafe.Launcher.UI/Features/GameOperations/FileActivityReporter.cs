using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Cafe.Launcher.UI.Models;

namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>Serializes bounded-rate, immutable file activity snapshots across concurrent workers.</summary>
internal sealed class FileActivityReporter(GameOperationKind kind, GameOperationStage stage,
    int totalFiles, Action<GameOperationProgress> sink)
{
    private readonly object sync = new();
    private readonly Dictionary<string, GameOperationFileProgress> active = new(StringComparer.Ordinal);
    private long sequence;
    private long lastReport;
    private int completed;

    /// <summary>Records measured bytes; publishing is independently rate-limited.</summary>
    public void Update(string path, long bytes, long total, bool verifying, bool emit = true)
    {
        lock (sync)
        {
            var next = new GameOperationFileProgress(path, Math.Clamp(bytes, 0, Math.Max(0, total)), total, verifying);
            if (!active.TryGetValue(path, out var previous) || previous != next) sequence++;
            active[path] = next;
            if (emit) Publish();
        }
    }

    /// <summary>Observes actual reads, retaining sub-percent progress in large files.</summary>
    public Action<long, long> ReadProgress(string path) => (bytes, total) => Update(path, bytes, total, verifying: true);

    /// <summary>Finishes one processed file, including a missing or rejected file.</summary>
    public void Complete(string path)
    {
        lock (sync)
        {
            active.Remove(path);
            completed++;
            sequence++;
            Publish(force: completed == totalFiles);
        }
    }

    /// <summary>Copies an immutable activity snapshot into an operation's metric report.</summary>
    public void Enrich(GameOperationProgress progress)
    {
        lock (sync)
        {
            progress.ProcessedFileCount = completed;
            progress.TotalFileCount = totalFiles;
            progress.ActiveFiles = active.Values.ToArray();
            progress.ActivitySequence = sequence;
        }
    }

    private void Publish(bool force = false)
    {
        var now = Stopwatch.GetTimestamp();
        if (!force && lastReport != 0 && Stopwatch.GetElapsedTime(lastReport, now) < TimeSpan.FromMilliseconds(100)) return;
        lastReport = now;
        var progress = GameOperationProgressFactory.CreateProgress(kind, stage, StageProgressReporter.Percent(completed, totalFiles));
        Enrich(progress);
        sink(progress);
    }
}
