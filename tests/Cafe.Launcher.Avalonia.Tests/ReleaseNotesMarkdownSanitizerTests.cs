using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ReleaseNotesMarkdownSanitizerTests
{
    [Fact]
    public void Sanitize_WithImagesAndLinks_PreservesLabelsWithoutTargets()
    {
        const string markdown = "![Banner](https://example.com/banner.png) [Details](file:///danger)";

        var sanitized = ReleaseNotesMarkdownSanitizer.Sanitize(markdown);

        Assert.Equal("Banner Details", sanitized);
    }

    [Fact]
    public void Sanitize_WithFormatting_PreservesMarkdown()
    {
        const string markdown = "## Highlights\n\n- **Faster** updates";

        Assert.Equal(markdown, ReleaseNotesMarkdownSanitizer.Sanitize(markdown));
    }

    [Theory]
    [InlineData("NOTE")]
    [InlineData("TIP")]
    [InlineData("IMPORTANT")]
    [InlineData("WARNING")]
    [InlineData("CAUTION")]
    public void Sanitize_GitHubAlert_IsPreservedForMarkdig(string marker)
    {
        var markdown = "> [!" + marker + "]\n> 正文";

        Assert.Equal(markdown, ReleaseNotesMarkdownSanitizer.Sanitize(markdown));
    }

    [Fact]
    public void Sanitize_AlertMarkerWithTrailingText_IsLeftAlone()
    {
        const string markdown = "> [!NOTE] 行内提及不构成警示块";

        Assert.Equal(markdown, ReleaseNotesMarkdownSanitizer.Sanitize(markdown));
    }

    [Fact]
    public void Sanitize_LowercaseAlertMarker_IsNotAnAlertOnGitHub()
    {
        const string markdown = "> [!note]\n> GitHub 仅认大写标记";

        Assert.Equal(markdown, ReleaseNotesMarkdownSanitizer.Sanitize(markdown));
    }

    [Fact]
    public void Sanitize_TaskListCheckboxes_ArePreservedForMarkdig()
    {
        var sanitized = ReleaseNotesMarkdownSanitizer.Sanitize(
            "- [ ] 未完成\n- [x] 已完成\n1. [X] 有序已完成");

        Assert.Equal("- [ ] 未完成\n- [x] 已完成\n1. [X] 有序已完成", sanitized);
    }

    [Fact]
    public void Sanitize_ListItemWithPlainBrackets_IsUntouched()
    {
        const string markdown = "- [普通方括号] 不是任务列表";

        Assert.Equal(markdown, ReleaseNotesMarkdownSanitizer.Sanitize(markdown));
    }

    [Fact]
    public void Sanitize_GfmSyntaxInsideCode_IsNotRewritten()
    {
        const string markdown =
            "`- [x] [inline](https://example.com)`\n\n```markdown\n> [!NOTE]\n![image](https://example.com/image.png)\n```";

        Assert.Equal(markdown, ReleaseNotesMarkdownSanitizer.Sanitize(markdown));
    }

    [Fact]
    public void Sanitize_TaskListInsideBlockQuote_IsPreservedForMarkdig()
    {
        const string markdown = "> - [ ] 未完成\n> - [X] 已完成";

        Assert.Equal(markdown, ReleaseNotesMarkdownSanitizer.Sanitize(markdown));
    }

    [Fact]
    public void Sanitize_LinkWithNestedLabelAndDestination_PreservesCompleteLabel()
    {
        const string markdown = "[the [nested] label](https://example.com/a_(b))";

        Assert.Equal("the [nested] label", ReleaseNotesMarkdownSanitizer.Sanitize(markdown));
    }
}
