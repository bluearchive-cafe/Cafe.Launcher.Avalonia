using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Services.Diagnostics;

namespace Cafe.Launcher.Services;

/// <summary>
/// Polls a cross-process signal endpoint (named event or local socket) on a
/// background task and raises an injected callback when a signal is consumed.
/// The polling contract — 250 ms interval, auto-reset, best-effort cleanup —
/// lives in this module instead of inside the window bootstrap.
/// </summary>
internal sealed class CrossProcessPollingListener : IDisposable
{
    /// <summary>轮询间隔；关闭收尾的等待窗口由测试据此推导。</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly Task listenerTask;
    private readonly Func<TimeSpan, bool> waitForSignal;
    private readonly Action onSignalRaised;
    private volatile bool stopRequested;
    private bool disposed;

    /// <summary>Initializes the listener and starts the background polling loop.</summary>
    public CrossProcessPollingListener(Func<TimeSpan, bool> waitForSignal, Action onSignalRaised)
    {
        this.waitForSignal = waitForSignal;
        this.onSignalRaised = onSignalRaised;
        listenerTask = Task.Run(Listen, cancellationTokenSource.Token);
    }

    /// <summary>Gets whether the listener is being stopped; dispatch should drop late signals.</summary>
    public bool IsCancellationRequested => stopRequested;

    /// <summary>
    /// The polling loop itself. <see cref="Dispose"/> releases the token source only after
    /// this task finishes, so tests can assert the loop really ended before teardown.
    /// </summary>
    internal Task PollingLoopCompletion => listenerTask;

    /// <summary>
    /// 只停止轮询，不等待也不释放资源的关闭请求阶段入口：先让转发信号不再派发，
    /// 退出时的资源收尾留给 <see cref="Dispose"/>。
    /// </summary>
    public void StopListening()
    {
        if (stopRequested)
        {
            return;
        }

        stopRequested = true;
        cancellationTokenSource.Cancel();
    }

    private void Listen()
    {
        try
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                if (!waitForSignal(PollInterval))
                {
                    continue;
                }

                if (cancellationTokenSource.IsCancellationRequested)
                {
                    return;
                }

                onSignalRaised();
            }
        }
        catch (Exception ex)
        {
            // Listener stopped — non-critical.
            LauncherLog.LogSync(LogEntrySeverity.Warn, "CrossProcess", "listener loop exited: " + ex.Message);
        }
    }

    /// <summary>
    /// 停止并释放。退出清理跑在 UI 线程上，因此这里绝不同步等待轮询循环
    /// （审计 AUD-PERF-010）：取消后循环至多再走完一个轮询间隔，收尾转到线程池，
    /// 任务结束后才释放令牌源，迟到的续体因此不会注册到已释放的源。
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        StopListening();

        if (listenerTask.IsCompleted)
        {
            ReleaseCancellationSource();
            return;
        }

        _ = FinishThenReleaseAsync();
    }

    private async Task FinishThenReleaseAsync()
    {
        try
        {
            await listenerTask.ConfigureAwait(false);
        }
        catch
        {
            // 循环体自己吞掉异常；这里只保证释放前任务已经终结。
        }

        ReleaseCancellationSource();
    }

    private void ReleaseCancellationSource()
    {
        try
        {
            cancellationTokenSource.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // 并发释放按幂等处理。
        }
    }
}
