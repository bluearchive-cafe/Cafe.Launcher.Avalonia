using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>Provides the shared transport and cooperative operation controls for a transfer.</summary>
public sealed record FileDownloadOperationControl(
    HttpClient HttpClient,
    Func<Task> WaitWhilePausedAsync,
    Func<long, CancellationToken, Task> ReportProgressAsync,
    bool ConnectionUsesProxy);
