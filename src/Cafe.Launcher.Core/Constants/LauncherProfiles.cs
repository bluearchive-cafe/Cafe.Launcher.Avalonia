using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
        LauncherApiBaseUrl = "https://api.cafe-launcher.saibamidori.com/",
        LauncherReleasesPath = "/api/v2/launcher/releases",
        LauncherDownloadMirrorBaseUrl = "https://download.cafe-launcher.saibamidori.com/",
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
        GameLauncherExecutableFileName = "xldr_BlueArchiveOnline_JP_loader_x64.exe",
        GameStartScriptFileName = "run.bat",
        ApiBaseUrl = "https://api-launcher-jp.yo-star.com",
        AuthorizationSalt = "DE7108E9B2842FD460F4777702727869",
        AuthorizationVersion = "1.7.2",
        OfficialPackageHost = "launcher-pkg-ba-jp.yo-star.com",
        PackageAssetPrefix = "/prod/BlueArchive_JP/launcher_background_img/",
        RuntimeId = "blue-archive-jp",
        OfficialWebsiteUrl = "https://bluearchive.jp/",
        CookieLibraryRelativeSegments =
            ["AppData", "LocalLow", "YostarJP", "BlueArchive", "Cookies", "Library"],
        DeviceOptionRelativeSegments =
            ["AppData", "LocalLow", "YostarJP", "BlueArchive", "DeviceOption"],
        TitleVideoAudioRelativeSegments =
            ["AppData", "LocalLow", "YostarJP", "BlueArchive", "TitleVideoAudioSaveData"]
    };

    /// <summary>StellaSora 国服的已核对官方协议与入口；真实进程家族仍需实机验收。</summary>
    public static YostarGameProfile StellaSoraChina { get; } = new()
    {
        Tag = "StellaSora_CN",
        RootFolderName = "YostarGames",
        GameFolderName = "StellaSora_CN",
        GameExecutableFileName = "xtlr.exe",
        GameLauncherExecutableFileName = "xtlr.exe",
        GameStartScriptFileName = "",
        ApiBaseUrl = "https://launcher-api.yostar.net",
        AuthorizationSalt = "872550AD59A235662C5B7D5F88CEBE4B",
        AuthorizationVersion = "1.3.0",
        GameConfigIncludesParameters = false,
        OfficialPackageHost = "game-launcher-ss-cn.yostar.net",
        PackageAssetPrefix = "",
        RuntimeId = "stella-sora-cn",
        OfficialWebsiteUrl = "https://stellasora.yostar.cn/",
        CookieLibraryRelativeSegments = [],
        DeviceOptionRelativeSegments = [],
        TitleVideoAudioRelativeSegments = []
    };

    /// <summary>随程序发布的游戏和能力清单；页面选择不能修改声明或扩展支持范围。</summary>
    public static IReadOnlyList<LauncherGameDefinition> GameDefinitions { get; } = Array.AsReadOnly(
        new[]
        {
            new LauncherGameDefinition(BlueArchiveJapan, SupportsCafeDownloadSource: true, SupportsResourcePanel: true),
            new LauncherGameDefinition(StellaSoraChina, SupportsCafeDownloadSource: false, SupportsResourcePanel: false)
        });

    /// <summary>所有支持的官方档案；身份查找始终使用稳定 RuntimeId。</summary>
    public static IReadOnlyList<YostarGameProfile> SupportedGames { get; } = Array.AsReadOnly(
        new[] { BlueArchiveJapan, StellaSoraChina });

    /// <summary>查找明确游戏；未知、区服 tag 或显示名称都不回退为默认游戏。</summary>
    public static YostarGameProfile GetSupportedGame(string gameId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        return TryGetSupportedGame(gameId, out var profile)
            ? profile
            : throw new ArgumentException("Unsupported game identity.", nameof(gameId));
    }

    /// <summary>按区分大小写的稳定身份查找内置档案；不修改任何选择状态。</summary>
    public static bool TryGetSupportedGame(string? gameId, [NotNullWhen(true)] out YostarGameProfile? profile)
    {
        foreach (var candidate in SupportedGames)
        {
            if (string.Equals(candidate.RuntimeId, gameId, StringComparison.Ordinal))
            {
                profile = candidate;
                return true;
            }
        }

        profile = null;
        return false;
    }

    /// <summary>读取已登记游戏的能力；产品服务存在不表示所有条目均支持该服务。</summary>
    public static LauncherGameDefinition GetGameDefinition(string gameId)
    {
        var profile = GetSupportedGame(gameId);
        foreach (var definition in GameDefinitions)
        {
            if (ReferenceEquals(definition.Profile, profile))
            {
                return definition;
            }
        }

        throw new InvalidOperationException("Supported game has no capability declaration.");
    }
}
