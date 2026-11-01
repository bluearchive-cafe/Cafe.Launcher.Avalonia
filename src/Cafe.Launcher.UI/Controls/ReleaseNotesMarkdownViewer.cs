using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Services;
using Markdig;
using MarkView.Avalonia;
using MarkView.Avalonia.Extensions;
using MarkView.Avalonia.Rendering;

namespace Cafe.Launcher.UI.Controls;

/// <summary>
/// 更新说明 Markdown 表面：远程图片经宿主提供的缓存加载，外部链接经协议校验后打开。
/// </summary>
internal sealed class ReleaseNotesMarkdownViewer : MarkdownViewer
{
    /// <summary>提供启动器统一网络与缓存路径的图片字节加载器。</summary>
    public static readonly StyledProperty<Func<string, CancellationToken, Task<byte[]>>?> ImageLoaderProperty =
        AvaloniaProperty.Register<ReleaseNotesMarkdownViewer, Func<string, CancellationToken, Task<byte[]>>?>(nameof(ImageLoader));

    /// <summary>跟随覆盖层实际可见性，在退出动效结束后取消加载并释放图片。</summary>
    public static readonly StyledProperty<bool> IsDocumentActiveProperty =
        AvaloniaProperty.Register<ReleaseNotesMarkdownViewer, bool>(nameof(IsDocumentActive), true);

    private ImageScope? imageScope;

    static ReleaseNotesMarkdownViewer()
    {
        // Documents own decoded bitmaps. MarkView's shared cache keys loader types, so reopening
        // would reuse a bitmap disposed with the previous document. Encoded bytes already use
        // the launcher's ImageCacheService; keep decoded images within their document scope.
        MarkdownViewerDefaults.ImageCacheSize = 0;
    }

    /// <summary>构造 XAML 使用的更新说明查看器。</summary>
    public ReleaseNotesMarkdownViewer() : this(ExternalLinkService.Open)
    {
    }

    /// <summary>通过导航替身验证链接行为，测试不会启动系统浏览器。</summary>
    internal ReleaseNotesMarkdownViewer(Action<string?> openLink)
    {
        Extensions.Add(new RemoteImagesExtension(this));
        Pipeline = CreatePipeline();
        LinkClicked += (_, args) =>
        {
            args.Handled = true;
            if (ExternalLinkService.TryCreateAllowedUri(args.Url, out var uri))
            {
                openLink(uri.AbsoluteUri);
            }
        };
    }

    /// <summary>只提供字节加载行为，查看器负责位图的展示与释放。</summary>
    public Func<string, CancellationToken, Task<byte[]>>? ImageLoader
    {
        get => GetValue(ImageLoaderProperty);
        set => SetValue(ImageLoaderProperty, value);
    }

    /// <summary>覆盖层实际可见时为 true，包括退出动效期间。</summary>
    public bool IsDocumentActive
    {
        get => GetValue(IsDocumentActiveProperty);
        set => SetValue(IsDocumentActiveProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty && string.IsNullOrEmpty(Markdown))
        {
            ReleaseImages();
            return;
        }
        if (change.Property == IsDocumentActiveProperty || change.Property == ImageLoaderProperty)
        {
            if (IsDocumentActive)
            {
                Pipeline = CreatePipeline();
            }
            else
            {
                ReleaseImages();
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (IsDocumentActive && Content is null && !string.IsNullOrEmpty(Markdown))
        {
            Pipeline = CreatePipeline();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ReleaseImages();
        base.OnDetachedFromVisualTree(e);
    }

    private static MarkdownPipeline CreatePipeline() => new MarkdownPipelineBuilder()
        .UseSupportedExtensions()
        .UseAlertBlocks()
        .Build();

    private void ReleaseImages()
    {
        // 先分离旧内容，避免任何渲染帧仍引用即将释放的位图。
        foreach (var image in this.GetVisualDescendants().OfType<Avalonia.Controls.Image>())
        {
            image.Source = null;
        }
        Content = null;
        imageScope?.Dispose();
        imageScope = null;
    }

    private sealed class RemoteImagesExtension(ReleaseNotesMarkdownViewer owner) : IMarkViewExtension
    {
        public void Register(AvaloniaRenderer renderer)
        {
            owner.ReleaseImages();
            var scope = new ImageScope(owner.IsDocumentActive ? owner.ImageLoader : null);
            owner.imageScope = scope;
            // 替换库内默认 HTTP/data/资源加载器，所有出站请求仅经过启动器网络层。
            renderer.ImageLoaders.Clear();
            renderer.ImageLoaders.Add(scope);
        }
    }

    private sealed class ImageScope(Func<string, CancellationToken, Task<byte[]>>? loadBytes) : IImageLoader, IDisposable
    {
        private readonly CancellationTokenSource lifetime = new();
        private readonly List<Bitmap> bitmaps = [];
        private bool disposed;

        public bool CanLoad(string url) => loadBytes is not null
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme is "https" or "http";

        public async Task<IImage?> LoadAsync(string url, CancellationToken cancellationToken = default)
        {
            if (disposed || !CanLoad(url)) return null;

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            Bitmap? bitmap = null;
            try
            {
                var bytes = await loadBytes!(url, linked.Token).ConfigureAwait(false);
                bitmap = await Task.Run(() => BannerImageDecoder.Decode(bytes), linked.Token).ConfigureAwait(false);
                var decoded = bitmap;
                return await Dispatcher.UIThread.InvokeAsync<IImage?>(() =>
                {
                    if (disposed || linked.IsCancellationRequested)
                    {
                        decoded.Dispose();
                        return null;
                    }

                    bitmaps.Add(decoded);
                    return decoded;
                });
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                bitmap?.Dispose();
                return null;
            }
            catch (Exception)
            {
                // 图片失败不影响发布说明正文、下载动作或其他图片。
                bitmap?.Dispose();
                return null;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
            foreach (var bitmap in bitmaps)
            {
                bitmap.Dispose();
            }
            bitmaps.Clear();
        }
    }
}
