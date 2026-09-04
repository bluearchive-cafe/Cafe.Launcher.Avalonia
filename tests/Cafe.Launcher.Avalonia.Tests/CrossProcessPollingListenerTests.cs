using Cafe.Launcher.Avalonia.Services;
using Xunit;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class CrossProcessPollingListenerTests
{
    [Fact]
    public void Listen_WhenSignalWaitReportsSignal_RaisesCallbackOncePerSignal()
    {
        var firstWaitEntered = new ManualResetEventSlim(false);
        var callbackInvoked = new ManualResetEventSlim(false);
        var callbackCount = 0;
        var isFirstWait = true;
        using var listener = new CrossProcessPollingListener(
            _ =>
            {
                if (isFirstWait)
                {
                    isFirstWait = false;
                    firstWaitEntered.Set();
                    return true;
                }

                Thread.Sleep(5);
                return false;
            },
            () =>
            {
                Interlocked.Increment(ref callbackCount);
                callbackInvoked.Set();
            });

        Assert.True(firstWaitEntered.Wait(TimeSpan.FromSeconds(5)), "listener never started polling");
        Assert.True(callbackInvoked.Wait(TimeSpan.FromSeconds(5)), "callback was never raised");

        // 一次信号只触发一次回调；随后的空轮询不得再触发。
        Thread.Sleep(60);
        Assert.Equal(1, Volatile.Read(ref callbackCount));
        Assert.False(listener.IsCancellationRequested);
    }

    [Fact]
    public void Listen_WhenCancelledDuringWait_DropsLateSignalWithoutCallback()
    {
        var enteredWait = new ManualResetEventSlim(false);
        var callbackCount = 0;
        CrossProcessPollingListener? listener = null;
        using var owned = new CrossProcessPollingListener(
            _ =>
            {
                if (!enteredWait.IsSet)
                {
                    enteredWait.Set();
                    // 阻塞"等待"直到外部 Dispose 取消，然后上报一个"迟到"的信号。
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (listener is null || (!listener.IsCancellationRequested && DateTime.UtcNow < deadline))
                    {
                        Thread.Sleep(5);
                    }

                    return true;
                }

                Thread.Sleep(5);
                return false;
            },
            () => Interlocked.Increment(ref callbackCount));
        listener = owned;

        Assert.True(enteredWait.Wait(TimeSpan.FromSeconds(5)), "listener never started polling");
        owned.Dispose();

        Assert.Equal(0, Volatile.Read(ref callbackCount));
    }

    [Fact]
    public void Dispose_StopsPollingLoopAndIsIdempotent()
    {
        var waitCallCount = 0;
        var listener = new CrossProcessPollingListener(
            _ =>
            {
                Interlocked.Increment(ref waitCallCount);
                Thread.Sleep(5);
                return false;
            },
            () => { });

        listener.Dispose();
        var countAfterDispose = Volatile.Read(ref waitCallCount);

        listener.Dispose();

        Thread.Sleep(80);
        Assert.True(Volatile.Read(ref waitCallCount) <= countAfterDispose + 1,
            "polling loop kept running after disposal");
        Assert.True(listener.IsCancellationRequested);
    }

    [Fact]
    public void Listen_WhenSignalWaitReturnsTrueTwice_RaisesOneCallbackPerSignal()
    {
        var secondCallbackInvoked = new ManualResetEventSlim(false);
        var callbackCount = 0;
        // 前两次“等待”报告有信号，之后恒为空轮询；每次 true 应恰好触发一次回调。
        var remainingTrues = 2;
        using var listener = new CrossProcessPollingListener(
            _ => remainingTrues-- > 0,
            () =>
            {
                if (Interlocked.Increment(ref callbackCount) == 2)
                {
                    secondCallbackInvoked.Set();
                }
            });

        Assert.True(secondCallbackInvoked.Wait(TimeSpan.FromSeconds(5)), "second callback was never raised");

        // 两次 true → 恰好两次回调；之后的空轮询不得追加。
        Thread.Sleep(60);
        Assert.Equal(2, Volatile.Read(ref callbackCount));
        Assert.False(listener.IsCancellationRequested);
    }

    [Fact]
    public void Listen_WhenSignalWaitThrows_StopsLoopSilentlyAndRemainsDisposable()
    {
        var waitCallCount = 0;
        var callbackCount = 0;
        var listener = new CrossProcessPollingListener(
            _ =>
            {
                if (Interlocked.Increment(ref waitCallCount) == 1)
                {
                    throw new InvalidOperationException("simulated wait failure");
                }

                return false;
            },
            () => Interlocked.Increment(ref callbackCount));

        // 等待委托抛异常 → 监听循环按设计静默退出：不触发回调，Dispose 仍然安全。
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref waitCallCount) >= 1, TimeSpan.FromSeconds(5)));
        Thread.Sleep(50);
        Assert.Equal(0, Volatile.Read(ref callbackCount));

        listener.Dispose();
        Assert.True(listener.IsCancellationRequested);
    }
}
