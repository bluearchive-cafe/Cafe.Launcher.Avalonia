using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Cafe.Launcher.Avalonia.Services.VideoWallpaper;

namespace Cafe.Launcher.Avalonia.Tests;

internal sealed class FakeVideoWallpaperEngine : IVideoWallpaperEngine
{
    public bool LoadResult { get; set; } = true;
    public string? LoadedPath { get; private set; }
    public int PlayCount { get; private set; }
    public int PauseCount { get; private set; }
    public int StopCount { get; private set; }
    public int? LastVolume { get; private set; }
    public bool? LastMuted { get; private set; }
    public bool Disposed { get; private set; }

    public WriteableBitmap? CurrentFrame { get; private set; }

    public event Action? FrameReady;

    public Task<bool> LoadAsync(string path, CancellationToken cancellationToken)
    {
        LoadedPath = path;
        if (LoadResult)
        {
            CurrentFrame = new WriteableBitmap(
                new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        }
        return Task.FromResult(LoadResult);
    }

    public void RaiseFrameReady() => FrameReady?.Invoke();

    public void Play() => PlayCount++;
    public void Pause() => PauseCount++;
    public void Stop() => StopCount++;
    public void SetVolume(int volume) => LastVolume = volume;
    public void SetMuted(bool muted) => LastMuted = muted;

    public Bitmap? CaptureFrame() => CurrentFrame;

    public void Dispose() => Disposed = true;
}
