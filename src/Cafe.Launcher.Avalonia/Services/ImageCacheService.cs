using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Caches downloaded images (e.g., launcher background) by CRC64 hash.
/// Mirrors the original Electron launcher's IndexedDB image cache.
/// </summary>
public sealed class ImageCacheService : IDisposable
{
    private const int MaxImageBytes = 25 * 1024 * 1024;
    private static readonly TimeSpan RemoteImageCacheLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Entries older than this are swept once at startup. CRC-keyed .cache files and
    /// URL-keyed .remote files have no other eviction — the 24h freshness window only
    /// controls .remote reuse, not the file itself — so the sweep bounds disk growth
    /// for long-lived installs; anything evicted is simply re-downloaded when needed.
    /// Leftover .tmp files from crashed downloads age out the same way.
    /// </summary>
    internal static readonly TimeSpan CacheEntryLifetime = TimeSpan.FromDays(30);

    private readonly string cacheDir;
    private readonly IRemoteHttpTransport transport;
    private readonly Crc64Service crc64Service;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> cacheLocks =
        new(StringComparer.Ordinal);
    private bool disposed;

    public ImageCacheService(
        IRemoteHttpTransport transport,
        Crc64Service crc64Service,
        LauncherDataRoot dataRoot)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        this.transport = transport;
        this.crc64Service = crc64Service;
        this.cacheDir = dataRoot.ImageCacheDirectory;
        try
        {
            Directory.CreateDirectory(cacheDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cache directory is non-critical — log and continue without caching
            LocalDiagnostics.LogSync(LogEntrySeverity.Warn, "ImageCache", $"failed to create cache directory: {ex.Message}");
        }

        _ = Task.Run(CleanupExpiredEntries);
    }

    /// <summary>
    /// Returns the cached file path if a cached copy exists for the given CRC64 hash.
    /// </summary>
    public string? GetCachedPath(string crc64Hash)
    {
        if (string.IsNullOrWhiteSpace(crc64Hash))
            return null;
        // Defense-in-depth: reject hashes containing path separators or traversal sequences
        if (crc64Hash.Contains('/') || crc64Hash.Contains('\\') || crc64Hash.Contains(".."))
            return null;
        var cachePath = Path.Combine(cacheDir, $"{crc64Hash}.cache");
        return File.Exists(cachePath) ? cachePath : null;
    }

    public async Task<string?> GetCachedPathAsync(string crc64Hash, CancellationToken ct = default)
    {
        var cachePath = GetCachedPath(crc64Hash);
        if (cachePath is null)
        {
            return null;
        }

        var actual = await crc64Service.ComputeFileAsync(cachePath, null, ct).ConfigureAwait(false);
        if (string.Equals(actual, crc64Hash, StringComparison.OrdinalIgnoreCase))
        {
            return cachePath;
        }

        TryDelete(cachePath);
        return null;
    }

    /// <summary>
    /// Downloads an image from the given URL and caches it under the CRC64 hash.
    /// Returns the local file path. The proxy mode resolves from launcher settings
    /// via the transport.
    /// </summary>
    public Task<string> CacheImageAsync(string url, string crc64Hash, CancellationToken ct = default)
    {
        return CacheImageAsync(url, crc64Hash, ResolvedModeOptions(), ct);
    }

    private async Task<string> CacheImageAsync(
        string url,
        string crc64Hash,
        RemoteRequestOptions options,
        CancellationToken ct)
    {
        // Defense-in-depth: reject hashes containing path separators or traversal sequences
        if (crc64Hash.Contains('/') || crc64Hash.Contains('\\') || crc64Hash.Contains(".."))
            throw new ArgumentException("CRC64 hash contains invalid characters.", nameof(crc64Hash));

        var cachePath = Path.Combine(cacheDir, $"{crc64Hash}.cache");
        var cacheLock = cacheLocks.GetOrAdd(crc64Hash, static _ => new SemaphoreSlim(1, 1));
        await cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(cachePath))
            {
                return cachePath;
            }

            var tempPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                var bytes = await GetImageBytesAsync(new Uri(url), options, ct).ConfigureAwait(false);
                await File.WriteAllBytesAsync(tempPath, bytes, ct).ConfigureAwait(false);

