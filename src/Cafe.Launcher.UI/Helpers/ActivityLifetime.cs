using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.UI.Helpers;

/// <summary>
/// 活动生命周期（ADR-051）：封闭新工作、给在飞工作发租约、停机时等待计数归零，并持有
/// 生命周期取消令牌。刷新协调器与游戏操作旅程各持有一个实例——共享的是这套状态机，不是
/// 状态本身；两者的停机互不牵连。刷新序列化门、after-load 任务与下载停止原因的翻译都留在
/// 各自域内。
/// </summary>
internal sealed class ActivityLifetime : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly CancellationToken token;

    private TaskCompletionSource? drained;
    private int activeCount;
    private bool shuttingDown;
    private bool disposed;
    private bool cancellationInProgress;
    private bool cancellationDisposed;

    public ActivityLifetime()
    {
        // 源释放后令牌仍可读取与传递；缓存一份，Dispose 之后访问属性不会抛。
        token = cancellation.Token;
    }

    /// <summary>Gets whether new work is closed; true after <see cref="BeginShutdown"/> or <see cref="Dispose"/>.</summary>
    public bool IsShuttingDown
    {
        get
        {
            lock (gate)
            {
                return shuttingDown;
            }
        }
    }

    /// <summary>Gets the token cancelled by <see cref="Cancel"/> and <see cref="Dispose"/>.</summary>
    public CancellationToken LifetimeToken => token;

    /// <summary>
    /// Starts one unit of work and returns the lease that ends it; null when shutdown or
    /// disposal already closed the door. Releasing a lease twice counts once.
    /// </summary>
    public IDisposable? TryBegin()
    {
        lock (gate)
        {
            if (shuttingDown)
            {
                return null;
            }

            activeCount++;
            return new Lease(this);
        }
    }

    /// <summary>
    /// Closes the door and returns the task that completes when every outstanding lease has
    /// been released. Repeated calls return the same task; with nothing in flight it is already
    /// complete.
    /// </summary>
    public Task BeginShutdown()
    {
        lock (gate)
        {
            shuttingDown = true;
            if (activeCount == 0)
            {
                return drained?.Task ?? Task.CompletedTask;
            }

            drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return drained.Task;
        }
    }

    /// <summary>
    /// Cancels the lifetime token without closing new work: the refresh coordinator begins
    /// shutdown first and only then unwinds in-flight refreshes. Idempotent, and a no-op after
    /// disposal.
    /// </summary>
    public void Cancel()
    {
        lock (gate)
        {
            if (cancellationDisposed || cancellationInProgress)
            {
                return;
            }

            cancellationInProgress = true;
        }

        try
        {
            cancellation.Cancel();
        }
        finally
        {
            lock (gate)
            {
                cancellationInProgress = false;
            }

            TryDisposeCancellation();
        }
    }

    /// <summary>
    /// Closes new work, cancels the token and releases the token source once nothing is in
    /// flight. Safe at any time; repeated calls are no-ops.
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            shuttingDown = true;
        }

        Cancel();
        // Cancel 可能因另一线程正在取消而提前返回，释放检查要再走一次。
        TryDisposeCancellation();
    }

    private void Release()
    {
        TaskCompletionSource? completed;
        lock (gate)
        {
            activeCount--;
            completed = activeCount == 0 ? drained : null;
        }

        completed?.TrySetResult();
        TryDisposeCancellation();
    }

    /// <summary>
    /// 源只在「已释放、没有在飞租约、也没有正在进行的取消」时才真正 Dispose：取消是停机的
    /// 第一步，而租约由在飞工作自己收尾，两者可能并发，且
    /// <see cref="CancellationTokenSource.Dispose"/> 不能与其他成员并发调用。
    /// </summary>
    private void TryDisposeCancellation()
    {
        lock (gate)
        {
            if (!disposed || activeCount > 0 || cancellationInProgress || cancellationDisposed)
            {
                return;
            }

            cancellationDisposed = true;
        }

        cancellation.Dispose();
    }

    private sealed class Lease(ActivityLifetime owner) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                owner.Release();
            }
        }
    }
}
