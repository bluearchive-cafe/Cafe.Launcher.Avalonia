using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Covers the range rules the export dialog and the export service share: the window each
/// preset resolves to and the half-open boundary the filter relies on.
/// </summary>
public sealed class LogExportOptionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 15, 30, 0, TimeSpan.FromHours(8));

    [Fact]
    public void ResolveWindow_ForAll_IsUnbounded()
    {
        var window = new LogExportOptions().ResolveWindow(Now);

        Assert.True(window.IsUnbounded);
        Assert.Equal(ExportWindow.Unbounded, window);
    }

    [Theory]
    [InlineData(LogExportRangePreset.LastHour, 1)]
    [InlineData(LogExportRangePreset.Last24Hours, 24)]
    [InlineData(LogExportRangePreset.Last7Days, 24 * 7)]
    [InlineData(LogExportRangePreset.Last30Days, 24 * 30)]
    public void ResolveWindow_ForPreset_StartsThatFarBackAndLeavesTheUpperBoundOpen(
        LogExportRangePreset preset,
        int expectedHours)
    {
        var window = new LogExportOptions { Range = preset }.ResolveWindow(Now);

        Assert.Equal(Now.AddHours(-expectedHours), window.From);
        Assert.Null(window.To);
    }

    [Fact]
    public void ResolveWindow_ForCustomRange_CoversWholeDaysOfThePickedDates()
    {
        var options = new LogExportOptions
        {
            Range = LogExportRangePreset.Custom,
            CustomFrom = new DateTimeOffset(2026, 9, 8, 13, 45, 0, TimeSpan.FromHours(8)),
            CustomTo = new DateTimeOffset(2026, 9, 9, 7, 0, 0, TimeSpan.FromHours(8))
        };

        var window = options.ResolveWindow(Now);

        // Picking a day means the whole day: start at the first day's midnight, end exclusive
        // at the midnight after the last one, regardless of the time the picker reported.
        Assert.Equal(LocalMidnight(2026, 9, 8), window.From);
        Assert.Equal(LocalMidnight(2026, 9, 10), window.To);
    }

    [Fact]
    public void ResolveWindow_ForCustomRangeWithOneBound_LeavesTheOtherSideUnbounded()
    {
        var fromOnly = new LogExportOptions
        {
            Range = LogExportRangePreset.Custom,
            CustomFrom = new DateTimeOffset(2026, 9, 8, 13, 45, 0, TimeSpan.FromHours(8))
        }.ResolveWindow(Now);
        var toOnly = new LogExportOptions
        {
            Range = LogExportRangePreset.Custom,
            CustomTo = new DateTimeOffset(2026, 9, 9, 7, 0, 0, TimeSpan.FromHours(8))
        }.ResolveWindow(Now);

        Assert.Equal(LocalMidnight(2026, 9, 8), fromOnly.From);
        Assert.Null(fromOnly.To);
        Assert.Null(toOnly.From);
        Assert.Equal(LocalMidnight(2026, 9, 10), toOnly.To);
    }

    [Fact]
    public void Contains_WithBothBounds_IsInclusiveOnFromAndExclusiveOnTo()
    {
        var window = new ExportWindow(Now, Now.AddHours(1));

        Assert.True(window.Contains(Now));
        Assert.True(window.Contains(Now.AddMinutes(59)));
        Assert.False(window.Contains(Now.AddHours(1)));
        Assert.False(window.Contains(Now.AddTicks(-1)));
    }

    [Fact]
    public void Contains_WithoutAParsableTimestamp_KeepsTheEntry()
    {
        var window = new ExportWindow(Now, Now.AddHours(1));

        Assert.True(window.Contains(null));
    }

    [Fact]
    public void ContainsFileWrittenAt_ForWriteTimesAroundTheWindow_AppliesTheHalfOpenBoundary()
    {
        var window = new ExportWindow(
            new DateTimeOffset(2026, 9, 9, 16, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 10, 16, 0, 0, TimeSpan.Zero));

        Assert.True(window.ContainsFileWrittenAt(new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc)));
        Assert.False(window.ContainsFileWrittenAt(new DateTime(2026, 9, 9, 15, 59, 0, DateTimeKind.Utc)));
        Assert.False(window.ContainsFileWrittenAt(new DateTime(2026, 9, 10, 16, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void IsRangeValid_WithBothBoundsSet_RequiresStartNotLaterThanEnd()
    {
        var earlier = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var later = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

        Assert.True(LogExportOptions.IsRangeValid(LogExportRangePreset.Custom, earlier, later));
        Assert.False(LogExportOptions.IsRangeValid(LogExportRangePreset.Custom, later, earlier));
    }

    [Fact]
    public void IsRangeValid_WithOneBoundMissing_AcceptsTheRange()
    {
        var bound = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        // Either side may stay open, and a custom range with no bounds at all means "everything".
        Assert.True(LogExportOptions.IsRangeValid(LogExportRangePreset.Custom, bound, null));
        Assert.True(LogExportOptions.IsRangeValid(LogExportRangePreset.Custom, null, bound));
        Assert.True(LogExportOptions.IsRangeValid(LogExportRangePreset.Custom, null, null));
    }

    [Fact]
    public void IsRangeValid_OutsideTheCustomRange_IgnoresReversedBounds()
    {
        var earlier = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var later = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

        // A leftover custom pair must not invalidate a preset range.
        Assert.True(LogExportOptions.IsRangeValid(LogExportRangePreset.Last7Days, later, earlier));
        Assert.True(new LogExportOptions
        {
            Range = LogExportRangePreset.Last7Days,
            CustomFrom = later,
            CustomTo = earlier
        }.IsCustomRangeValid);
    }

    [Fact]
    public void IsRangeValid_WithSameDayBoundsInAnyOrder_AcceptsTheRange()
    {
        // Both pickers hand over a day, so a single-day range is the common case: the two
        // timestamps can sit in either order while the days are still in order.
        Assert.True(LogExportOptions.IsRangeValid(
            LogExportRangePreset.Custom,
            new DateTimeOffset(2026, 9, 9, 23, 0, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 9, 9, 1, 0, 0, TimeSpan.FromHours(8))));
    }

    /// <summary>
    /// Midnight of the given day as the export builds it. A custom bound is a date, and a
    /// <see cref="DateTime"/> with an unspecified kind converts through the machine's zone, so
    /// the expectation follows that same conversion instead of pinning an offset that would only
    /// hold in one time zone.
    /// </summary>
    private static DateTimeOffset LocalMidnight(int year, int month, int day) =>
        new(new DateTime(year, month, day));
}