                var actual = await crc64Service.ComputeFileAsync(tempPath, null, ct).ConfigureAwait(false);
                if (!string.Equals(actual, crc64Hash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Image CRC64 mismatch. Expected {crc64Hash}, actual {actual}.");
                }

                File.Move(tempPath, cachePath, overwrite: true);
                return cachePath;
            }
            finally
            {
                TryDelete(tempPath);
            }
        }
        finally
        {
            cacheLock.Release();
        }
    }

    /// <summary>
    /// Returns a URL-keyed image cache entry when it is still fresh, otherwise downloads and
    /// persists a new copy. Use this when the remote payload does not provide a content hash.
    /// The proxy mode resolves from launcher settings via the transport.
    /// </summary>
    public async Task<byte[]> GetCachedOrDownloadImageBytesAsync(
        string url,
        CancellationToken ct = default)
    {
        var cacheKey = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        var cachePath = Path.Combine(cacheDir, $"{cacheKey}.remote");
        var cacheLock = cacheLocks.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));
        await cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(cachePath)
                && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) <= RemoteImageCacheLifetime)
            {
                try
                {
                    return await File.ReadAllBytesAsync(cachePath, ct).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    TryDelete(cachePath);
                }
            }

            var bytes = await GetImageBytesAsync(new Uri(url), ResolvedModeOptions(), ct).ConfigureAwait(false);
            var tempPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(tempPath, bytes, ct).ConfigureAwait(false);
                File.Move(tempPath, cachePath, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A cache write is optional: callers can still render the downloaded image.
                // 豁免：缓存写入是可选优化，失败时调用方仍可直接渲染下载内容。
                System.Diagnostics.Debug.WriteLine(
                    $"ImageCacheService: failed to cache remote image: {exception.Message}");
            }
            finally
            {
                TryDelete(tempPath);
            }

            return bytes;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    /// <summary>
    /// Downloads the image bytes directly, pinning a direct connection: callers
    /// of this overload deliberately bypass the settings-resolved proxy mode.
    /// </summary>
    public Task<byte[]> GetImageBytesAsync(string url, CancellationToken ct = default)
    {
        return GetImageBytesAsync(new Uri(url), DirectModeOptions(), ct);
    }

    private async Task<byte[]> GetImageBytesAsync(
        Uri uri,
        RemoteRequestOptions options,
        CancellationToken ct)
    {
        var remote = await transport.GetStreamAsync(uri, options, ct).ConfigureAwait(false);
        using var input = remote.Content;
        if (remote.DeclaredContentLength is > MaxImageBytes)
        {
            throw new InvalidDataException("Image response is too large.");
        }

        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            // 停顿预算由传输层的流包装承担：每次读取都有空闲上限，
            // 静默断流会以 HttpRequestException 浮出而不是永久挂起。
            var read = await input.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > MaxImageBytes)
            {
                throw new InvalidDataException("Image response is too large.");
            }

            output.Write(buffer.AsSpan(0, read));
        }

        return output.ToArray();
    }

    private static RemoteRequestOptions ResolvedModeOptions() => new()
    {
        Timeout = RequestTimeout,
        ConfigureRequest = ConfigureImageRequest
    };

    private static RemoteRequestOptions DirectModeOptions() => new()
    {
        ProxyMode = ProxyModes.Direct,
        Timeout = RequestTimeout,
        ConfigureRequest = ConfigureImageRequest
    };

    private static void ConfigureImageRequest(HttpRequestMessage request) =>
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            $"CafeLauncher/{BuildInfo.LauncherVersion} (.NET)");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void CleanupExpiredEntries() => CleanupExpiredEntries(DateTimeOffset.UtcNow);

    /// <summary>
    /// Deletes cache entries whose last write precedes <see cref="CacheEntryLifetime"/>
    /// relative to <paramref name="utcNow"/>. Individual failures are ignored — a file
    /// concurrently in use simply survives until the next sweep.
    /// </summary>
    internal void CleanupExpiredEntries(DateTimeOffset utcNow)
    {
        var expiryThreshold = utcNow - CacheEntryLifetime;
        try
        {
            foreach (var pattern in new[] { "*.cache", "*.remote", "*.tmp" })
            {
                foreach (var file in Directory.EnumerateFiles(cacheDir, pattern))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < expiryThreshold)
                        {
                            File.Delete(file);
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // 单个文件失败不阻塞其余清理（可能正被并发读取）。
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LocalDiagnostics.LogSync(LogEntrySeverity.Warn, "ImageCache", $"cache sweep failed: {exception.Message}");
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        foreach (var semaphore in cacheLocks.Values)
        {
            semaphore.Dispose();
        }

        cacheLocks.Clear();
        GC.SuppressFinalize(this);
    }
}
