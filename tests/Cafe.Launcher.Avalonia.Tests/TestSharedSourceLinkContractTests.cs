using System.Xml.Linq;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// <c>tests/Support</c>、<c>tests/TestDoubles</c> 与两个根设施是通过 <c>Compile-Link</c> 编进
/// 测试工程的共享源文件：它们不在任何工程的目录树里，于是「少登记一处」不会编译失败，只会让
/// 某个套件静默地少一个设施，直到有人写测试时才发现。本守卫把「谁链接了谁」变成断言。
/// </summary>
/// <remarks>
/// 三个不变量：
/// ① 共享目录里的每个 .cs 至少被一个测试工程链接（没有孤儿文件）；
/// ② <c>Support/</c> 与两个根设施被两个工程都链接（否则同一设施只有一半套件能用）；
/// ③ 无头工程链接的替身必须也被单元工程链接（两个套件的替身集合只允许差在「单元更多」）。
/// </remarks>
public sealed class TestSharedSourceLinkContractTests
{
    private const string UnitProject =
        "tests/Cafe.Launcher.Avalonia.Tests/Cafe.Launcher.Avalonia.Tests.csproj";

    private const string HeadlessProject =
        "tests/Cafe.Launcher.Avalonia.HeadlessTests/Cafe.Launcher.Avalonia.HeadlessTests.csproj";

    private const string SupportDirectory = "tests/Support";
    private const string DoublesDirectory = "tests/TestDoubles";

    private static readonly string[] SharedDirectories = [SupportDirectory, DoublesDirectory];

    private static readonly string[] SharedRootFacilities =
    [
        "tests/TestUserDataIsolation.cs",
        "tests/TestAnimationSetup.cs"
    ];

    [Fact]
    public void SharedSources_AreLinkedByAtLeastOneTestProject()
    {
        var linked = LinkedSources(UnitProject)
            .Concat(LinkedSources(HeadlessProject))
            .ToHashSet(PathComparer);

        Assert.True(linked.Count > 0, "两个测试工程都没有解析出 Compile-Link 条目，先修解析再谈共享设施。");

        foreach (var directory in SharedDirectories)
        {
            var files = Directory
                .EnumerateFiles(
                    TestRepository.FromRepositoryRoot(directory),
                    "*.cs",
                    SearchOption.AllDirectories)
                .ToArray();

            // 反空转：目录被搬走或改名时，下面的「没有孤儿」会因为空集合而变绿。
            Assert.True(
                files.Length > 0,
                $"{directory} 里一个 .cs 都没枚举到——目录改名了吗？");

            var orphans = files
                .Where(file => !linked.Contains(Path.GetFullPath(file)))
                .Select(file => Path.GetRelativePath(TestRepository.Root, file))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                orphans.Length == 0,
                $"{directory} 里有没被任何测试工程链接的文件。每个共享设施都要在两个工程的 "
                + "Compile-Link 段各登记一次；只有一套件用得上就说明它不属于这里："
                + string.Join(", ", orphans));
        }
    }

    [Fact]
    public void SharedFacilities_AreLinkedByBothTestProjects()
    {
        var unit = LinkedSources(UnitProject).ToHashSet(PathComparer);
        var headless = LinkedSources(HeadlessProject).ToHashSet(PathComparer);

        // 反空转：解析失败会让两组集合都为空，「两边都链接了」于是自动成立。
        Assert.True(unit.Count > 0, $"{UnitProject} 没有解析出 Compile-Link 条目。");
        Assert.True(headless.Count > 0, $"{HeadlessProject} 没有解析出 Compile-Link 条目。");

        var supportFiles = Directory
            .EnumerateFiles(
                TestRepository.FromRepositoryRoot(SupportDirectory),
                "*.cs",
                SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(TestRepository.Root, file))
            .ToArray();
        Assert.True(supportFiles.Length > 0, $"{SupportDirectory} 里一个 .cs 都没枚举到——目录改名了吗？");

        var required = SharedRootFacilities
            .Concat(supportFiles)
            .Select(path => Path.GetFullPath(TestRepository.FromRepositoryRoot(path)))
            .ToArray();

        var missing = required
            .Where(path => !unit.Contains(path) || !headless.Contains(path))
            .Select(path => Path.GetRelativePath(TestRepository.Root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "这些共享设施只被一个测试工程链接，另一个套件用不到它（两个工程都要登记）："
            + string.Join(", ", missing));
    }

    [Fact]
    public void HeadlessDoubles_AreASubsetOfTheUnitDoubles()
    {
        var unitDoubles = DoublesIn(LinkedSources(UnitProject));
        var headlessDoubles = DoublesIn(LinkedSources(HeadlessProject));

        Assert.True(
            unitDoubles.Count >= 13,
            $"单元工程只链接了 {unitDoubles.Count} 个替身，低于落地基线 13——先确认 Compile-Link 段仍能被解析。");
        Assert.True(
            headlessDoubles.Count >= 4,
            $"无头工程只链接了 {headlessDoubles.Count} 个替身，低于落地基线 4——先确认 Compile-Link 段仍能被解析。");

        var headlessOnly = headlessDoubles
            .Except(unitDoubles)
            .Select(path => Path.GetRelativePath(TestRepository.Root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            headlessOnly.Length == 0,
            "无头工程链接了单元工程没有的替身。两个套件的替身集合只允许差在「单元更多」："
            + "无头工程需要某个替身时，先在单元工程登记，再共享给无头："
            + string.Join(", ", headlessOnly));
    }

    private static HashSet<string> DoublesIn(IEnumerable<string> linked)
    {
        var doublesRoot = TestRepository.FromRepositoryRoot(DoublesDirectory) + Path.DirectorySeparatorChar;

        return linked
            .Where(path => path.StartsWith(doublesRoot, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(PathComparer);
    }

    /// <summary>
    /// 一个测试工程 <c>Compile-Link</c> 进来的全部源文件（绝对路径）。只看显式
    /// <c>Include</c>：隐式 glob 取不到工程目录之外的共享源。
    /// </summary>
    private static string[] LinkedSources(string projectRelativePath)
    {
        var projectPath = TestRepository.FromRepositoryRoot(projectRelativePath);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;

        return XDocument
            .Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "Compile")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(
                projectDirectory,
                include!.Replace('\\', Path.DirectorySeparatorChar))))
            .ToArray();
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
