using System.Text.RegularExpressions;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Guards the release-notes contract in AGENTS.md: CHANGELOG_RELEASE.md is a single-release
/// document written for the person installing the launcher, so internal engineering
/// vocabulary must never reach it — except inside the folded technical appendix at the end
/// of the file, whose contents are exempt (see AGENTS.md, Release Notes).
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

        var expected = new[] { $"## v{ProjectMetadata.ReadVersionPrefix()}" };

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
        var userFacingNotes = RemoveFoldedTechnicalAppendix(ReadChangelog());

        foreach (var term in InternalTerminology)
        {
            Assert.False(
                userFacingNotes.Contains(term, StringComparison.OrdinalIgnoreCase),
                $"CHANGELOG_RELEASE.md is user-facing: the internal term '{term}' must not appear. See AGENTS.md (Release Notes).");
        }
    }

    /// <summary>
    /// The exemption above is only safe while the appendix stays folded, sits after the
    /// user-facing notes, and cannot swallow them.
    /// </summary>
    [Fact]
    public void Changelog_TechnicalAppendix_IsOneFoldedBlockAtTheEnd()
    {
        var changelog = ReadChangelog();

        Assert.Equal(1, CountOccurrences(changelog, TechnicalAppendixOpen));
        Assert.Equal(1, CountOccurrences(changelog, TechnicalAppendixClose));

        var openIndex = changelog.IndexOf(TechnicalAppendixOpen, StringComparison.Ordinal);
        var closeIndex = changelog.IndexOf(TechnicalAppendixClose, StringComparison.Ordinal);
        var warningIndex = changelog.IndexOf("> [!WARNING]", StringComparison.Ordinal);

        Assert.True(openIndex > warningIndex, "The folded technical appendix must follow the user-facing notes.");
        Assert.True(
            string.IsNullOrWhiteSpace(changelog[(closeIndex + TechnicalAppendixClose.Length)..]),
            "The folded technical appendix must end the file.");

        var userFacingNotes = changelog[..openIndex];
        Assert.Contains("> [!NOTE]", userFacingNotes, StringComparison.Ordinal);
        Assert.Contains("> [!WARNING]", userFacingNotes, StringComparison.Ordinal);
        Assert.True(
            userFacingNotes.Length >= 1000,
            $"The user-facing notes came out suspiciously short ({userFacingNotes.Length} characters).");
        Assert.True(
            closeIndex - openIndex >= 200,
            "The folded technical appendix is too small to justify a vocabulary exemption.");
    }

    private const string TechnicalAppendixOpen = "<details>";
    private const string TechnicalAppendixClose = "</details>";

    private static string RemoveFoldedTechnicalAppendix(string changelog)
    {
        var openIndex = changelog.LastIndexOf(TechnicalAppendixOpen, StringComparison.Ordinal);
        if (openIndex < 0)
        {
            return changelog;
        }

        var closeIndex = changelog.IndexOf(TechnicalAppendixClose, openIndex, StringComparison.Ordinal);
        if (closeIndex < 0)
        {
            return changelog;
        }

        return string.Concat(
            changelog.AsSpan(0, openIndex),
            changelog.AsSpan(closeIndex + TechnicalAppendixClose.Length));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string ReadChangelog() => File.ReadAllText(TestRepository.FromRepositoryRoot("CHANGELOG_RELEASE.md"));

}
