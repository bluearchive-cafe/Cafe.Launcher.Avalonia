using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>Provides the shared transport and cooperative operation controls for a transfer.</summary>
/// <param name="ReportProgressAsync">Reports transferred bytes.</param>
/// <param name="ReportProgressResetAsync">
/// Asks the progress owner to resample valid downloaded bytes after discarded
/// temporary data is removed.
/// </param>
public sealed record FileDownloadOperationControl(
    HttpClient HttpClient,
    Func<Task> WaitWhilePausedAsync,
    Func<long, CancellationToken, Task> ReportProgressAsync,
    Func<CancellationToken, Task> ReportProgressResetAsync,
    bool ConnectionUsesProxy);
