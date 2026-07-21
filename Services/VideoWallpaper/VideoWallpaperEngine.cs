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
    private static readonly long MinFrameIntervalTicks = CalculateFrameInterval(Stopwatch.Frequency);

    internal static long CalculateFrameInterval(long frequency) => frequency / 30;

    private readonly LibVLC libVlc;
    private readonly MediaPlayer mediaPlayer;

    private IntPtr nativeBuffer;
    private int bufferSize;
    private uint videoWidth;
    private uint videoHeight;
    private uint stride;
    private WriteableBitmap? frontBuffer;
    private WriteableBitmap? backBuffer;
    private int frameInFlight;
    private long lastFrameTimestamp;
    private VideoWallpaperLoadGate? loadGate;
    private bool disposed;

    public VideoWallpaperEngine()
    {
        libVlc = new LibVLC("--no-osd", "--no-stats", "--no-video-title-show");
        mediaPlayer = new MediaPlayer(libVlc) { EnableHardwareDecoding = true };
    }

    public IImage? CurrentFrame => frontBuffer;

    public event Action? FrameReady;

    public async Task<bool> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || !File.Exists(path))
        {
            return false;
        }

        using var gate = new VideoWallpaperLoadGate(cancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var timeoutRegistration = timeout.Token.Register(gate.Fail);
        using var media = new Media(libVlc, new Uri(path));

        void FailLoad(object? sender, EventArgs args) => gate.Fail();

        mediaPlayer.EncounteredError += FailLoad;
        mediaPlayer.EndReached += FailLoad;
        mediaPlayer.Stopped += FailLoad;
        loadGate = gate;
        try
        {
            media.AddOption(":input-repeat=65535"); // loop playback
            mediaPlayer.SetVideoFormatCallbacks(OnVideoFormat, OnVideoCleanup);
            mediaPlayer.SetVideoCallbacks(OnLock, null, OnDisplay);
            if (!mediaPlayer.Play(media))
            {
                return false;
            }

            return await gate.WaitAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try { mediaPlayer.Stop(); } catch (Exception) { /* ignore */ }
            throw;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (ReferenceEquals(loadGate, gate))
            {
                loadGate = null;
            }
            mediaPlayer.EncounteredError -= FailLoad;
            mediaPlayer.EndReached -= FailLoad;
            mediaPlayer.Stopped -= FailLoad;
        }
    }

    private uint OnVideoFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height,
        ref uint pitches, ref uint lines)
    {
        // Format callbacks are not expected to re-fire mid-playback for a single Media, but guard
        // against leaking the previous allocation/bitmaps if VLC ever does call this twice.
        FreeNativeBuffer();

        WriteChroma(chroma, "RV32"); // BGRA
        videoWidth = width;
        videoHeight = height;
        stride = width * 4;
        pitches = stride;
        lines = height;
        bufferSize = (int)(stride * height);
        nativeBuffer = Marshal.AllocHGlobal(bufferSize);

        var w = (int)videoWidth;
        var h = (int)videoHeight;
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
                loadGate?.Succeed();
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
    private unsafe void CopyAndSwap()
    {
        if (backBuffer is null || nativeBuffer == IntPtr.Zero)
        {
            return;
        }

        using (var fb = backBuffer.Lock())
        {
            var src = (byte*)nativeBuffer.ToPointer();
            var dst = (byte*)fb.Address.ToPointer();
            var srcStride = (int)stride;
            var dstStride = fb.RowBytes;
            var rowLength = Math.Min(srcStride, dstStride);
            for (var y = 0; y < videoHeight; y++)
            {
                Buffer.MemoryCopy(
                    src + (y * srcStride), dst + (y * dstStride), rowLength, rowLength);
            }
        }

        (frontBuffer, backBuffer) = (backBuffer, frontBuffer);
    }

    public void Play() => mediaPlayer.Play();
    public void Pause() => mediaPlayer.SetPause(true);
    public void Stop()
    {
        loadGate?.Fail();
        mediaPlayer.Stop();
    }
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
        loadGate?.Fail();
        try { mediaPlayer.Stop(); } catch (Exception) { /* ignore */ }
        try { mediaPlayer.Dispose(); } catch (Exception) { /* ignore */ }
        try { libVlc.Dispose(); } catch (Exception) { /* ignore */ }
        FreeNativeBuffer();
        try { frontBuffer?.Dispose(); } catch (Exception) { /* ignore */ }
        try { backBuffer?.Dispose(); } catch (Exception) { /* ignore */ }
    }
}
