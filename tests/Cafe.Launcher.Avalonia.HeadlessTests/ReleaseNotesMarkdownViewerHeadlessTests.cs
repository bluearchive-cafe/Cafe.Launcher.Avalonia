using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Cafe.Launcher.Avalonia.Controls;
using Cafe.Launcher.Avalonia.Testing;
using Xunit;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed class ReleaseNotesMarkdownViewerHeadlessTests
{
    [AvaloniaFact]
    public void Markdown_WhenUsingGfmAndAlerts_RendersNativeStructuresWithoutRemoteImages()
    {
        var viewer = new ReleaseNotesMarkdownViewer
        {
            Markdown = "> [!WARNING]\n> 正文\n\n- [x] 完成\n\n![远程图片](https://example.com/image.png)"
        };
        var window = new Window { Content = viewer };

        window.Show();

        var renderedContent = Assert.IsAssignableFrom<Control>(viewer.Content);
        var descendants = EnumerateControls(renderedContent).ToArray();
        Assert.Contains(descendants.OfType<Border>(), border => border.Classes.Contains("markdown-alert-warning"));
        Assert.Contains(descendants.OfType<TextBlock>(), text => text.Classes.Contains("markdown-alert-header"));
        Assert.Contains(
            descendants.OfType<TextBlock>(),
            text => text.Classes.Contains("markdown-task-list") && text.Text == "\u2611");
        Assert.Empty(descendants.OfType<Image>());

        window.Close();
    }

    /// <summary>
    /// 折叠技术附录靠 HTML 块实现，而 MarkView 会静默忽略 HTML 块：这里钉住两件事——
    /// 块内条目在应用内照常可见（发布说明对话框显示的是同一段文本），且不出现裸标签。
    /// </summary>
    [AvaloniaFact]
    public void Markdown_WithTechnicalAppendix_ShowsEntriesWithoutRawHtmlTags()
    {
        var changelog = File.ReadAllText(TestRepository.FromRepositoryRoot("CHANGELOG_RELEASE.md"));
        var openIndex = changelog.IndexOf("<details>", StringComparison.Ordinal);
        var closeIndex = changelog.IndexOf("</details>", openIndex, StringComparison.Ordinal);
        Assert.True(openIndex >= 0 && closeIndex > openIndex, "CHANGELOG_RELEASE.md must carry the folded technical appendix.");

        var viewer = new ReleaseNotesMarkdownViewer
        {
            Markdown = changelog[openIndex..(closeIndex + "</details>".Length)]
        };
        var window = new Window { Content = viewer };

        window.Show();

        var renderedContent = Assert.IsAssignableFrom<Control>(viewer.Content);
        var visibleText = EnumerateControls(renderedContent)
            .OfType<TextBlock>()
            .Select(ReadText)
            .Where(text => text.Length > 0)
            .ToArray();

        Assert.Contains(visibleText, text => text.Contains("SHA256SUMS", StringComparison.Ordinal));
        Assert.DoesNotContain(
            visibleText,
            text => text.Contains("<details>", StringComparison.Ordinal) || text.Contains("<summary>", StringComparison.Ordinal));

        window.Close();
    }

    private static string ReadText(TextBlock block)
    {
        var builder = new StringBuilder();
        Append(block.Inlines);
        if (builder.Length == 0 && block.Text is not null)
        {
            builder.Append(block.Text);
        }

        return builder.ToString();

        void Append(IEnumerable<Inline>? inlines)
        {
            foreach (var inline in inlines ?? [])
            {
                switch (inline)
                {
                    case Run run:
                        builder.Append(run.Text);
                        break;
                    case Span span:
                        Append(span.Inlines);
                        break;
                }
            }
        }
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        yield return root;

        IEnumerable<Control> children = root switch
        {
            Panel panel => panel.Children,
            Decorator decorator when decorator.Child is not null => [decorator.Child],
            ContentControl contentControl when contentControl.Content is Control child => [child],
            _ => []
        };

        foreach (var child in children)
        {
            foreach (var descendant in EnumerateControls(child))
            {
                yield return descendant;
            }
        }
    }
}
