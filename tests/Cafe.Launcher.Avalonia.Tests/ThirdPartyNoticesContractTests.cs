using System.Text.RegularExpressions;
using System.Xml.Linq;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// The self-contained archives redistribute the .NET runtime alongside the NuGet packages, so the
/// license disclosure has to name it and has to travel with the binaries. Both halves are asserted
/// here because either can be dropped silently: the generator's header is hand-written text, and
/// the packaging copy is one statement in a long script. The third half is the table itself: it is
/// rewritten wholesale by the generator, so an upgrade that forgets to re-run it leaves the
/// disclosure naming versions that never shipped.
/// </summary>
public sealed class ThirdPartyNoticesContractTests
{
    private const string NoticesRelativePath = "THIRD-PARTY-NOTICES.md";
    private const string PackagesPropsRelativePath = "Directory.Packages.props";
    private const string AppProjectRelativePath = "src/Cafe.Launcher.Avalonia/Cafe.Launcher.Avalonia.csproj";

    private static readonly Regex NoticesRow = new(
        @"^\|\s*(?<name>[^|]+?)\s*\|\s*(?<version>[^|]+?)\s*\|",
        RegexOptions.Compiled | RegexOptions.Multiline);

    [Fact]
    public void Notices_DeclareTheRedistributedSelfContainedRuntime()
    {
        var notices = File.ReadAllText(TestRepository.FromRepositoryRoot("THIRD-PARTY-NOTICES.md"));

        Assert.Contains("## Self-contained .NET runtime", notices, StringComparison.Ordinal);
        Assert.Contains("Microsoft.NETCore.App", notices, StringComparison.Ordinal);
        Assert.Contains("MIT-licensed", notices, StringComparison.Ordinal);
    }

    [Fact]
    public void NoticesGenerator_EmitsTheRuntimeSectionOnRegeneration()
    {
        var generator = File.ReadAllText(TestRepository.FromRepositoryRoot("scripts/New-ThirdPartyNotices.ps1"));

        Assert.Contains("## Self-contained .NET runtime", generator, StringComparison.Ordinal);
        Assert.Contains("dotnet --list-runtimes", generator, StringComparison.Ordinal);
    }

    [Fact]
    public void DistributionScript_ShipsTheLicenseDisclosureWithEveryArchive()
    {
        var script = File.ReadAllText(TestRepository.FromRepositoryRoot("scripts/Build-Distribution.ps1"));

        // Every packaging step below the publish loop copies the publish directory, so placing the
        // two files there is what puts them inside the zip, .app, tar.gz, deb, rpm and AppImage.
        Assert.Contains("\"LICENSE\", \"THIRD-PARTY-NOTICES.md\"", script, StringComparison.Ordinal);
        Assert.Contains("Copy-Item -LiteralPath (Join-Path $RootDir $noticeFile)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Notices_PackageVersionsMatchThePackagesTheAppShips()
    {
        // 生成器按 NuGet 解析出的依赖图重写整张表，所以「升了版本、忘了重跑
        // scripts/New-ThirdPartyNotices.ps1」会留下一份写着旧版本号的许可披露——AGENTS.md 自己
        // 也记着这一点没有守卫（`.repository-audit` 的 AUD-MAINT 系列）。只比对应用工程真的分发
        // 的包：测试专用的包（xunit、coverlet、Test.Sdk）不进发行档案，本来就不该出现在表里。
        var referenced = XDocument.Load(TestRepository.FromRepositoryRoot(AppProjectRelativePath))
            .Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .OfType<string>()
            .ToList();
        var declared = ReadDeclaredPackageVersions();
        var notices = ReadNoticesTable();

        Assert.True(
            referenced.Count >= 13,
            $"应用工程只解析出 {referenced.Count} 个包引用，读法多半已经失效。");
        Assert.True(
            notices.Count >= 40,
            $"{NoticesRelativePath} 只解析出 {notices.Count} 行，表格格式或过滤条件已经对不上。");

        var drift = new List<string>();
        foreach (var name in referenced)
        {
            if (!declared.TryGetValue(name, out var declaredVersion))
            {
                drift.Add($"{name} 没有在 {PackagesPropsRelativePath} 里登记版本");
                continue;
            }

            if (!notices.TryGetValue(name, out var noticeVersion))
            {
                drift.Add($"{name} 整行缺失（声明的版本是 {declaredVersion}）");
                continue;
            }

            if (!string.Equals(noticeVersion, declaredVersion, StringComparison.Ordinal))
            {
                drift.Add($"{name} 写的是 {noticeVersion}，声明的是 {declaredVersion}");
            }
        }

        Assert.True(
            drift.Count == 0,
            $"许可披露与应用实际分发的版本不一致——改过依赖后要重跑 scripts/New-ThirdPartyNotices.ps1 并提交：{string.Join("；", drift)}。");
    }

    private static Dictionary<string, string> ReadDeclaredPackageVersions() =>
        XDocument.Load(TestRepository.FromRepositoryRoot(PackagesPropsRelativePath))
            .Descendants("PackageVersion")
            .Select(element => (
                Name: (string?)element.Attribute("Include"),
                Version: (string?)element.Attribute("Version")))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name) && !string.IsNullOrWhiteSpace(entry.Version))
            .ToDictionary(entry => entry.Name!, entry => entry.Version!, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> ReadNoticesTable()
    {
        var notices = File.ReadAllText(TestRepository.FromRepositoryRoot(NoticesRelativePath));

        var rows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in NoticesRow.Matches(notices))
        {
            var name = match.Groups["name"].Value;
            var version = match.Groups["version"].Value;

            // 表头与分隔行也长得像数据行，按内容排除而不是按下标排除。
            if (name == "Package" || version.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            rows[name] = version;
        }

        return rows;
    }
}
