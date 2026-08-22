using System;
using System.Threading;
using Serilog.Debugging;
using Serilog.Sinks.Async;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>
/// Observes the Serilog.Async queue without adding work to the application
/// logging path. Dropped events are reported through Serilog's out-of-band
/// SelfLog channel so the monitor cannot recursively enqueue another event.
/// </summary>
internal sealed class AsyncLogBufferMonitor : IAsyncLogEventSinkMonitor, IDisposable
{
    private static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromSeconds(30);

    private readonly TimeSpan checkInterval;
    private readonly Action<string> report;
    private IAsyncLogEventSinkInspector? inspector;
    private Timer? timer;
    private long lastReportedDroppedMessages;

    internal AsyncLogBufferMonitor(
        TimeSpan? checkInterval = null,
        Action<string>? report = null)
    {
        this.checkInterval = checkInterval ?? DefaultCheckInterval;
        this.report = report ?? (message => SelfLog.WriteLine(message));
    }

    public void StartMonitoring(IAsyncLogEventSinkInspector inspector)
    {
        Interlocked.Exchange(ref this.inspector, inspector);
        timer = new Timer(
            static state => ((AsyncLogBufferMonitor)state!).CheckNow(),
            this,
            this.checkInterval,
            this.checkInterval);
    }

    public void StopMonitoring(IAsyncLogEventSinkInspector inspector)
    {
        var activeTimer = Interlocked.Exchange(ref timer, null);
        activeTimer?.Dispose();

        Check(inspector);
        Interlocked.Exchange(ref this.inspector, null);
    }

    internal void CheckNow()
    {
        var activeInspector = Volatile.Read(ref inspector);
        if (activeInspector is not null)
        {
            Check(activeInspector);
        }
    }

    private void Check(IAsyncLogEventSinkInspector activeInspector)
    {
        try
        {
            var droppedMessages = activeInspector.DroppedMessagesCount;
            var previousCount = Interlocked.Read(ref lastReportedDroppedMessages);

            if (droppedMessages <= previousCount)
            {
                return;
            }

            Interlocked.Exchange(ref lastReportedDroppedMessages, droppedMessages);
            report(
                $"Serilog async sink dropped {droppedMessages} messages; " +
                $"buffer usage is {activeInspector.Count}/{activeInspector.BufferSize}.");
        }
        catch (ObjectDisposedException)
        {
            // The sink may be disposing while the timer callback is running.
        }
        catch
        {
            // Diagnostics monitoring must never affect application shutdown.
        }
    }

    public void Dispose()
    {
        var activeTimer = Interlocked.Exchange(ref timer, null);
        activeTimer?.Dispose();
        Interlocked.Exchange(ref inspector, null);
        GC.SuppressFinalize(this);
    }
}
