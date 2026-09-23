using Markdig;
using MarkView.Avalonia;
using MarkView.Avalonia.Extensions;
using MarkView.Avalonia.Rendering;

namespace Cafe.Launcher.Avalonia.Controls;

/// <summary>
/// 更新说明专用 Markdown 表面：启用 GFM/Alerts，但不允许内容触发外部导航或图片请求。
/// </summary>
public sealed class ReleaseNotesMarkdownViewer : MarkdownViewer
{
    public ReleaseNotesMarkdownViewer()
    {
        Pipeline = new MarkdownPipelineBuilder()
            .UseSupportedExtensions()
            .UseAlertBlocks()
            .Build();
        Extensions.Add(DisableImagesExtension.Instance);
        LinkClicked += static (_, eventArgs) => eventArgs.Handled = true;
    }

    private sealed class DisableImagesExtension : IMarkViewExtension
    {
        public static DisableImagesExtension Instance { get; } = new();

        public void Register(AvaloniaRenderer renderer) => renderer.ImageLoaders.Clear();
    }
}
