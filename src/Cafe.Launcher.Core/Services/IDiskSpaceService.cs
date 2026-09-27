using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// 磁盘空间判定的窄接口。表现层只问「够不够」，不关心缓存与路径回退策略——那些留在实现里。
/// </summary>
public interface IDiskSpaceService
{
    /// <summary>目标路径所在卷的可用字节数；路径不存在或无法判定时返回 <c>null</c>。</summary>
    long? GetAvailableBytes(string path);

    /// <summary>可用空间是否满足需求。</summary>
    bool HasEnoughSpace(string path, long requiredBytes);

    /// <summary>完整判定（含不可判定与不足两种结果的区分）。</summary>
    DiskSpaceCheckResult Check(string path, long requiredBytes);

    /// <summary>按下载计划推算本次所需空间（含解压预留）。</summary>
    long ResolveRequiredBytes(bool isFreshInstall, long plannedDownloadBytes, string? decompressionSize);
}
