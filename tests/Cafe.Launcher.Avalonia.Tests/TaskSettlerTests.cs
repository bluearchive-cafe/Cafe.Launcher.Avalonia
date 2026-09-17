using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class TaskSettlerTests
{
    [Fact]
    public async Task WaitAsync_WhenTheCurrentTaskCompletes_ReturnsWithoutWaitingForTheBudget()
    {
        var completed = Task.CompletedTask;

        await TaskSettler.WaitAsync(() => completed, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitAsync_WhenNothingIsInFlight_ReturnsImmediately()
    {
        await TaskSettler.WaitAsync(() => null, TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task WaitAsync_WhenTheBudgetElapses_ReturnsAndLetsTheCallerProceed()
    {
        var neverCompleting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await TaskSettler.WaitAsync(() => neverCompleting.Task, TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task WaitAsync_WhenTheTaskIsReplacedMidWait_WaitsForTheNewestOne()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? provided = first.Task;
        var calls = 0;

        var waiting = TaskSettler.WaitAsync(
            () =>
            {
                calls++;
                return provided;
            },
            TimeSpan.FromSeconds(5));
        // 确认快照已取走（pending 已捕获 first），再换槽并放行旧任务：
        // 旧任务完成时 settler 必须发现引用已变化，转而去等新的那次。
        await TestWait.UntilAsync(() => calls >= 1, TimeSpan.FromSeconds(5), "settler did not snapshot");
        provided = Task.CompletedTask;
        first.TrySetResult();
        await waiting;

        Assert.True(calls >= 2, "A replaced task did not trigger the re-check loop.");
    }
}
