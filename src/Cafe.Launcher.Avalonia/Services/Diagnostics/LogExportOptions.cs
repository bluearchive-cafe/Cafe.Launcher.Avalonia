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

    /// <summary>Time range to crop the exported logs and crash reports to.</summary>
    public LogExportRangePreset Range { get; init; } = LogExportRangePreset.All;

    /// <summary>First included day when <see cref="Range"/> is <see cref="LogExportRangePreset.Custom"/>.</summary>
    public DateTimeOffset? CustomFrom { get; init; }

    /// <summary>Last included day when <see cref="Range"/> is <see cref="LogExportRangePreset.Custom"/>; the whole day is kept.</summary>
    public DateTimeOffset? CustomTo { get; init; }

    /// <summary>Whether the archive also carries the crash snapshots inside the range.</summary>
    public bool IncludeCrashReports { get; init; }

    /// <summary>Whether the archive also carries the launcher state files (local paths, player UID).</summary>
    public bool IncludeUserData { get; init; }

    /// <summary>Gets whether a custom range has bounds in a usable order.</summary>
    public bool IsCustomRangeValid => IsRangeValid(Range, CustomFrom, CustomTo);

    /// <summary>
    /// Gets whether a range has bounds in a usable order. A non-custom range is always usable,
    /// and a custom range may leave either side open; only a fully specified pair is checked.
    /// </summary>
    public static bool IsRangeValid(
        LogExportRangePreset range,
        DateTimeOffset? customFrom,
        DateTimeOffset? customTo) =>
        range != LogExportRangePreset.Custom
        || customFrom is null
        || customTo is null
        || customFrom.Value.Date <= customTo.Value.Date;

    /// <summary>Resolves the effective window relative to <paramref name="now"/>.</summary>
    public ExportWindow ResolveWindow(DateTimeOffset now) => Range switch
    {
        LogExportRangePreset.LastHour => new ExportWindow(now.AddHours(-1), null),
        LogExportRangePreset.Last24Hours => new ExportWindow(now.AddDays(-1), null),
        LogExportRangePreset.Last7Days => new ExportWindow(now.AddDays(-7), null),
        LogExportRangePreset.Last30Days => new ExportWindow(now.AddDays(-30), null),
        LogExportRangePreset.Custom => new ExportWindow(CustomFrom?.Date, CustomTo?.Date.AddDays(1)),
        _ => ExportWindow.Unbounded
    };
}
