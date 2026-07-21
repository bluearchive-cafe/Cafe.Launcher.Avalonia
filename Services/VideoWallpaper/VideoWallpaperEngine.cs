using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace Cafe.Launcher.Avalonia.Services.VideoWallpaper;

/// <summary>
/// Native video wallpaper engine backed by LibVLCSharp memory rendering (video callbacks). Frames
/// are decoded into a native buffer, then copied on the UI thread into a double-buffered
/// <see cref="WriteableBitmap"/> pair and swapped, raising <see cref="FrameReady"/>. No
/// <c>VideoView</c>/native HWND is used, so the existing background Image control renders the frames
/// and overlays compose normally.
/// </summary>
internal sealed class VideoWallpaperEngine : IVideoWallpaperEngine
{
    // Cap frame delivery to ~30 fps to avoid burning CPU/GPU on cosmetic wallpaper playback.
    private const long MinFrameIntervalTicks = (long)(TimeSpan.TicksPerSecond / 30.0);

    // Decode-resolution cap. The wallpaper window defaults to 1300×754, so 1280 px on the longest
    // side keeps typical ≦1080p videos practically full-resolution while capping 4K/2K sources at
    // sensible memory and upload bandwidth (~2.4 MB/frame).
    private const int MaxDecodedSide = 1280;

    private readonly LibVLC libVlc;
    private readonly MediaPlayer mediaPlayer;

    private IntPtr nativeBuffer;
    private int bufferSize;
    private int sourceWidth;
    private int sourceHeight;
    private int decodedWidth;
    private int decodedHeight;
    private uint stride;
    private WriteableBitmap? frontBuffer;
    private WriteableBitmap? backBuffer;
    private int frameInFlight;
    private long lastFrameTimestamp;
    private bool disposed;

    public VideoWallpaperEngine()
    {
        libVlc = new LibVLC("--no-osd", "--no-stats", "--no-video-title-show");
        mediaPlayer = new MediaPlayer(libVlc) { EnableHardwareDecoding = true };
    }

    public IImage? CurrentFrame => frontBuffer;

    public event Action? FrameReady;

