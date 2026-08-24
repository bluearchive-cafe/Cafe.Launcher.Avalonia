using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Shared retry policy for HTTP requests with configurable attempt count
/// and backoff schedule. Unifies the retry logic previously duplicated in
/// <see cref="LauncherApiClient"/> and <see cref="ResourcePanelApiClient"/>.
/// </summary>
internal static class RetryPolicy
{
    /// <summary>
    /// Executes <paramref name="action"/> up to <paramref name="maxAttempts"/> times.
    /// Retries only on specific network exceptions; cancellation always
    /// propagates immediately.
    /// </summary>
    /// <param name="action">The async operation to execute and possibly retry.</param>
    /// <param name="maxAttempts">Total attempts (1 = single attempt, no retry).</param>
    /// <param name="backoff">Backoff duration for attempt index <c>i</c> (0-based, called after failure <c>i</c>).</param>
    /// <param name="onRetryableError">
    /// Optional filter — only exceptions matching this predicate are retried.
    /// If null, defaults to <c>ex is HttpRequestException or TaskCanceledException</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token that always throws immediately.</param>
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> action,
        int maxAttempts,
        Func<int, TimeSpan> backoff,
        CancellationToken cancellationToken,
        Func<Exception, bool>? onRetryableError = null)
    {
        var isRetryable = onRetryableError ?? (ex => ex is HttpRequestException or TaskCanceledException);
        Exception? lastException = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (isRetryable(ex))
            {
                lastException = ex;
                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(backoff(attempt), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        throw lastException!;
    }
}
