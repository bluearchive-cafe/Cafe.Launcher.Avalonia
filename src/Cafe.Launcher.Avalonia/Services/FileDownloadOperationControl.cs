using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Cooperative operation controls for one transfer. Everything here is
/// caller-owned policy — the batch transport travels in
/// <see cref="Transport"/>, and the temp-file state machine lives inside
/// <see cref="IFileDownloadService"/>.
/// </summary>
/// <param name="Transport">The batch-scoped download transport (one per download batch).</param>
/// <param name="WaitWhilePausedAsync">Awaited between chunks to honor a cooperative pause.</param>
/// <param name="ReportProgressAsync">Reports transferred bytes.</param>
/// <param name="ReportProgressResetAsync">
/// Asks the progress owner to resample valid downloaded bytes after discarded
/// temporary data is removed.
/// </param>
public sealed record FileDownloadOperationControl(
    IDownloadTransport Transport,
    Func<Task> WaitWhilePausedAsync,
    Func<long, CancellationToken, Task> ReportProgressAsync,
    Func<CancellationToken, Task> ReportProgressResetAsync);
