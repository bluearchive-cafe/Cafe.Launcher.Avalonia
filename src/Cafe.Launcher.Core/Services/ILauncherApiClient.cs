using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// 官方 HTTP API 的读写面（游戏配置、CDN 配置、清单地址、远程清单与补丁源还原）。
/// 表现层的下载族与 Core 的启动器核心服务只经它访问远端；传输、签名头与补丁源规则
/// 属于实现细节。
/// </summary>
public interface ILauncherApiClient
{
    Task<GameConfigResponse> GetGameConfigAsync(CancellationToken cancellationToken = default);

    Task<BaseConfigResponse> GetBaseConfigAsync(CancellationToken cancellationToken = default);

    Task<CdnConfigResponse> GetCdnConfigAsync(string patchUrlGroup, CancellationToken cancellationToken = default);

    Task<OperationsResourceResponse> GetOperationsResourceAsync(CancellationToken cancellationToken = default);

    Task<SocialMediaResourceResponse> GetSocialMediaResourceAsync(CancellationToken cancellationToken = default);

    Task<InstallationConfigResponse> GetInstallationConfigAsync(CancellationToken cancellationToken = default);

    Task<ManifestUrlResponse> GetManifestUrlAsync(
        string version,
        string filePath,
        string patchUrlGroup,
        CancellationToken cancellationToken = default);

    Task<RemoteManifest> GetRemoteManifestAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>把任意 CDN 地址还原成官方补丁源地址。</summary>
    string RestoreOfficialPackageUrl(string url);
}
