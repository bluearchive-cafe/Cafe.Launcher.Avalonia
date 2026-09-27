using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// 远程图片缓存的窄接口。表现层只问「有没有缓存 / 给我字节」，缓存目录布局、CRC 校验与
/// 传输细节留在实现里。
/// </summary>
public interface IImageCacheService : IDisposable
{
    /// <summary>命中缓存时返回本地路径，否则 <c>null</c>（同步探测，不触发下载）。</summary>
    string? GetCachedPath(string crc64Hash);

    /// <summary>命中缓存时返回本地路径，否则 <c>null</c>。</summary>
    Task<string?> GetCachedPathAsync(string crc64Hash, CancellationToken ct = default);

    /// <summary>把远程图片写入缓存并返回本地路径。</summary>
    Task<string> CacheImageAsync(string url, string crc64Hash, CancellationToken ct = default);

    /// <summary>命中缓存则读缓存，否则下载并写入缓存。</summary>
    Task<byte[]> GetCachedOrDownloadImageBytesAsync(string url, CancellationToken ct = default);

    /// <summary>取图片字节（不经过缓存目录）。</summary>
    Task<byte[]> GetImageBytesAsync(string url, CancellationToken ct = default);
}
