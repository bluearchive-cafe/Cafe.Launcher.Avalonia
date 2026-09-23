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
}
