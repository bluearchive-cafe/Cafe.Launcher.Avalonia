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
    private readonly RemoteHttpUrlValidator urlValidator;

    public FileDownloadService(
        Crc64Service crc64Service,
        LocalDiagnostics diagnostics,
        RemoteHttpUrlValidator urlValidator)
    {
        this.crc64Service = crc64Service;
        this.diagnostics = diagnostics;
        this.urlValidator = urlValidator;
    }

    public async Task DownloadAsync(
        FileDownloadRequest request,
        FileDownloadOperationControl control,
        CancellationToken cancellationToken)
    {
        var targetTempPath = request.TargetTempPath;
        var cdnConfig = request.CdnConfig;
        var source = request.Source;
        var expectedSize = request.ExpectedSize;
        var expectedHash = request.ExpectedHash;
        var filePath = request.FilePath;
        var httpClient = control.HttpClient;
        var pauseAwaiter = control.WaitWhilePausedAsync;
        var onProgressAsync = control.ReportProgressAsync;
        var onProgressResetAsync = control.ReportProgressResetAsync;
        var connectionUsesProxy = control.ConnectionUsesProxy;
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
                if (existingLength == expectedSize && expectedSize > 0)
                {
                    return;
                }

                if (existingLength > expectedSize && expectedSize > 0)
                {
                    File.Delete(targetTempPath);
                    existingLength = 0;
                }

                var initialUri = new Uri(downloadUrl);
                using var response = await RemoteHttpRequestService.SendAsync(
                    httpClient,
                    initialUri,
                    uri =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, uri);
                        if (existingLength > 0)
                        {
                            request.Headers.Range = new RangeHeaderValue(existingLength, null);
                        }

                        return request;
                    },
                    urlValidator,
                    cancellationToken,
                    connectionUsesProxy).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var fileMode = FileMode.Create;
                if (existingLength > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent)
                {
                    var contentRange = response.Content.Headers.ContentRange;
                    if (contentRange?.Unit != "bytes"
                        || contentRange.From != existingLength
                        || contentRange.Length is { } contentLength
                        && contentLength != expectedSize)
                    {
                        throw new InvalidDataException(
                            $"Invalid Content-Range for resumed download: {filePath}.");
                    }

                    fileMode = FileMode.Append;
                }

                string crc64;
                {
                    await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    await using var output = new FileStream(targetTempPath, fileMode, FileAccess.Write, FileShare.Read);
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
                await onProgressResetAsync(cancellationToken).ConfigureAwait(false);

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
                if (ex is InvalidDataException || ex is not (HttpRequestException or IOException))
                {
                    var deleted = false;
                    try
                    {
                        if (File.Exists(targetTempPath))
                        {
                            File.Delete(targetTempPath);
                            deleted = true;
                        }
                    }
                    catch
                    {
                        // Best-effort cleanup; preserve the original transfer exception.
                    }

                    if (deleted)
                    {
                        await onProgressResetAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

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
