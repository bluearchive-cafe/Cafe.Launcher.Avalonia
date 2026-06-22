using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Caches downloaded images (e.g., launcher background) by CRC64 hash.
/// Mirrors the original Electron launcher's IndexedDB image cache.
/// </summary>
public sealed class ImageCacheService : IDisposable
{
    private const int MaxImageBytes = 25 * 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private readonly string cacheDir;
    private readonly IHttpClientLeaseSource httpClientLeaseSource;
    private readonly Crc64Service crc64Service;
    private readonly RemoteHttpUrlValidator urlValidator;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> cacheLocks =
        new(StringComparer.Ordinal);
    private bool disposed;

    public ImageCacheService(
        HttpClientFactory httpClientFactory,
        Crc64Service crc64Service,
        RemoteHttpUrlValidator urlValidator)
        : this(
            new ProxyAwareHttpClientLeaseSource(
                httpClientFactory,
                baseAddress: null,
                timeout: RequestTimeout),
            crc64Service,
            urlValidator,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                LauncherConstants.ProductName,
                "image-cache"))
    {
    }

    internal ImageCacheService(
        IHttpClientLeaseSource httpClientLeaseSource,
        Crc64Service crc64Service,
        RemoteHttpUrlValidator urlValidator,
        string cacheDir)
    {
        this.httpClientLeaseSource = httpClientLeaseSource;
        this.crc64Service = crc64Service;
        this.urlValidator = urlValidator;
        this.cacheDir = cacheDir;
        try
        {
            Directory.CreateDirectory(cacheDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cache directory is non-critical — log and continue without caching
            System.Diagnostics.Debug.WriteLine($"ImageCacheService: failed to create cache directory: {ex.Message}");
        }
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
    /// Returns the local file path.
    /// </summary>
    public Task<string> CacheImageAsync(string url, string crc64Hash, CancellationToken ct = default)
    {
        return CacheImageAsync(url, crc64Hash, ProxyModes.Direct, ct);
    }

    public async Task<string> CacheImageAsync(
        string url,
        string crc64Hash,
        string proxyMode,
        CancellationToken ct = default)
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
                var bytes = await GetImageBytesAsync(url, proxyMode, ct).ConfigureAwait(false);
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

    public Task<byte[]> GetImageBytesAsync(string url, CancellationToken ct = default)
    {
        return GetImageBytesAsync(url, ProxyModes.Direct, ct);
    }

    public async Task<byte[]> GetImageBytesAsync(
        string url,
        string proxyMode,
        CancellationToken ct = default)
    {
        using var lease = await CreateRequestClientAsync(proxyMode, ct).ConfigureAwait(false);
        using var response = await RemoteHttpRequestService.SendAsync(
            lease.Client,
            new Uri(url),
            static uri => new HttpRequestMessage(HttpMethod.Get, uri),
            urlValidator,
            ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxImageBytes)
        {
            throw new InvalidDataException("Image response is too large.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, ct).ConfigureAwait(false);
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

    private async Task<HttpClientLease> CreateRequestClientAsync(string proxyMode, CancellationToken ct)
    {
        return await httpClientLeaseSource
            .CreateLeaseAsync(proxyMode, ct)
            .ConfigureAwait(false);
    }

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
        httpClientLeaseSource.Dispose();
    }
}
