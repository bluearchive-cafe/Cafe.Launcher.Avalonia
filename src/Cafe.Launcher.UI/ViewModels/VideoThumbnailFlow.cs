using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Cafe.Launcher.UI.Helpers;

namespace Cafe.Launcher.UI.ViewModels;

/// <summary>
/// 视频背景的首帧/封面截取流程：每次选中视频按文件指纹发起一次截取，换源时取消
/// 在途任务但保留已交付的帧供切回复用；迟到的旧结果按指纹与代数丢弃。只产出预览
/// 用的缩略图，不改变壁纸本身。
/// </summary>
internal sealed class VideoThumbnailFlow : IDisposable
{
    private readonly Func<string, CancellationToken, Task<IImage?>> loader;
    private readonly LatestRefresh refresh = new();
    private IImage? thumbnail;
    private string? sourceKey;
    private int generation;
    private bool disposed;

    public VideoThumbnailFlow(Func<string, CancellationToken, Task<IImage?>>? loader = null)
    {
        this.loader = loader ?? ((_, _) => Task.FromResult<IImage?>(null));
    }

    /// <summary>Raised whenever the delivered thumbnail changes or is cleared.</summary>
    public event Action? ThumbnailChanged;

    /// <summary>The current capture request, independent of presentation-only settings previews.</summary>
    public Task Pending => refresh.Pending;

    /// <summary>The delivered first frame, if one has arrived and not been cleared.</summary>
    public IImage? Thumbnail => thumbnail;

    /// <summary>Starts a capture unless this file fingerprint is already captured.</summary>
    public void Request(string path, string? fingerprint)
    {
        // 同一文件指纹的缩略图可跨「切回静态再选中」复用：每次重新截帧都要再起一个
        // 临时 libmpv 解码器，既耗 CPU 又让原生分配随切换次数累积（AUD-PERF-009）。
        if (fingerprint is not null && sourceKey == fingerprint && thumbnail is not null)
        {
            return;
        }

        Clear();
        sourceKey = fingerprint;
        var requestGeneration = generation;
        refresh.Run(null, async cancellationToken =>
        {
            try
            {
                var image = await loader(path, cancellationToken);
                // 只按「指纹 + 代数」判定陈旧：静态背景下完成的首帧仍属于该视频，
                // 保留下来供下次选中复用；换了来源或文件内容则丢弃。
                if (disposed || cancellationToken.IsCancellationRequested || requestGeneration != generation
                    || sourceKey != fingerprint)
                {
                    (image as IDisposable)?.Dispose();
                    return;
                }

                thumbnail = image;
                ThumbnailChanged?.Invoke();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        });
    }

    /// <summary>
    /// Gets the first frame for the current selection, awaiting the in-flight capture
    /// without cancelling it. The caller borrows the bitmap.
    /// </summary>
    public async Task<Bitmap?> GetThumbnailAsync(CancellationToken cancellationToken)
    {
        if (sourceKey is null)
        {
            return null;
        }

        await refresh.Pending.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return thumbnail as Bitmap;
    }

    /// <summary>取消在途截帧但保留已交付的缩略图，供再次选中同一文件时复用。</summary>
    public void CancelPending()
    {
        generation++;
        refresh.Cancel();
    }

    /// <summary>Drops the delivered thumbnail after bindings settle and cancels any capture.</summary>
    public void Clear()
    {
        generation++;
        refresh.Cancel();
        sourceKey = null;
        var old = thumbnail;
        thumbnail = null;
        ThumbnailChanged?.Invoke();
        BitmapLifetime.ReleaseAfterBindingsSettle(old);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Clear();
    }
}
