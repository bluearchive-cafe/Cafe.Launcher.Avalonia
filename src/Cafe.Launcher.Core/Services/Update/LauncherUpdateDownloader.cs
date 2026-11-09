using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services.Update;

/// <summary>
/// Downloads launcher release assets over the shared remote transport. The package
/// stream is hashed as it is written to a <c>.part</c> file, then promoted to the
/// destination only when the digest matches the expected value — a mismatched or
/// truncated download never becomes a runnable file.
/// </summary>
internal sealed class LauncherUpdateDownloader : ILauncherUpdateDownloader
{
    /// <summary>Report progress once per this many bytes rather than per read chunk.</summary>
    internal const long ProgressReportThresholdBytes = 256 * 1024;

    /// <summary>Upper bound for a text asset; SHA256SUMS is a few hundred bytes.</summary>
    internal const int MaxTextAssetBytes = 256 * 1024;

    private const int BufferSize = 81_920;

    private readonly IRemoteHttpTransport transport;

    public LauncherUpdateDownloader(IRemoteHttpTransport transport)
    {
        this.transport = transport;
    }

    /// <inheritdoc />
    public async Task<string?> ReadTextAsync(ReleaseFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        Exception? lastFailure = null;
        foreach (var uri in CreateCandidateUris(file))
        {
            try
            {
                return await ReadTextOnceAsync(uri, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsRetryableTransportFailure(exception, cancellationToken))
            {
                lastFailure = exception;
            }
        }

        throw lastFailure!;
    }

    private async Task<string?> ReadTextOnceAsync(Uri uri, CancellationToken cancellationToken)
    {
        var body = await transport
            .GetStreamAsync(uri, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await using var stream = body.Content;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var buffer = new char[MaxTextAssetBytes + 1];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        if (read > MaxTextAssetBytes)
        {
            return null;
        }

        return new string(buffer, 0, read);
    }

    /// <inheritdoc />
    public async Task<LauncherUpdateDownloadResult> DownloadAndVerifyAsync(
        ReleaseFile file,
        string expectedSha256,
        string destinationPath,
        IProgress<LauncherUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var partPath = destinationPath + ".part";
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var uris = CreateCandidateUris(file);
        Exception? lastFailure = null;
        for (var index = 0; index < uris.Length; index++)
        {
            var isLast = index + 1 == uris.Length;
            try
            {
                var result = await DownloadOnceAsync(
                    file,
                    uris[index],
                    expectedSha256,
                    destinationPath,
                    partPath,
                    progress,
                    cancellationToken).ConfigureAwait(false);

                if (result.IsSuccess || isLast)
                {
                    return result;
                }

                // 镜像包哈希不符时再取一次可信的 GitHub 原包；两次都经过 SHA-256 校验。
                lastFailure = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DeleteFileIfExists(partPath);
                return LauncherUpdateDownloadResult.Failed(
                    LauncherUpdateDownloadStatus.Cancelled,
                    "The launcher update download was cancelled.");
            }
            catch (Exception exception) when (IsRetryableTransportFailure(exception, cancellationToken))
            {
                DeleteFileIfExists(partPath);
                lastFailure = exception;
                if (isLast)
                {
                    return MapTransportFailure(exception);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                DeleteFileIfExists(partPath);
                return LauncherUpdateDownloadResult.Failed(
                    LauncherUpdateDownloadStatus.IoFailure,
                    exception.Message);
            }
        }

        return lastFailure is { } failure
            ? MapTransportFailure(failure)
            : LauncherUpdateDownloadResult.Failed(
                LauncherUpdateDownloadStatus.NetworkFailure,
                "No download source was available.");
    }

    private async Task<LauncherUpdateDownloadResult> DownloadOnceAsync(
        ReleaseFile file,
        Uri uri,
        string expectedSha256,
        string destinationPath,
        string partPath,
        IProgress<LauncherUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        DeleteFileIfExists(partPath);

        var body = await transport
            .GetStreamAsync(uri, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await using var source = body.Content;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var destination = new FileStream(
            partPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            useAsync: true))
        {
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                long total = 0;
                long lastReported = 0;
                var startedAt = Stopwatch.GetTimestamp();
                int read;
                while ((read = await source
                    .ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)
                    .ConfigureAwait(false)) > 0)
                {
                    await destination
                        .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    total += read;
                    if (total - lastReported >= ProgressReportThresholdBytes)
                    {
                        progress?.Report(CreateProgress(total, body.DeclaredContentLength, startedAt));
                        lastReported = total;
                    }
                }

                progress?.Report(CreateProgress(total, body.DeclaredContentLength, startedAt));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        var actual = Convert.ToHexString(hash.GetHashAndReset());
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            DeleteFileIfExists(partPath);
            return LauncherUpdateDownloadResult.Failed(
                LauncherUpdateDownloadStatus.ChecksumMismatch,
                $"SHA-256 mismatch for {file.Name}: expected {expectedSha256}, got {actual}.");
        }

        File.Move(partPath, destinationPath, overwrite: true);
        return LauncherUpdateDownloadResult.Succeeded(destinationPath);
    }

    /// <summary>镜像优先、GitHub 兜底；顺序由更新服务校验并决定。</summary>
    private static Uri[] CreateCandidateUris(ReleaseFile file)
    {
        if (string.IsNullOrWhiteSpace(file.MirrorUrl))
        {
            return [new Uri(file.Url)];
        }

        return [new Uri(file.MirrorUrl), new Uri(file.Url)];
    }

    private static bool IsRetryableTransportFailure(
        Exception exception,
        CancellationToken cancellationToken) =>
        exception is HttpRequestException or InvalidOperationException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static LauncherUpdateDownloadResult MapTransportFailure(Exception exception)
    {
        if (exception is InvalidOperationException
            && RemoteHttpRequestService.IsBlockedAddressRejection(exception))
        {
            return LauncherUpdateDownloadResult.Failed(
                LauncherUpdateDownloadStatus.UrlRejected,
                exception.Message);
        }

        return LauncherUpdateDownloadResult.Failed(
            LauncherUpdateDownloadStatus.NetworkFailure,
            exception.Message);
    }

    private static LauncherUpdateProgress CreateProgress(long downloadedBytes, long? totalBytes, long startedAt)
    {
        var elapsedSeconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
        var bytesPerSecond = elapsedSeconds > 0
            ? (long)(downloadedBytes / elapsedSeconds)
            : 0;
        return new LauncherUpdateProgress(downloadedBytes, totalBytes, bytesPerSecond);
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup: a stranded .part file is a maintenance annoyance,
            // never a reason to change the operation's reported outcome.
        }
    }
}
