using System;
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
    private readonly LibVLC libVlc;
    private readonly MediaPlayer mediaPlayer;

    private IntPtr nativeBuffer;
    private int bufferSize;
    private uint videoWidth;
    private uint videoHeight;
    private uint stride;
    private WriteableBitmap? frontBuffer;
    private WriteableBitmap? backBuffer;
    private int frameInFlight; // 0 = idle, 1 = a CopyAndSwap is queued/running (guarded via Interlocked)
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
            var size = new PixelSize(w, h);
            var dpi = new Vector(96, 96);
            frontBuffer = new WriteableBitmap(size, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
            backBuffer = new WriteableBitmap(size, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
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
    private unsafe void CopyAndSwap()
    {
        if (backBuffer is null || nativeBuffer == IntPtr.Zero)
        {
            return;
        }

        using (var fb = backBuffer.Lock())
        {
            Buffer.MemoryCopy(
                nativeBuffer.ToPointer(), fb.Address.ToPointer(), bufferSize, bufferSize);
        }

        (frontBuffer, backBuffer) = (backBuffer, frontBuffer);
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
        mediaPlayer.Dispose();
        libVlc.Dispose();
        FreeNativeBuffer();
        frontBuffer?.Dispose();
        backBuffer?.Dispose();
    }
}
