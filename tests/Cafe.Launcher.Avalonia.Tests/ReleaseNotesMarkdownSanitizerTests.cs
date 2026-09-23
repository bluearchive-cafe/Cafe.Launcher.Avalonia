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
    [InlineData("NOTE", "ℹ️ Note")]
    [InlineData("TIP", "💡 Tip")]
    [InlineData("IMPORTANT", "❗ Important")]
    [InlineData("WARNING", "⚠️ Warning")]
    [InlineData("CAUTION", "🚫 Caution")]
    public void Sanitize_GitHubAlertHeader_BecomesBoldBlockquoteTitle(string marker, string label)
    {
        var sanitized = ReleaseNotesMarkdownSanitizer.Sanitize("> [!" + marker + "]\n> 正文");

        Assert.Equal("> **" + label + "**\n> 正文", sanitized);
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
    public void Sanitize_TaskListCheckboxes_BecomeGlyphMarkers()
    {
        var sanitized = ReleaseNotesMarkdownSanitizer.Sanitize(
            "- [ ] 未完成\n- [x] 已完成\n1. [X] 有序已完成");

        Assert.Equal("- ☐ 未完成\n- ☑ 已完成\n1. ☑ 有序已完成", sanitized);
    }

    [Fact]
    public void Sanitize_ListItemWithPlainBrackets_IsUntouched()
    {
        const string markdown = "- [普通方括号] 不是任务列表";

        Assert.Equal(markdown, ReleaseNotesMarkdownSanitizer.Sanitize(markdown));
    }
}
