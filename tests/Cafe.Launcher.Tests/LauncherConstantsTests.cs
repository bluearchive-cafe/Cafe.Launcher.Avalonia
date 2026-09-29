using System.Reflection;
using Cafe.Launcher.Constants;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Tests;

public sealed class LauncherConstantsTests
{
    [Fact]
    public void LauncherVersion_UsesApplicationSemVer()
    {
        // 版本来自 WinExe 宿主程序集，不是 BuildInfo 所在的类型碰巧属于哪个程序集：
        // LauncherConstants 已随 Core 拆分离开宿主，用它的程序集会把 Core 默认的
        // 1.0.0 当成产品版本（见 AssemblySplitContractTests.BuildIdentity_ReadsTheSuppliedHostAssembly）。
        var expected = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        Assert.NotNull(expected);
        Assert.Equal(expected, BuildInfo.LauncherVersion);
    }

    [Fact]
    public void CommitSha_UsesSevenCharacterLowercaseGitHash()
    {
        Assert.Matches("^[0-9a-f]{7}$", BuildInfo.CommitSha);
    }

    [Fact]
    public void BuildTime_IsAParsedTimestampOrEmpty()
    {
        // 宿主 csproj 的 git 元数据 Exec 曾把单个百分号交给 MSBuild 吃掉，git 的致命错误文本
        // 被当成构建时间写进 AssemblyMetadata，再由 LauncherBuildIdentity 原样透出到关于页与日志。
        // git 不可用时（源码包构建）允许空串。
        Assert.DoesNotContain("fatal", BuildInfo.BuildTime, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            BuildInfo.BuildTime.Length == 0
            || DateTimeOffset.TryParse(
                BuildInfo.BuildTime,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out _),
            $"BuildTime 必须是可解析的时间戳或空串，实际为 '{BuildInfo.BuildTime}'。");
    }

    [Fact]
    public void YostarAuthorizationVersion_RemainsTheValueTheProtocolWasVerifiedAgainst()
    {
        // 该常量是签名 head 里的 head.version（官方填的是自己运行的版本），协议兼容性是对着
        // 官方启动器 1.7.2 核对过的。本用例只钉住这个字面量，并不证明它与官方当前产物一致——
        // 数值一旦变动必须先复核官方产物版本；算法层面的守卫见
        // AuthorizationHeaderFactoryTests（字段序、签名拼装、版本变更对签名的影响）。
        Assert.Equal("1.7.2", LauncherProfiles.BlueArchiveJapan.AuthorizationVersion);
    }

    [Fact]
    public void LauncherUpdateEndpoints_UseTheApplicationRepository()
    {
        Assert.Equal("bluearchive-cafe/Cafe.Launcher.Avalonia", LauncherProfiles.Cafe.GitHubReleaseRepositorySlug);
        Assert.Equal("/api/v2/launcher/releases", LauncherProfiles.Cafe.LauncherReleasesPath);
        Assert.Equal(
            "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases",
            LauncherProfiles.Cafe.GitHubReleasesPageUrl);
        Assert.Equal(
            "https://api.github.com/repos/bluearchive-cafe/Cafe.Launcher.Avalonia/releases",
            LauncherProfiles.Cafe.GitHubReleasesApiUrl);
    }
}
