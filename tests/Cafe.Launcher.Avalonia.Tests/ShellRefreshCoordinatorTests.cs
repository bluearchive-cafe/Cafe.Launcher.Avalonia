using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Features.Shell;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 验证 ShellRefreshCoordinator 的并发与生命周期契约：
/// 刷新在信号量下串行化、关闭时先排空进行中的刷新再放行、
/// 刷新委托抛异常后协调器状态自愈、生命周期取消让在途刷新收尾。
/// 所有等待均通过 TCS 门控或有界 WaitAsync，不依赖真实时间。
/// </summary>
public sealed class ShellRefreshCoordinatorTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RefreshAsync_ConcurrentRefreshes_RunSerializedWithoutOverlap()
    {
        // 每次刷新都在加载回调内被独立的 TCS 门控。若协调器未串行化,
        // 第二个刷新会在第一个仍持有信号量时进入加载回调,事件顺序将交错。
        var events = new List<string>();
        var eventsLock = new object();
        int callIndex = 0;
        var firstEntered = CreateSignal();
        var secondEntered = CreateSignal();
        var firstGate = CreateSignal();
        var secondGate = CreateSignal();

        void Record(string entry)
        {
            lock (eventsLock)
            {
                events.Add(entry);
            }
        }

        async Task<bool> LoadHostStateAsync(CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref callIndex);
            Record($"enter-{index}");
            (index == 1 ? firstEntered : secondEntered).TrySetResult();
            await (index == 1 ? firstGate : secondGate).Task;
            Record($"exit-{index}");
            return true;
        }

        using var coordinator = new ShellRefreshCoordinator(LoadHostStateAsync, CompletedAfterLoad);

        var firstRefresh = coordinator.RefreshAsync(resumePersistedDownload: false);
        await firstEntered.Task.WaitAsync(WaitTimeout);

        var secondRefresh = coordinator.RefreshAsync(resumePersistedDownload: false);
        // 第一个刷新仍持有信号量且尚未退出,第二个刷新此刻必然没进入加载回调。
        Assert.False(secondRefresh.IsCompleted);
        Assert.DoesNotContain("enter-2", events);

        firstGate.TrySetResult();
        await firstRefresh.WaitAsync(WaitTimeout);

        await secondEntered.Task.WaitAsync(WaitTimeout);
        secondGate.TrySetResult();
        await secondRefresh.WaitAsync(WaitTimeout);

        Assert.Equal(new[] { "enter-1", "exit-1", "enter-2", "exit-2" }, events);
    }

    [Fact]
    public async Task BeginShutdown_WithoutActiveRefresh_ReturnsCompletedTaskAndIgnoresLaterRefreshes()
    {
        int loadCalls = 0;
        using var coordinator = new ShellRefreshCoordinator(
            _ =>
            {
                loadCalls++;
                return Task.FromResult(true);
            },
            CompletedAfterLoad);

        // 初始 after-load 任务是已完成任务,排空握手无需等待。
        Assert.Same(Task.CompletedTask, coordinator.PendingAfterLoadWork);

        var drain = coordinator.BeginShutdown();
        Assert.True(drain.IsCompleted);

        // 关闭后的刷新按契约被静默忽略,不再触碰任何回调。
        await coordinator.RefreshAsync(resumePersistedDownload: false);

        Assert.Equal(0, loadCalls);
    }

    [Fact]
    public async Task BeginShutdown_WhileRefreshInFlight_CompletesOnlyAfterRefreshDrains()
    {
        int loadCalls = 0;
        var entered = CreateSignal();
        var gate = CreateSignal();
        using var coordinator = new ShellRefreshCoordinator(
            async _ =>
            {
                loadCalls++;
                entered.TrySetResult();
                await gate.Task;
                return true;
            },
            CompletedAfterLoad);

        var refresh = coordinator.RefreshAsync(resumePersistedDownload: false);
        await entered.Task.WaitAsync(WaitTimeout);

        var drain = coordinator.BeginShutdown();
        Assert.False(drain.IsCompleted);

        gate.TrySetResult();
        await refresh.WaitAsync(WaitTimeout);
        await drain.WaitAsync(WaitTimeout);

        // 排空完成后,新刷新按关闭契约被忽略。
        await coordinator.RefreshAsync(resumePersistedDownload: false);
        Assert.Equal(1, loadCalls);
    }

    [Fact]
    public async Task BeginShutdown_WithMultipleActiveRefreshes_CompletesOnlyAfterLastOneDrains()
    {
        // activeRefreshCount 在 RefreshAsync 进入时递增、finally 中递减;
        // 通过排空任务只有在最后一个活跃刷新完成后才完成来验证计数语义。
        int callIndex = 0;
        var firstEntered = CreateSignal();
        var secondEntered = CreateSignal();
        var firstGate = CreateSignal();
        var secondGate = CreateSignal();
        using var coordinator = new ShellRefreshCoordinator(
            async _ =>
            {
                var index = Interlocked.Increment(ref callIndex);
                (index == 1 ? firstEntered : secondEntered).TrySetResult();
                await (index == 1 ? firstGate : secondGate).Task;
                return true;
            },
            CompletedAfterLoad);

        var firstRefresh = coordinator.RefreshAsync(resumePersistedDownload: false);
        var secondRefresh = coordinator.RefreshAsync(resumePersistedDownload: false);
        await firstEntered.Task.WaitAsync(WaitTimeout);

        var drain = coordinator.BeginShutdown();
        Assert.False(drain.IsCompleted);

        // 第一个刷新完成后第二个仍在执行,活跃计数未归零,排空任务必须保持未完成。
        firstGate.TrySetResult();
        await firstRefresh.WaitAsync(WaitTimeout);
        Assert.False(drain.IsCompleted);

        await secondEntered.Task.WaitAsync(WaitTimeout);
        secondGate.TrySetResult();
        await secondRefresh.WaitAsync(WaitTimeout);
        await drain.WaitAsync(WaitTimeout);
    }

    [Fact]
    public async Task RefreshAsync_WhenLoadThrows_PropagatesAndKeepsCoordinatorUsable()
    {
        int loadCalls = 0;
        using var coordinator = new ShellRefreshCoordinator(
            _ =>
            {
                loadCalls++;
                if (loadCalls == 1)
                {
                    throw new InvalidOperationException("host state load failed");
                }

                return Task.FromResult(true);
            },
            CompletedAfterLoad);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.RefreshAsync(resumePersistedDownload: false));

        // 先验证协调器仍可用:第二次刷新照常执行(信号量已释放)。
        await coordinator.RefreshAsync(resumePersistedDownload: false);
        Assert.Equal(2, loadCalls);

        // 异常路径必须已递减活跃计数,否则排空握手会永远悬空。
        Assert.True(coordinator.BeginShutdown().IsCompleted);
    }

    [Fact]
    public async Task RefreshAsync_WhenLifetimeCancels_UnwindsInFlightRefreshAndCompletesDrain()
    {
        var entered = CreateSignal();
        using var coordinator = new ShellRefreshCoordinator(
            async refreshToken =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, refreshToken);
                return true;
            },
            CompletedAfterLoad);

        var refresh = coordinator.RefreshAsync(resumePersistedDownload: false);
        await entered.Task.WaitAsync(WaitTimeout);

        var drain = coordinator.BeginShutdown();
        coordinator.CancelLifetime();

        // 生命周期取消被 RefreshAsync 吞掉:任务正常完成而非抛出,
        // 活跃计数归零后驱动排空握手完成。
        await refresh.WaitAsync(WaitTimeout);
        await drain.WaitAsync(WaitTimeout);
    }

    [Fact]
    public async Task RefreshAsync_WhenLoaded_StoresAfterLoadWorkForShutdownHandshake()
    {
        var pendingSource = CreateSignal();
        bool? receivedResumeFlag = null;
        using var coordinator = new ShellRefreshCoordinator(
            _ => Task.FromResult(true),
            (resumePersistedDownload, _) =>
            {
                receivedResumeFlag = resumePersistedDownload;
                return Task.FromResult(pendingSource.Task);
            });

        // 刷新本身不等待 after-load 任务完成,启动更新检查因此能在后台运行。
        await coordinator.RefreshAsync(resumePersistedDownload: true);

        Assert.True(receivedResumeFlag);
        Assert.Same(pendingSource.Task, coordinator.PendingAfterLoadWork);

        var drain = coordinator.BeginShutdown();
        pendingSource.TrySetResult();
        await coordinator.WaitForShutdownWorkAsync(drain);
    }

    [Fact]
    public async Task RefreshAsync_AfterDispose_CompletesWithoutRunningWork()
    {
        int loadCalls = 0;
        var coordinator = new ShellRefreshCoordinator(
            _ =>
            {
                loadCalls++;
                return Task.FromResult(true);
            },
            CompletedAfterLoad);
        coordinator.Dispose();

        await coordinator.RefreshAsync(resumePersistedDownload: false);

        Assert.Equal(0, loadCalls);
    }

    /// <summary>构造带 RunContinuationsAsynchronously 的信号量,避免等待续体吞掉完成信号。</summary>
    private static TaskCompletionSource CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>默认 after-load 回调:立即返回一个已完成的后台任务。</summary>
    private static Func<bool, CancellationToken, Task<Task>> CompletedAfterLoad { get; } =
        (_, _) => Task.FromResult(Task.CompletedTask);
}
