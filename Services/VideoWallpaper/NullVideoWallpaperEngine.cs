using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Cafe.Launcher.Avalonia.Services.VideoWallpaper;

/// <summary>
/// No-op engine used when native libvlc is unavailable. Always fails to load so callers fall back
/// to the bundled image.
/// </summary>
internal sealed class NullVideoWallpaperEngine : IVideoWallpaperEngine
{
    public IImage? CurrentFrame => null;

    public event Action? FrameReady { add { } remove { } }

    public Task<bool> LoadAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public void Play() { }
    public void Pause() { }
    public void Stop() { }
    public void SetVolume(int volume) { }
    public void SetMuted(bool muted) { }
    public Bitmap? CaptureFrame() => null;
    public void Dispose() { }
}
