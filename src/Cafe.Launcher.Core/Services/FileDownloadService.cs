using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services.Diagnostics;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// Production implementation of <see cref="IFileDownloadService"/>.
/// </summary>
internal sealed class FileDownloadService : IFileDownloadService
{
    /// <summary>
    /// Retry domain order: 0 = backup CDN, 1 = primary CDN (matching the original Electron launcher).
    /// The first 4 attempts use the primary CDN, then 3 on backup, then 3 on primary.
    /// </summary>
    internal static readonly int[] RetryDomainOrder = [1, 1, 1, 1, 0, 0, 0, 1, 1, 1];

    /// <summary>
    /// <see cref="Exception.Data"/> 标记键：响应体读取中途以 <see cref="IOException"/> 失败
    /// （连接被对端关闭，<c>HttpIOException</c>）。只有网络读会打上这个标记；本地磁盘 IO
    /// 的 <see cref="IOException"/> 不会打，重试判据据此区分「可重试的网络中断」与
    /// 「必须原样上抛、保留可续传 .tmp 的本地失败」。
    /// </summary>
    private const string NetworkReadFailureDataKey = "Cafe.Launcher.FileDownload.NetworkReadFailure";

    private readonly ICrc64Service crc64Service;
    private readonly ILauncherDiagnostics diagnostics;
    private readonly TimeSpan? idleReadTimeout;

    public FileDownloadService(
        ICrc64Service crc64Service,
        ILauncherDiagnostics diagnostics,
        TimeSpan? idleReadTimeout = null)
    {
        this.crc64Service = crc64Service;
        this.diagnostics = diagnostics;
        this.idleReadTimeout = idleReadTimeout;
    }

    /// <inheritdoc />
    public long GetExistingDownloadedSize(string targetTempPath, long expectedSize) =>
        StatExistingBytes(targetTempPath, expectedSize);

    /// <summary>
    /// The single implementation of the .tmp length semantics shared by the
    /// resume decisions in <see cref="DownloadAsync"/> and by batch-level
    /// progress seeding: 0 when the file is absent or larger than expected.
    /// </summary>
    private static long StatExistingBytes(string targetTempPath, long expectedSize)
    {
        if (expectedSize <= 0 || !File.Exists(targetTempPath))
        {
            return 0;
        }

        var length = new FileInfo(targetTempPath).Length;
        return length <= expectedSize ? length : 0;
    }

