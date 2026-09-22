using System;
using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services.Update;

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
        try
        {
            var body = await transport
                .GetStreamAsync(new Uri(file.Url), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await using var stream = body.Content;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var buffer = new char[MaxTextAssetBytes];
            var read = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (reader.Peek() != -1)
            {
                return null;
            }

            return new string(buffer, 0, read);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return null;
        }
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
        try
        {
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            DeleteFileIfExists(partPath);

            var body = await transport
                .GetStreamAsync(new Uri(file.Url), cancellationToken: cancellationToken)
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
                            progress?.Report(new LauncherUpdateProgress(total, body.DeclaredContentLength));
                            lastReported = total;
                        }
                    }

                    progress?.Report(new LauncherUpdateProgress(total, body.DeclaredContentLength));
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DeleteFileIfExists(partPath);
            return LauncherUpdateDownloadResult.Failed(
                LauncherUpdateDownloadStatus.Cancelled,
                "The launcher update download was cancelled.");
        }
        catch (OperationCanceledException exception)
        {
            DeleteFileIfExists(partPath);
            return LauncherUpdateDownloadResult.Failed(
                LauncherUpdateDownloadStatus.NetworkFailure,
                exception.Message);
        }
        catch (HttpRequestException exception)
        {
            DeleteFileIfExists(partPath);
            return LauncherUpdateDownloadResult.Failed(
                LauncherUpdateDownloadStatus.NetworkFailure,
                exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            DeleteFileIfExists(partPath);
            return LauncherUpdateDownloadResult.Failed(
                LauncherUpdateDownloadStatus.NetworkFailure,
                exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DeleteFileIfExists(partPath);
            return LauncherUpdateDownloadResult.Failed(
                LauncherUpdateDownloadStatus.IoFailure,
                exception.Message);
        }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is HttpRequestException or InvalidOperationException or IOException
        || exception is OperationCanceledException;

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
