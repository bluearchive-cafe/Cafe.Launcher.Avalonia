using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Bounds each streaming response-body read so a silently stalled connection
/// (headers received, then no bytes and no TCP reset) surfaces as an
/// <see cref="HttpRequestException"/> instead of hanging the caller forever.
/// Under <see cref="HttpCompletionOption.ResponseHeadersRead"/>, HttpClient.Timeout
/// only covers the time until headers arrive; the body read is otherwise unbounded.
/// The budget is idle-per-read — every successful read resets it by construction,
/// so slow-but-progressing transfers are never aborted, and a timeout converts to
/// <see cref="HttpRequestException"/> so existing retry/resume paths take over.
/// </summary>
internal static class ResponseBodyReader
{
    /// <summary>
    /// Idle budget applied to each body read. Generous enough for saturated links
    /// and CDN hiccups; a healthy connection delivers bytes far more often.
    /// </summary>
    public static readonly TimeSpan DefaultIdleReadTimeout = TimeSpan.FromSeconds(60);

    public static async Task<int> ReadAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken,
        TimeSpan? idleReadTimeout = null)
    {
        var timeout = idleReadTimeout ?? DefaultIdleReadTimeout;
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idleCts.CancelAfter(timeout);
        try
        {
            return await stream.ReadAsync(buffer, idleCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The linked token fired while the caller's token is still live, so no
            // bytes arrived within budget: report a stall, not a cancellation.
            throw new HttpRequestException(
                $"Remote response body stalled: no bytes within {FormatTimeout(timeout)}.");
        }
    }

    private static string FormatTimeout(TimeSpan timeout) =>
        timeout.TotalSeconds >= 1
            ? $"{timeout.TotalSeconds:F0}s"
            : $"{timeout.TotalMilliseconds:F0}ms";
}