    public async Task<DownloadOutcome> DownloadAsync(
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
        var transport = control.Transport;
        var pauseAwaiter = control.WaitWhilePausedAsync;
        var onProgressAsync = control.ReportProgressAsync;
        var onProgressResetAsync = control.ReportProgressResetAsync;
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
                var existingLength = StatExistingBytes(targetTempPath, expectedSize);
                if (expectedSize > 0 && existingLength == expectedSize)
                {
                    // 上次会话已下满但未验证：显式 AlreadyComplete，
                    // 调用方在安装阶段自行校验。
                    return DownloadOutcome.AlreadyComplete();
                }

                var declaredLength = File.Exists(targetTempPath)
                    ? new FileInfo(targetTempPath).Length
                    : 0;
                if (declaredLength > expectedSize && expectedSize > 0)
                {
                    File.Delete(targetTempPath);
                    existingLength = 0;
                    await onProgressResetAsync(cancellationToken).ConfigureAwait(false);
                }

                var initialUri = new Uri(downloadUrl);
                using var response = await transport.SendAsync(
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
                    cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var fileMode = FileMode.Create;
                // A resume request answered with 200 means the server ignored Range and sent
                // the whole body: the file must be rebuilt from zero and the batch counters
                // seeded from the old length must resample before the new bytes are added.
                var restartProgressFromZero = existingLength > 0;
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
                    restartProgressFromZero = false;
                }

                string crc64;
                // Rented rather than allocated: the buffer sits inside the retry loop, so a fresh
                // 256 KiB array per attempt (>85 KiB LOH threshold) would put every download into
                // the large object heap up to RetryDomainOrder.Length times per file — same LOH
                // concern ICrc64Service already solves with the shared pool.
                var buffer = ArrayPool<byte>.Shared.Rent(1024 * 256);
                try
                {
                    await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    await using var output = new FileStream(targetTempPath, fileMode, FileAccess.Write, FileShare.Read);
                    if (restartProgressFromZero)
                    {
                        // FileMode.Create already truncated the .tmp when the stream opened, so
                        // this resample sees zero and the full body counted below lands on a
                        // cleared slate instead of on top of the old bytes (AUD-DL-002).
                        await onProgressResetAsync(cancellationToken).ConfigureAwait(false);
                    }

                    while (true)
                    {
                        await pauseAwaiter().ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();

                        var read = await ReadResponseBodyAsync(responseStream, buffer, cancellationToken).ConfigureAwait(false);
                        if (read == 0) break;
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        await onProgressAsync(read, cancellationToken).ConfigureAwait(false);
                    }

                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    crc64 = await crc64Service.ComputeFileAsync(targetTempPath, null, control.ReportVerificationProgress, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                if (crc64 == expectedHash) return DownloadOutcome.Transferred(crc64);

                var downloadedLength = new FileInfo(targetTempPath).Length;
                File.Delete(targetTempPath);
                await onProgressResetAsync(cancellationToken).ConfigureAwait(false);

                await diagnostics.MessageAsync(
                    "FileDownload",
                    $"CRC64 mismatch after download{Environment.NewLine}" +
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
            catch (Exception ex) when (IsRetryableTransferFailure(ex, cancellationToken))
            {
                // HttpClient/ConnectTimeout cancel their own token, not the caller's.
                // Keep these failures in the network retry/resume path; preserve the
                // inner exception (including the transport's Fake-IP diagnostic marker).
                var failure = ex is OperationCanceledException
                    ? new HttpRequestException($"Download request timed out: {filePath}.", ex)
                    : ex;
                if (failure is InvalidDataException)
                {
                    // Only content-level failures invalidate the .tmp (CRC64 mismatch or an
                    // invalid Content-Range): discard it and resample progress. Network
                    // failures — and network body reads cut short by the peer — keep the
                    // resumable bytes. Local disk failures never reach this filter.
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

                await diagnostics.WarningAsync(
                    "FileDownload",
                    $"Transfer attempt {retryIndex + 1}/{RetryDomainOrder.Length} failed: " +
                    $"file={filePath}, host={new Uri(downloadUrl).Host}, " +
                    $"error={failure.GetType().Name}: {failure.Message}",
                    CancellationToken.None).ConfigureAwait(false);
                lastError = failure;
                if (retryIndex >= RetryDomainOrder.Length - 1)
                {
                    if (ReferenceEquals(failure, ex)) throw;
                    throw failure;
                }
            }
        }

        throw new HttpRequestException($"Download failed: {filePath}", lastError);
    }

    /// <summary>
    /// Reads one body chunk, tagging peer-side <see cref="IOException"/> failures as
    /// network failures so the retry filter keeps them in the resume domain. Local
    /// disk IO comes from the <see cref="FileStream"/> writes in <see cref="DownloadAsync"/>
    /// and is never tagged.
    /// </summary>
    private async Task<int> ReadResponseBodyAsync(
        Stream responseStream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ResponseBodyReader
                .ReadAsync(responseStream, buffer, cancellationToken, idleReadTimeout)
                .ConfigureAwait(false);
        }
        catch (IOException networkFailure)
        {
            networkFailure.Data[NetworkReadFailureDataKey] = true;
            throw;
        }
    }

    /// <summary>
    /// Decides whether a failure stays in the CDN retry/resume domain. Only
    /// transfer-shaped failures qualify: <see cref="HttpRequestException"/> (the
    /// transport's error mode, including its timeouts), <see cref="InvalidDataException"/>
    /// (CRC64 mismatch or an invalid Content-Range, which discard the .tmp), and body
    /// reads the peer cut short (tagged <see cref="IOException"/>). Local disk failures —
    /// <see cref="IOException"/>, <see cref="UnauthorizedAccessException"/>,
    /// <see cref="PathTooLongException"/> and friends — propagate as-is: retrying them
    /// cannot help, deleting the .tmp would throw away resumable progress, and the
    /// session layer maps disk-full and permission problems into user-facing messages.
    /// </summary>
    private static bool IsRetryableTransferFailure(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException)
        {
            return !cancellationToken.IsCancellationRequested;
        }

        if (exception is HttpRequestException or InvalidDataException)
        {
            return true;
        }

        return exception is IOException && exception.Data.Contains(NetworkReadFailureDataKey);
    }

    /// <summary>
    /// Build the full download URL from a CDN domain, source path, and file path.
    /// The domain's authority (including an explicit port) and path prefix are
    /// preserved: upstream CDN domains are plain hosts today, but silently dropping
    /// a path or port would misroute every file request the moment one appears.
    /// </summary>
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
        var prefix = uri.AbsolutePath.TrimEnd('/');
        return $"{uri.Scheme}://{uri.Authority}{prefix}/{string.Join("/", pathItems)}";
    }

    /// <summary>Resolve CDN URL for a retry attempt.</summary>
    internal static string? ResolveRetryDomain(CdnConfigResponse cdnConfig, int retryType)
    {
        return retryType == 0 ? cdnConfig.BackUpCdn : cdnConfig.PrimaryCdn;
    }
}
