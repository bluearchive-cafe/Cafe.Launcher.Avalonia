using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LatestRefreshTests
{
    [Fact]
    public async Task Run_WhenSupersededWhileInFlight_TheCancelledRunNeverAppliesItsResult()
    {
        var refresh = new LatestRefresh();
        var releaseSuperseded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = new List<string>();
        CancellationToken supersededToken = CancellationToken.None;

        refresh.Run(null, async token =>
        {
            supersededToken = token;
            try
            {
                await releaseSuperseded.Task.WaitAsync(token);
                applied.Add("superseded");
            }
            catch (OperationCanceledException)
            {
            }
        });
        refresh.Run(null, _ =>
        {
            applied.Add("latest");
            return Task.CompletedTask;
        });

        // 新的一次进入槽位即取消在飞的旧令牌；旧工作醒来后不得应用其结果。
        Assert.True(supersededToken.IsCancellationRequested);
        releaseSuperseded.TrySetResult();
        await refresh.Pending;
        await TestWait.HoldsForAsync(
            TimeSpan.FromMilliseconds(100),
            () => applied.SequenceEqual(["latest"]),
            "The superseded run applied its result after being cancelled");
    }

    [Fact]
    public async Task Run_WhenSupersededDuringTheDebounceWindow_TheWaitingWorkNeverStarts()
    {
        var refresh = new LatestRefresh();
        var applied = new List<string>();

        refresh.Run(TimeSpan.FromMilliseconds(30), _ =>
        {
            applied.Add("superseded");
            return Task.CompletedTask;
        });
        refresh.Run(TimeSpan.FromMilliseconds(30), _ =>
        {
            applied.Add("latest");
            return Task.CompletedTask;
        });

        await refresh.Pending;
        await TestWait.HoldsForAsync(
            TimeSpan.FromMilliseconds(150),
            () => applied.SequenceEqual(["latest"]),
            "A debounced run that was superseded during the window still started");
    }
}
