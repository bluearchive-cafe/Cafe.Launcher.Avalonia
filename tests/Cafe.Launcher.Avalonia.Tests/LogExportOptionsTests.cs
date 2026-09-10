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
    public void ResolveWindow_ForPreset_UsesRequestedDurationAndCurrentTimeUpperBound(
        LogExportRangePreset preset,
        int expectedHours)
    {
        var window = new LogExportOptions { Range = preset }.ResolveWindow(Now);

        Assert.Equal(Now.AddHours(-expectedHours), window.From);
        Assert.Equal(Now, window.To);
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

}
