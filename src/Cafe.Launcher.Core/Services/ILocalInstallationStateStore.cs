using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// 游戏目录内安装状态（<c>game-launcher-config.json</c> 与 <c>manifest.json</c>）的唯一读写入口。
/// 表现层的下载/卸载/启动流程只经它读写；同一路径的串行化与原子替换属于实现细节。
/// </summary>
public interface ILocalInstallationStateStore
{
    /// <summary>读取安装状态；文件缺失或损坏时返回 <c>NotInstalled</c> 态而非抛错。</summary>
    Task<LocalInstallationState> ReadAsync(
        string gamePath,
        CancellationToken cancellationToken = default);

    /// <summary>按提交内容重写两个状态文件（先写临时文件再原子替换）。</summary>
    Task<LocalInstallationState> CommitAsync(
        string gamePath,
        LocalInstallationStateCommit commit,
        CancellationToken cancellationToken = default);

    /// <summary>删除两个状态文件并返回删除后的状态。</summary>
    Task<LocalInstallationState> DeleteAsync(
        string gamePath,
        CancellationToken cancellationToken = default);
}
