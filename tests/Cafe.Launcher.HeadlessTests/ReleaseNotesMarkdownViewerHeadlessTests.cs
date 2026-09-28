using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Cafe.Launcher.UI.Controls;
using Cafe.Launcher.Testing;
using Xunit;

namespace Cafe.Launcher.HeadlessTests;

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
    /// <remarks>
    /// 断言必须与版本内容无关：此前它钉的是附录里恰好出现过的字面量 <c>SHA256SUMS</c>，
    /// 于是一次重写附录就让它变红，而它想守的东西（条目在应用内可见）其实没坏——这类
    /// 探针会训练维护者忽略它，正好放弃它本该抓住的那条信号。改为按结构断言：条目是
    /// 有长度的文本、且不出现裸标签。
    /// </remarks>
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

        // 条目本体的下限：一条被 HTML 块吞掉的条目不会留下这么长的文本，而列表符号（"•"）
        // 本身也不会被算进来。阈值取 5：当前版本的附录远超这个数，写少一两条也不会误红。
        const int MinimumRenderedEntries = 5;
        const int MinimumEntryLength = 20;
        var renderedEntries = visibleText.Count(text => text.Length >= MinimumEntryLength);
        Assert.True(
            renderedEntries >= MinimumRenderedEntries,
            $"折叠技术附录在应用内只渲染出 {renderedEntries} 条有内容的文本（期望 ≥ {MinimumRenderedEntries}）："
            + "HTML 块把条目吞掉了，或附录被改成了别的结构。");

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