    public Task<bool> LoadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested || !File.Exists(path))
            {
                return Task.FromResult(false);
            }

            using var media = new Media(libVlc, new Uri(path));
            media.AddOption(":input-repeat=65535"); // loop playback
            mediaPlayer.SetVideoFormatCallbacks(OnVideoFormat, OnVideoCleanup);
            mediaPlayer.SetVideoCallbacks(OnLock, null, OnDisplay);
            return Task.FromResult(mediaPlayer.Play(media));
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }

    private uint OnVideoFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height,
        ref uint pitches, ref uint lines)
    {
        // Format callbacks are not expected to re-fire mid-playback for a single Media, but guard
        // against leaking the previous allocation/bitmaps if VLC ever does call this twice.
        FreeNativeBuffer();

        WriteChroma(chroma, "RV32"); // BGRA
        sourceWidth = (int)width;
        sourceHeight = (int)height;
        stride = width * 4;
        pitches = stride;
        lines = height;
        // Native buffer stays at source resolution — VLC always writes the full decoded frame.
        bufferSize = (int)(stride * height);
        nativeBuffer = Marshal.AllocHGlobal(bufferSize);

        // Bitmaps cap at MaxDecodedSide to cut GPU upload and compositing cost. A 4K source (32 MB
        // per frame) produces ~2 MB bitmaps — a 15× reduction in VRAM traffic per frame swap.
        var maxSide = Math.Max(sourceWidth, sourceHeight);
        var scale = maxSide > MaxDecodedSide ? MaxDecodedSide / (double)maxSide : 1.0;
        decodedWidth = Math.Max(1, (int)(sourceWidth * scale));
        decodedHeight = Math.Max(1, (int)(sourceHeight * scale));

        var w = decodedWidth;
        var h = decodedHeight;
        Dispatcher.UIThread.Post(() =>
        {
            var oldFront = frontBuffer;
            var oldBack = backBuffer;
            var size = new PixelSize(w, h);
            var dpi = new Vector(96, 96);
            frontBuffer = new WriteableBitmap(size, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
            backBuffer = new WriteableBitmap(size, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
            oldFront?.Dispose();
            oldBack?.Dispose();
        });
        return 1;
    }

    private static void WriteChroma(IntPtr chroma, string fourcc)
    {
        for (var i = 0; i < 4; i++)
        {
            Marshal.WriteByte(chroma, i, (byte)fourcc[i]);
        }
    }

    private void OnVideoCleanup(ref IntPtr opaque) => FreeNativeBuffer();

    private void FreeNativeBuffer()
    {
        if (nativeBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(nativeBuffer);
            nativeBuffer = IntPtr.Zero;
        }
    }

    private IntPtr OnLock(IntPtr opaque, IntPtr planes)
    {
        Marshal.WriteIntPtr(planes, nativeBuffer);
        return IntPtr.Zero;
    }

    private void OnDisplay(IntPtr opaque, IntPtr picture)
    {
        // Throttle to ~30 fps: skip frames that arrive faster than the minimum interval.
        // This is checked before any allocation or Dispatcher post — early-exit is cheap.
        var now = Stopwatch.GetTimestamp();
        if (now - lastFrameTimestamp < MinFrameIntervalTicks)
        {
            return;
        }

        lastFrameTimestamp = now;

        // Atomically claim the in-flight slot. If a previous frame is still queued/copying, drop this
        // one — VLC's display callback may fire from a decode thread, so the check-and-set must be atomic.
        if (Interlocked.CompareExchange(ref frameInFlight, 1, 0) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                CopyAndSwap();
                FrameReady?.Invoke();
            }
            finally
            {
                // Always release the slot, even if the copy/raise throws — otherwise frameInFlight
                // would stay claimed and every subsequent frame would be dropped (frozen video).
                Volatile.Write(ref frameInFlight, 0);
            }
        }, DispatcherPriority.Render);
    }

    // Single shared native buffer: VLC may begin writing the next frame while this copy runs, so an
    // occasional torn frame is possible. That is acceptable for a cosmetic, frame-dropping wallpaper;
    // we deliberately avoid the extra allocation/locking a tear-free path would require.
    //
    // When the source resolution exceeds MaxDecodedSide, each frame is bilinear-subsampled
    // into a capped WriteableBitmap. A 4K→1280 source yields ~2.4 MB bitmaps — a ≥6× reduction
    // in GPU upload volume per frame swap vs. full-resolution decode, with quality barely
    // distinguishable from 1:1 at wallpaper viewing distance.
    private unsafe void CopyAndSwap()
    {
        if (backBuffer is null || nativeBuffer == IntPtr.Zero)
        {
            return;
        }

        using (var fb = backBuffer.Lock())
        {
            var dst = (byte*)fb.Address.ToPointer();
            var src = (byte*)nativeBuffer.ToPointer();
            var srcStride = (int)stride;
            var dstStride = fb.RowBytes;

            if (sourceWidth == decodedWidth && sourceHeight == decodedHeight)
            {
                // Same resolution — row-at-a-time block copy with no subsampling overhead.
                var rowLen = Math.Min(srcStride, dstStride);
                for (var y = 0; y < decodedHeight; y++)
                {
                    Buffer.MemoryCopy(
                        src + (y * srcStride), dst + (y * dstStride), rowLen, rowLen);
                }
            }
            else
            {
                BilinearSubsample(src, srcStride, dst, dstStride);
            }
        }

        (frontBuffer, backBuffer) = (backBuffer, frontBuffer);
    }

    // Bilinear interpolation from source to decoded-buffer dimensions. Each destination pixel
    // blends its four nearest source neighbours — far smoother than nearest-neighbour at the cost
    // of a ~4× per-pixel compute budget. The output resolution is already clamped by MaxDecodedSide,
    // so the per-frame cost is bounded.
    private unsafe void BilinearSubsample(
        byte* src, int srcStride, byte* dst, int dstStride)
    {
        var xScale = decodedWidth > 1
            ? (float)(sourceWidth - 1) / (decodedWidth - 1)
            : 0f;
        var yScale = decodedHeight > 1
            ? (float)(sourceHeight - 1) / (decodedHeight - 1)
            : 0f;

        for (var dy = 0; dy < decodedHeight; dy++)
        {
            var sy = dy * yScale;
            var sy0 = (int)sy;
            var sy1 = Math.Min(sy0 + 1, sourceHeight - 1);
            var wy = sy - sy0;

            var dstRow = (int*)(dst + (dy * dstStride));
            var srcRow0 = (int*)(src + (sy0 * srcStride));
            var srcRow1 = (int*)(src + (sy1 * srcStride));

            for (var dx = 0; dx < decodedWidth; dx++)
            {
                var sx = dx * xScale;
                var sx0 = (int)sx;
                var sx1 = Math.Min(sx0 + 1, sourceWidth - 1);
                var wx = sx - sx0;

                var p00 = srcRow0[sx0];
                var p10 = srcRow0[sx1];
                var p01 = srcRow1[sx0];
                var p11 = srcRow1[sx1];

                // Blend each channel: weight = (1 - wx) * (1 - wy) for top-left, etc.
                var a = (1f - wx) * (1f - wy);
                var b = wx * (1f - wy);
                var c = (1f - wx) * wy;
                var d = wx * wy;

                byte B(int p, int shift) => (byte)(
                    ((p >> shift) & 0xFF) * a
                    + ((p10 >> shift) & 0xFF) * b
                    + ((p01 >> shift) & 0xFF) * c
                    + ((p11 >> shift) & 0xFF) * d);

                dstRow[dx] = (B(p00, 24) << 24) | (B(p00, 16) << 16) | (B(p00, 8) << 8) | B(p00, 0);
            }
        }
    }

    public void Play() => mediaPlayer.Play();
    public void Pause() => mediaPlayer.SetPause(true);
    public void Stop() => mediaPlayer.Stop();
    public void SetVolume(int volume) => mediaPlayer.Volume = Math.Clamp(volume, 0, 100);
    public void SetMuted(bool muted) => mediaPlayer.Mute = muted;

    public Bitmap? CaptureFrame() => frontBuffer;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try { mediaPlayer.Stop(); } catch (Exception) { /* ignore */ }
        try { mediaPlayer.Dispose(); } catch (Exception) { /* ignore */ }
        try { libVlc.Dispose(); } catch (Exception) { /* ignore */ }
        FreeNativeBuffer();
        try { frontBuffer?.Dispose(); } catch (Exception) { /* ignore */ }
        try { backBuffer?.Dispose(); } catch (Exception) { /* ignore */ }
    }
}
