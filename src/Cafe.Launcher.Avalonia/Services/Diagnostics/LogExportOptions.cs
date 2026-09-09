using System;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>Selectable time window for a diagnostics export.</summary>
public enum LogExportRangePreset
{
    All,
    LastHour,
    Last24Hours,
    Last7Days,
    Last30Days,
    Custom
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

    public LogExportRangePreset Range { get; init; } = LogExportRangePreset.All;

    /// <summary>First included day when <see cref="Range"/> is <see cref="LogExportRangePreset.Custom"/>.</summary>
    public DateTimeOffset? CustomFrom { get; init; }

    /// <summary>Last included day when <see cref="Range"/> is <see cref="LogExportRangePreset.Custom"/>; the whole day is kept.</summary>
    public DateTimeOffset? CustomTo { get; init; }

    public bool IncludeCrashReports { get; init; }

    public bool IncludeUserData { get; init; }

    /// <summary>Gets whether a custom range has bounds in a usable order.</summary>
    public bool IsCustomRangeValid =>
        Range != LogExportRangePreset.Custom
        || CustomFrom is null
        || CustomTo is null
        || CustomFrom.Value.Date <= CustomTo.Value.Date;

    /// <summary>Resolves the effective window relative to <paramref name="now"/>.</summary>
    public (DateTimeOffset? From, DateTimeOffset? To) ResolveWindow(DateTimeOffset now) => Range switch
    {
        LogExportRangePreset.LastHour => (now.AddHours(-1), null),
        LogExportRangePreset.Last24Hours => (now.AddDays(-1), null),
        LogExportRangePreset.Last7Days => (now.AddDays(-7), null),
        LogExportRangePreset.Last30Days => (now.AddDays(-30), null),
        LogExportRangePreset.Custom => (CustomFrom?.Date, CustomTo?.Date.AddDays(1)),
        _ => (null, null)
    };

    /// <summary>Gets whether <paramref name="timestamp"/> falls inside the window.</summary>
    public static bool Contains(
        DateTimeOffset? timestamp,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        // An entry without a parsable timestamp cannot be placed in time; the
        // export keeps it rather than silently dropping diagnostics.
        if (timestamp is null)
        {
            return true;
        }

        return (from is null || timestamp.Value >= from.Value)
            && (to is null || timestamp.Value < to.Value);
    }
}
