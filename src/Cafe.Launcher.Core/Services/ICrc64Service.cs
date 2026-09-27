using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// 目录清单 CRC-64 校验的窄接口。表现层（下载/修复）只经它取用，实现因此可以保持
/// <c>internal</c>——公开面只留「能做什么」，不留「怎么算」。
/// </summary>
public interface ICrc64Service
{
    /// <summary>计算文件的 CRC-64 十六进制值（小写，16 位）。</summary>
    /// <param name="filePath">待校验文件。</param>
    /// <param name="progress">可选进度回调（0–100）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<string> ComputeFileAsync(
        string filePath,
        Action<int>? progress = null,
        CancellationToken cancellationToken = default);
}
