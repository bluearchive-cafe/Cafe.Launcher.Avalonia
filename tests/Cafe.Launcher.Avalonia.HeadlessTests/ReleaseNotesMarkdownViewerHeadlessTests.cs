using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Cafe.Launcher.Avalonia.Controls;
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
