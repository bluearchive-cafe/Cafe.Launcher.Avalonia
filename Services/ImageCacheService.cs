using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Caches downloaded images (e.g., launcher background) by CRC64 hash.
/// Mirrors the original Electron launcher's IndexedDB image cache.
/// </summary>
public sealed class ImageCacheService : IDisposable
{
    private readonly string cacheDir;
    private readonly SocketsHttpHandler handler;
    private readonly HttpClient httpClient;

    public ImageCacheService()
    {
        cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LauncherConstants.ProductName,
            "image-cache");
        Directory.CreateDirectory(cacheDir);

        handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        };
        httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>
    /// Returns the cached file path if a cached copy exists for the given CRC64 hash.
    /// </summary>
    public string? GetCachedPath(string crc64Hash)
    {
        if (string.IsNullOrWhiteSpace(crc64Hash))
            return null;
        var cachePath = Path.Combine(cacheDir, $"{crc64Hash}.cache");
        return File.Exists(cachePath) ? cachePath : null;
    }

    /// <summary>
    /// Downloads an image from the given URL and caches it under the CRC64 hash.
    /// Returns the local file path.
    /// </summary>
    public async Task<string> CacheImageAsync(string url, string crc64Hash, CancellationToken ct = default)
    {
        var cachePath = Path.Combine(cacheDir, $"{crc64Hash}.cache");
        var bytes = await httpClient.GetByteArrayAsync(url, ct);
        await File.WriteAllBytesAsync(cachePath, bytes, ct);
        return cachePath;
    }

    public Task<byte[]> GetImageBytesAsync(string url, CancellationToken ct = default)
    {
        return httpClient.GetByteArrayAsync(url, ct);
    }

    public void Dispose()
    {
        httpClient.Dispose();
        handler.Dispose();
    }
}
