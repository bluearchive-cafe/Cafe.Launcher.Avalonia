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
    /// <summary>取游戏配置（含各服务器的资源版本与起始可执行文件）。</summary>
    Task<GameConfigResponse> GetGameConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>取基础配置（公告、更新渠道等无版本区分的远端状态）。</summary>
    Task<BaseConfigResponse> GetBaseConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>按补丁源分组取 CDN 配置（该组的地址族与相对路径模板）。</summary>
    Task<CdnConfigResponse> GetCdnConfigAsync(string patchUrlGroup, CancellationToken cancellationToken = default);

    /// <summary>取首页运营资源（横幅、公告卡）。</summary>
    Task<OperationsResourceResponse> GetOperationsResourceAsync(CancellationToken cancellationToken = default);

    /// <summary>取社交媒体资源（外链与图标）。</summary>
    Task<SocialMediaResourceResponse> GetSocialMediaResourceAsync(CancellationToken cancellationToken = default);

    /// <summary>取安装配置：与官方启动器共享的安装状态来源。</summary>
    Task<InstallationConfigResponse> GetInstallationConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>取指定版本与文件在补丁源里的清单地址。</summary>
    Task<ManifestUrlResponse> GetManifestUrlAsync(
        string version,
        string filePath,
        string patchUrlGroup,
        CancellationToken cancellationToken = default);

    /// <summary>取远程清单（绝对地址；地址的可信性由传输层的出站校验负责）。</summary>
    Task<RemoteManifest> GetRemoteManifestAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>把任意 CDN 地址还原成官方补丁源地址。</summary>
    string RestoreOfficialPackageUrl(string url);
}
