using System;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>Selectable time window for a diagnostics export.</summary>
public enum LogExportRangePreset
{
    All,
    LastHour,
    Last24Hours,
    Last7Days,
    Last30Days
}

/// <summary>
/// Describes what a diagnostics export contains. The resolved window is half-open:
/// <see cref="ResolveWindow"/>'s <c>From</c> is inclusive and <c>To</c> is exclusive,
/// with a <see langword="null"/> bound meaning "unbounded on that side".
/// </summary>
public sealed record LogExportOptions
{
    /// <summary>Exports every log file in full and nothing else.</summary>
    public static LogExportOptions Default { get; } = new();

    /// <summary>Time range to crop the exported logs and crash reports to.</summary>
    public LogExportRangePreset Range { get; init; } = LogExportRangePreset.All;

    /// <summary>Whether the archive also carries the crash snapshots inside the range.</summary>
    public bool IncludeCrashReports { get; init; }

    /// <summary>Whether the archive also carries the launcher state files (local paths, player UID).</summary>
    public bool IncludeUserData { get; init; }

    /// <summary>Resolves the effective window relative to <paramref name="now"/>.</summary>
    public ExportWindow ResolveWindow(DateTimeOffset now) => Range switch
    {
        LogExportRangePreset.LastHour => new ExportWindow(now.AddHours(-1), now),
        LogExportRangePreset.Last24Hours => new ExportWindow(now.AddDays(-1), now),
        LogExportRangePreset.Last7Days => new ExportWindow(now.AddDays(-7), now),
        LogExportRangePreset.Last30Days => new ExportWindow(now.AddDays(-30), now),
        _ => ExportWindow.Unbounded
    };
}
