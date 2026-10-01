namespace Cafe.Launcher.Core.Models;

/// <summary>
/// 一款 Yostar 游戏的全部随游戏变化的身份值：游戏标识、安装目录名、官方 API 与签名参数、
/// 客户端可执行名，以及只在该游戏语境下成立的链接。
/// </summary>
/// <remarks>
/// 这是「哪款游戏」的唯一出处。值只写在 <see cref="Constants.LauncherProfiles"/> 的档案里，
/// 消费方接收注入的实例，因此换游戏是换一个档案，而不是搜替换常量。
///
/// 边界：<b>启动器产品</b>的身份（产品名、发行仓库、汉化源与资源面板）不在这里，
/// 见 <see cref="LauncherProductProfile"/>。判断某个值该放哪一边的判据是「官方为另一款游戏
/// 重新构建时它会不会变」——变则属于游戏档案。
/// </remarks>
public sealed record YostarGameProfile
{
    /// <summary>官方 API 与状态文件里的游戏标识（官方 <c>game_tag</c> / <c>GAME_DIR_NAME</c>）。</summary>
    public required string Tag { get; init; }

    /// <summary>官方安装根的目录名。两款已知官方启动器都用 <c>YostarGames</c>。</summary>
    public required string RootFolderName { get; init; }

    /// <summary>安装根之下的游戏目录名。</summary>
    public required string GameFolderName { get; init; }

    /// <summary>游戏客户端本体的可执行文件名（不是官方配置里的宿主名）。</summary>
    public required string GameExecutableFileName { get; init; }

    /// <summary>已知官方启动宿主；本地配置损坏且离线卸载时，用于识别游戏与反作弊进程家族。</summary>
    public required string GameLauncherExecutableFileName { get; init; }

    /// <summary>游戏目录里官方分发自带的启动脚本名。</summary>
    public required string GameStartScriptFileName { get; init; }

    /// <summary>官方 launcher API 基址。不同游戏可能落在不同的官方域名上。</summary>
    public required string ApiBaseUrl { get; init; }

    /// <summary>官方 Authorization 签名盐。官方更换即协议失配，调用方须可降级。</summary>
    public required string AuthorizationSalt { get; init; }

    /// <summary>签名 <c>head.version</c> 回传的启动器版本；见 <see cref="Constants.LauncherProfiles"/>。</summary>
    public required string AuthorizationVersion { get; init; }

    /// <summary>官方安装包主机（清单与游戏文件的官方下载域）。</summary>
    public required string OfficialPackageHost { get; init; }

    /// <summary>
    /// 官方配置下发的包内相对路径前缀；命中时补上 <see cref="OfficialPackageBaseUrl"/>
    /// 才能得到可直接取回的绝对地址。
    /// </summary>
    public required string PackageAssetPrefix { get; init; }

    /// <summary>
    /// 稳定的运行时身份，用于 UMU <c>GAMEID</c> 与本启动器托管的兼容前缀布局。
    /// 刻意与可执行文件名解耦：改名不应让既有兼容状态成为孤儿。
    /// </summary>
    public required string RuntimeId { get; init; }

    /// <summary>游戏官网。</summary>
    public required string OfficialWebsiteUrl { get; init; }

    /// <summary>
    /// 游戏客户端 Cookie 库在用户配置目录下的相对路径（BestHTTP 的实现细节，供资源面板读取 UID）。
    /// </summary>
    public required string[] CookieLibraryRelativeSegments { get; init; }

    /// <summary>官方包基址；由 <see cref="OfficialPackageHost"/> 派生，不单独登记。</summary>
    public string OfficialPackageBaseUrl => "https://" + OfficialPackageHost;
}
