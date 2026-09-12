using System.Reflection;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LauncherConstantsTests
{
    [Fact]
    public void LauncherVersion_UsesApplicationSemVer()
    {
        var expected = typeof(LauncherConstants).Assembly
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
    public void YostarAuthorizationVersion_RemainsTheValueTheProtocolWasVerifiedAgainst()
    {
        // 该常量是签名 head 里的 head.version（官方填的是自己运行的版本），协议兼容性是对着
        // 官方启动器 1.7.2 核对过的。本用例只钉住这个字面量，并不证明它与官方当前产物一致——
        // 数值一旦变动必须先复核官方产物版本；算法层面的守卫见
        // AuthorizationHeaderFactoryTests（字段序、签名拼装、版本变更对签名的影响）。
        Assert.Equal("1.7.2", ApiConfig.YostarAuthorizationVersion);
    }
}
