using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Constants;

/// <summary>
/// 本仓库声明的启动器产品与游戏档案。**这是这些值的唯一出处**：产品身份、游戏身份、
/// 官方协议参数都只在这里写一次，其余代码接收注入的实例或按名引用这里的实例。
/// </summary>
/// <remarks>
/// 为什么要这一层：这些值原先散在 <c>ApiConfig</c>、<c>GamePaths</c>、<c>LauncherConstants</c>
/// 三个静态类里，且两类关注点混住——<c>ApiConfig</c> 同时放官方 API 与 Cafe 自己的发行仓库，
/// <c>GamePaths</c> 同时放游戏目录名与 <c>settings.json</c>。混住的代价不是「多游戏才暴露」，
/// 而是任何一次改值都要先判断它属于谁；档案把判断结果固化下来。
///
/// 换一款游戏 = 在这里加一个 <see cref="YostarGameProfile"/>，由组合根登记它；
/// 消费方不需要知道有第二个档案存在。
/// </remarks>
public static class LauncherProfiles
{
    /// <summary>本项目的产品档案（Cafe Launcher）。</summary>
    public static LauncherProductProfile Cafe { get; } = new()
    {
        ProductName = "Cafe Launcher",
        GitHubReleaseRepositorySlug = "bluearchive-cafe/Cafe.Launcher.Avalonia",
        CafePackageHost = "launcher-pkg-ba-jp.bluearchive.cafe",
        LauncherApiBaseUrl = "https://api-cafe-launcher.saibamidori.com/",
        LauncherReleasesPath = "/api/v2/launcher/releases",
        ResourcePanelApiBaseUrl = "https://api.bluearchive.cafe",
        CafeWebsiteUrl = "https://bluearchive.cafe/",
        HelpDocsUrl = "https://docs.bluearchive.cafe/cafe-launcher/",
        PrivacyPolicyUrl =
            "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/blob/main/PRIVACY.md",
        IssueTrackerUrl = "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/issues",
        DefaultBackgroundArtworkUrl = "https://www.pixiv.net/artworks/142932674"
    };

    /// <summary>
    /// Blue Archive 日服的官方参数。
    /// </summary>
    /// <remarks>
    /// <see cref="YostarGameProfile.AuthorizationVersion"/> 是签名 <c>head.version</c> 回传的
    /// **官方启动器版本**（官方填的是自己运行的版本），协议兼容性是对着官方 1.7.2 核对过的。
    /// 该值属于签名载荷，不是装饰字段：官方更新后必须重新核对，并把服务端拒绝当作
    /// 「该字段开始被校验」的信号。<c>LauncherConstantsTests</c> 钉住这个字面量。
    /// </remarks>
    public static YostarGameProfile BlueArchiveJapan { get; } = new()
    {
        Tag = "BlueArchive_JP",
        RootFolderName = "YostarGames",
        GameFolderName = "BlueArchive_JP",
        GameExecutableFileName = "BlueArchive.exe",
        GameStartScriptFileName = "run.bat",
        ApiBaseUrl = "https://api-launcher-jp.yo-star.com",
        AuthorizationSalt = "DE7108E9B2842FD460F4777702727869",
        AuthorizationVersion = "1.7.2",
        OfficialPackageHost = "launcher-pkg-ba-jp.yo-star.com",
        PackageAssetPrefix = "/prod/BlueArchive_JP/launcher_background_img/",
        RuntimeId = "blue-archive-jp",
        OfficialWebsiteUrl = "https://bluearchive.jp/",
        CookieLibraryRelativeSegments =
            ["AppData", "LocalLow", "YostarJP", "BlueArchive", "Cookies", "Library"]
    };
}
