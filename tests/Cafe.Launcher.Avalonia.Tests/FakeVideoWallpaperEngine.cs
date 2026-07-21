using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    public int DisposeCount { get; private set; }
    public Action? AfterLoad { get; set; }

    public IImage? CurrentFrame { get; private set; }

    public event Action? FrameReady;

    public Task<bool> LoadAsync(string path, CancellationToken cancellationToken)
    {
        LoadedPath = path;
        if (LoadResult)
        {
            // A platform-free stand-in frame: the real engine yields a WriteableBitmap, but these
            // unit tests must not require an Avalonia render platform.
            CurrentFrame = new StubFrame();
        }
        AfterLoad?.Invoke();
        return Task.FromResult(LoadResult);
    }

    public void RaiseFrameReady() => FrameReady?.Invoke();

    public void Play() => PlayCount++;
    public void Pause() => PauseCount++;
    public void Stop() => StopCount++;
    public void SetVolume(int volume) => LastVolume = volume;
    public void SetMuted(bool muted) => LastMuted = muted;

    // The fake's frame is not a real Bitmap; theme-color capture is exercised in engine-level tests.
    public Bitmap? CaptureFrame() => null;

    public void Dispose()
    {
        Disposed = true;
        DisposeCount++;
    }

    private sealed class StubFrame : IImage
    {
        public Size Size => new(2, 2);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
        }
    }
}
