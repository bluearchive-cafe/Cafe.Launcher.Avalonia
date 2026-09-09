using System.Text.RegularExpressions;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Guards the release-notes contract in AGENTS.md: CHANGELOG_RELEASE.md is a single-release
/// document written for the person installing the launcher, so internal engineering
/// vocabulary must never reach it.
/// </summary>
public sealed class ReleaseChangelogContractTests
{
    /// <summary>
    /// Vocabulary that only reads as meaningful to a contributor. Grouped by why it is
    /// banned, so a new term can be judged against the same rule.
    /// </summary>
    private static readonly string[] InternalTerminology = new[]
    {
        // 审计与工程流程
        "审计",
        "台账",
        "findings",
        "覆盖率",
        "工程质量",
        // 测试内部
        "单元测试",
        "集成测试",
        "测试守卫",
        "回归测试",
        "xUnit",
        "Headless",
        "WaitAsync",
        // 架构与实现
        "模态",
        "AppDomain",
        "Dispatcher",
        "依赖注入",
        "构造期",
        "调用点",
        "重构",
        // 构建流水线
        "workflow",
        "GitHub Actions",
    };

    [Fact]
    public void Changelog_HasSingleVersionHeading_MatchingProjectVersion()
    {
        var headings = Regex.Matches(ReadChangelog(), @"(?m)^## v[^\r\n]+")
            .Select(match => match.Value)
            .ToArray();

        var expected = new[] { $"## v{ReadProjectVersion()}" };

        Assert.Equal(expected, headings);
    }

    [Fact]
    public void Changelog_KeepsReleaseNoticeBlocks()
    {
        var changelog = ReadChangelog();

        Assert.Contains("> [!NOTE]", changelog, StringComparison.Ordinal);
        Assert.Contains("> [!WARNING]", changelog, StringComparison.Ordinal);
    }

    [Fact]
    public void Changelog_UsesUserFacingTerminologyOnly()
    {
        var changelog = ReadChangelog();

        foreach (var term in InternalTerminology)
        {
            Assert.False(
                changelog.Contains(term, StringComparison.OrdinalIgnoreCase),
                $"CHANGELOG_RELEASE.md is user-facing: the internal term '{term}' must not appear. See AGENTS.md (Release Notes).");
        }
    }

    private static string ReadChangelog() => File.ReadAllText(ProjectFile("CHANGELOG_RELEASE.md"));

    private static string ReadProjectVersion()
    {
        var project = File.ReadAllText(ProjectFile("src/Cafe.Launcher.Avalonia/Cafe.Launcher.Avalonia.csproj"));
        var match = Regex.Match(project, "<VersionPrefix>([^<]+)</VersionPrefix>");

        Assert.True(match.Success, "Cafe.Launcher.Avalonia.csproj must declare <VersionPrefix>.");
        return match.Groups[1].Value;
    }

    private static string ProjectFile(string relativePath) =>
        Path.Combine(TestLocalizationHelper.FindRepositoryRoot(), relativePath);
}
