using System.Text.RegularExpressions;
using System.Xml.Linq;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

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
    private const string ProductionProjectsRoot = "src";

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
        Assert.Contains("global.json", generator, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet --list-runtimes", generator, StringComparison.Ordinal);
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
        // scripts/New-ThirdPartyNotices.ps1」会留下一份写着旧版本号的许可披露。这里比对每一个生产工程
        // 直接引用的包：只要某个工程（例如 Windows 自更新 helper）引用了表里没有的包，披露就不完整——
        // 测试专用的包（xunit、coverlet、Test.Sdk）不进发行档案，本来就不该出现在表里。
        var projects = ProductionProjects().ToArray();
        var referenced = projects
            .SelectMany(project => XDocument.Load(project)
                .Descendants("PackageReference")
                .Select(element => (string?)element.Attribute("Include"))
                .OfType<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var declared = ReadDeclaredPackageVersions();
        var notices = ReadNoticesTable();

        Assert.True(
            projects.Length >= 5,
            $"src 下只找到 {projects.Length} 个生产工程，定位方式多半已经失效。");
        Assert.True(
            referenced.Count >= 13,
            $"生产工程只解析出 {referenced.Count} 个包引用，读法多半已经失效。");
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

    [Fact]
    public void Notices_NameEveryProductionProjectTheyWereGeneratedFrom()
    {
        var notices = File.ReadAllText(TestRepository.FromRepositoryRoot(NoticesRelativePath));
        // 顶部清单列的是工程名（.csproj 名）：宿主就叫 Cafe.Launcher，其余工程带自己的层名，
        // 因此层名部分是可选的。
        var declared = Regex
            .Matches(notices, @"^- `(?<name>Cafe\.Launcher(?:\.[A-Za-z0-9_.]+)?)`$", RegexOptions.Multiline)
            .Select(match => match.Groups["name"].Value)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var onDisk = ProductionProjects()
            .Select(project => Path.GetFileNameWithoutExtension(project))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // 少一个工程就意味着它的依赖没进披露表：这正是「按生产工程生成」要防的漂移。
        Assert.Equal(onDisk, declared);
    }

    private static IEnumerable<string> ProductionProjects() =>
        Directory
            .EnumerateFiles(
                TestRepository.FromRepositoryRoot(ProductionProjectsRoot),
                "*.csproj",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal);
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
