using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// 启动器更新检查（GitHub 发布清单 + 资产选择）。表现层只问「有没有新版本、这次发布能做什么」，
/// 取回与安装属于 <see cref="ILauncherSelfUpdateService"/> 与独立 helper。
/// </summary>
public interface ILauncherUpdateService
{
    /// <summary>检查指定渠道是否有新版本。</summary>
    Task<LauncherUpdateCheckResult> CheckForUpdateAsync(
        string updateChannel,
        CancellationToken cancellationToken = default);
}
