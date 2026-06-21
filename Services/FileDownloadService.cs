using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Service for downloading a single manifest file with retry domain cycling,
/// Range resume, CRC64 verification, cooperative pause, and cleanup.
/// </summary>
public interface IFileDownloadService
{
    /// <summary>
    /// Download one manifest file with CDN retry domain cycling.
    /// </summary>
    /// <param name="targetTempPath">Full path to the temporary output file.</param>
    /// <param name="cdnConfig">CDN configuration (primary/backup URLs used in retry order).</param>
    /// <param name="source">Source path segment for URL construction.</param>
    /// <param name="expectedSize">Expected file size in bytes. Skip download if temp file already matches.</param>
    /// <param name="expectedHash">Expected CRC64 hash as unsigned decimal string.</param>
    /// <param name="filePath">Relative file path within the game (used in URL construction and diagnostics).</param>
    /// <param name="httpClient">Pre-configured HTTP client (shared, caller-disposed).</param>
    /// <param name="pauseAwaiter">Cooperative pause: awaited inside the download loop.</param>
    /// <param name="onProgressAsync">Async progress callback, called with byte count per read.
    /// Throttling/speed limiting can be applied here since it runs inside the download loop.</param>
    /// <param name="cancellationToken">Propagates cancellation.</param>
    Task DownloadAsync(
        string targetTempPath,
        CdnConfigResponse cdnConfig,
        string source,
        long expectedSize,
        string expectedHash,
        string filePath,
        HttpClient httpClient,
        Func<Task> pauseAwaiter,
        Func<long, CancellationToken, Task> onProgressAsync,
        CancellationToken cancellationToken);
}

/// <summary>
/// Production implementation of <see cref="IFileDownloadService"/>.
/// </summary>
public sealed class FileDownloadService : IFileDownloadService
{
    /// <summary>
    /// Retry domain order: 0 = backup CDN, 1 = primary CDN (matching the original Electron launcher).
    /// The first 4 attempts use the primary CDN, then 3 on backup, then 3 on primary.
    /// </summary>
    internal static readonly int[] RetryDomainOrder = [1, 1, 1, 1, 0, 0, 0, 1, 1, 1];

    private readonly Crc64Service crc64Service;
    private readonly LocalDiagnostics diagnostics;

    public FileDownloadService(Crc64Service crc64Service, LocalDiagnostics diagnostics)
    {
        this.crc64Service = crc64Service;
        this.diagnostics = diagnostics;
    }

    public async Task DownloadAsync(
        string targetTempPath,
        CdnConfigResponse cdnConfig,
        string source,
        long expectedSize,
        string expectedHash,
        string filePath,
        HttpClient httpClient,
        Func<Task> pauseAwaiter,
        Func<long, CancellationToken, Task> onProgressAsync,
        CancellationToken cancellationToken)
    {
        var targetDirectory = Path.GetDirectoryName(targetTempPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        Exception? lastError = null;
        for (var retryIndex = 0; retryIndex < RetryDomainOrder.Length; retryIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var retryType = RetryDomainOrder[retryIndex];
            var downloadUrl = BuildDownloadUrl(ResolveRetryDomain(cdnConfig, retryType), source, filePath);

            try
            {
                var fi = new FileInfo(targetTempPath);
                var existingLength = fi.Exists ? fi.Length : 0;
                if (existingLength >= expectedSize && expectedSize > 0)
                {
                    return;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                if (existingLength > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(existingLength, null);
                }

                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                string crc64;
                {
                    await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    await using var output = new FileStream(targetTempPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                    var buffer = new byte[1024 * 256];
                    while (true)
                    {
                        await pauseAwaiter().ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();

                        var read = await responseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                        if (read == 0) break;
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        await onProgressAsync(read, cancellationToken).ConfigureAwait(false);
                    }

                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    crc64 = await crc64Service.ComputeFileAsync(targetTempPath, null, cancellationToken).ConfigureAwait(false);
                }

                if (crc64 == expectedHash) return;

                var downloadedLength = new FileInfo(targetTempPath).Length;
                File.Delete(targetTempPath);

                await diagnostics.MessageAsync(
                    "CRC64 mismatch after download",
                    $"file: {filePath}{Environment.NewLine}" +
                    $"expected: {expectedHash}{Environment.NewLine}" +
                    $"actual:   {crc64}{Environment.NewLine}" +
                    $"size: {downloadedLength} / expected: {expectedSize}",
                    CancellationToken.None);

                if (retryIndex >= RetryDomainOrder.Length - 1)
                {
                    throw new InvalidDataException(
                        $"CRC64 mismatch after all retries: {filePath}.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                try { File.Delete(targetTempPath); } catch { /* best-effort */ }
                lastError = ex;
                if (retryIndex >= RetryDomainOrder.Length - 1) throw;
            }
        }

        throw new HttpRequestException($"Download failed: {filePath}", lastError);
    }

    /// <summary>Build the full download URL from a CDN domain, source path, and file path.</summary>
    internal static string BuildDownloadUrl(string? domain, string source, string filePath)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new InvalidOperationException("CDN domain is empty.");
        }

        var uri = new Uri(domain);
        var pathItems = source
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Concat(filePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
            .Select(Uri.EscapeDataString)
            .ToList();
        return $"{uri.Scheme}://{uri.Host}/{string.Join("/", pathItems)}";
    }

    /// <summary>Resolve CDN URL for a retry attempt.</summary>
    internal static string? ResolveRetryDomain(CdnConfigResponse cdnConfig, int retryType)
    {
        return retryType == 0 ? cdnConfig.BackUpCdn : cdnConfig.PrimaryCdn;
    }
}
