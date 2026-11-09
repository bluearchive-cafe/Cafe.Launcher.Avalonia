using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Models;

namespace Cafe.Launcher.Tests;

public sealed class FileActivityReporterTests
{
    [Fact]
    public void ConcurrentCompletions_PreserveFinalCountsAndImmutableEarlierSnapshots()
    {
        var snapshots = new List<GameOperationProgress>();
        var reporter = new FileActivityReporter(GameOperationKind.Repair, GameOperationStage.FileCheck, 100, snapshots.Add);
        reporter.Update("large.bundle", 10, 100, true);
        var earlier = snapshots[0];
        Parallel.For(0, 99, i => reporter.Complete(i + ".bundle"));
        reporter.Update("large.bundle", 50, 100, true, emit: false);
        reporter.Complete("large.bundle");
        var final = snapshots[^1];
        Assert.Equal(100, final.ProcessedFileCount);
        Assert.Equal(100, final.TotalFileCount);
        Assert.Equal(100, final.Progress);
        Assert.Empty(final.ActiveFiles);
        Assert.Equal(10, Assert.Single(earlier.ActiveFiles).ProcessedBytes);
        Assert.True(final.ActivitySequence > earlier.ActivitySequence);
    }
}
