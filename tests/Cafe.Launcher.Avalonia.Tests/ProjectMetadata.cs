using System.Text.RegularExpressions;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 发布元数据的唯一读取点：<c>VersionPrefix</c> 是版本号在这份仓库里的声明处，
/// 发布说明的标题（<c>ReleaseChangelogContractTests</c>）与发布横幅的文件名
/// （<c>ReleaseBannerContractTests</c>）都从它派生。两处此前各有一份同形的正则读取，
/// 2026-09-17（AUD-MAINT-006）收敛到这一处，避免两条契约对「当前版本是什么」有两种说法。
/// </summary>
internal static class ProjectMetadata
{
    /// <summary>Reads the declared project version (the csproj <c>VersionPrefix</c>).</summary>
    internal static string ReadVersionPrefix()
    {
        var project = File.ReadAllText(
            TestRepository.FromRepositoryRoot("src/Cafe.Launcher.Avalonia/Cafe.Launcher.Avalonia.csproj"));
        var match = Regex.Match(project, "<VersionPrefix>([^<]+)</VersionPrefix>");

        Assert.True(match.Success, "Cafe.Launcher.Avalonia.csproj must declare <VersionPrefix>.");
        return match.Groups[1].Value;
    }
}
