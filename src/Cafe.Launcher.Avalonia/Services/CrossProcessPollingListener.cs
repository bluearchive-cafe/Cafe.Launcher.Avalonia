using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Polls a cross-process signal endpoint (named event or local socket) on a
/// background task and raises an injected callback when a signal is consumed.
/// The polling contract — 250 ms interval, auto-reset, best-effort cleanup —
/// lives in this module instead of inside the window bootstrap.
/// </summary>
internal sealed class CrossProcessPollingListener : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly Task listenerTask;
    private readonly Func<TimeSpan, bool> waitForSignal;
    private readonly Action onSignalRaised;
    private bool disposed;

    /// <summary>Initializes the listener and starts the background polling loop.</summary>
    public CrossProcessPollingListener(Func<TimeSpan, bool> waitForSignal, Action onSignalRaised)
    {
        this.waitForSignal = waitForSignal;
        this.onSignalRaised = onSignalRaised;
        listenerTask = Task.Run(Listen, cancellationTokenSource.Token);
    }

    /// <summary>Gets whether the listener is being stopped; dispatch should drop late signals.</summary>
    public bool IsCancellationRequested => cancellationTokenSource.IsCancellationRequested;

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
            Debug.WriteLine("CrossProcessPollingListener loop exited: " + ex.Message);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cancellationTokenSource.Cancel();

        try
        {
            // The loop polls every 250ms; after cancellation it exits within ~250ms.
            listenerTask.Wait(TimeSpan.FromMilliseconds(300));
        }
        catch
        {
            // Exit cleanup is best-effort.
        }

        cancellationTokenSource.Dispose();
    }
}
