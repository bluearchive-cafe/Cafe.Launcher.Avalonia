namespace Cafe.Launcher.Core.Models;

/// <summary>
/// 一个启动器<b>产品</b>的身份值：产品名（决定数据根）、发行仓库与发布通道、
/// 自有服务地址，以及对外链接。
/// </summary>
/// <remarks>
/// 与 <see cref="YostarGameProfile"/> 的分界是「官方为另一款游戏重新构建时它会不会变」：
/// 发行通道、汉化源、资源面板与官方网站跟随的是<b>谁在发行这个启动器</b>，不是哪款游戏。
/// 当前项目里两者一一对应，但它们是两个可独立替换的档案。
/// </remarks>
public sealed record LauncherProductProfile
{
    /// <summary>产品名。数据根目录名（<c>%LOCALAPPDATA%\&lt;产品名&gt;</c>）由它派生。</summary>
    public required string ProductName { get; init; }

    /// <summary>发行仓库 slug（<c>owner/repo</c>）。发行说明页与 API 地址都由它派生。</summary>
    public required string GitHubReleaseRepositorySlug { get; init; }

    /// <summary>本产品自有的汉化/镜像包主机。与游戏相关，但归属产品方所有。</summary>
    public required string CafePackageHost { get; init; }

    /// <summary>本产品自有服务端基址（发行元数据代理）。</summary>
    public required string LauncherApiBaseUrl { get; init; }

    /// <summary>本产品服务端的发行元数据路径。</summary>
    public required string LauncherReleasesPath { get; init; }

    /// <summary>资源面板服务基址。</summary>
    public required string ResourcePanelApiBaseUrl { get; init; }

    /// <summary>产品站点。</summary>
    public required string CafeWebsiteUrl { get; init; }

    /// <summary>产品帮助文档。</summary>
    public required string HelpDocsUrl { get; init; }

    /// <summary>隐私政策。</summary>
    public required string PrivacyPolicyUrl { get; init; }

    /// <summary>问题反馈入口。</summary>
    public required string IssueTrackerUrl { get; init; }

    /// <summary>默认壁纸的原始作品页。</summary>
    public required string DefaultBackgroundArtworkUrl { get; init; }

    /// <summary>发行仓库地址；由 <see cref="GitHubReleaseRepositorySlug"/> 派生。</summary>
    public string GitHubReleaseRepositoryUrl => "https://github.com/" + GitHubReleaseRepositorySlug;

    /// <summary>发行资产的下载路径前缀；由 <see cref="GitHubReleaseRepositorySlug"/> 派生。</summary>
    public string GitHubReleaseDownloadPathPrefix => "/" + GitHubReleaseRepositorySlug + "/releases/download/";

    /// <summary>GitHub 发行列表 API；由 <see cref="GitHubReleaseRepositorySlug"/> 派生。</summary>
    public string GitHubReleasesApiUrl =>
        "https://api.github.com/repos/" + GitHubReleaseRepositorySlug + "/releases";

    /// <summary>按标签查询发行版的 API 前缀；由 <see cref="GitHubReleasesApiUrl"/> 派生。</summary>
    public string GitHubReleaseByTagApiUrl => GitHubReleasesApiUrl + "/tags/";

    /// <summary>发行列表页（浏览器接管的落点，列表页永远存在，标签页不一定）。</summary>
    public string GitHubReleasesPageUrl => GitHubReleaseRepositoryUrl + "/releases";
}
