using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Cafe.Launcher.Avalonia.Services.VideoWallpaper;

/// <summary>
/// Abstraction over native video playback. Decodes frames into an <see cref="IImage"/> (a
/// <see cref="WriteableBitmap"/> at runtime) so the existing background Image control renders them —
/// no native HWND, overlays compose normally. Typed as <see cref="IImage"/> to keep consumers and
/// tests free of the platform-bound bitmap type.
/// </summary>
internal interface IVideoWallpaperEngine : IDisposable
{
    /// <summary>The most recently decoded frame, or null before the first frame.</summary>
    IImage? CurrentFrame { get; }

    /// <summary>Raised on the UI thread after <see cref="CurrentFrame"/> swaps to a new frame.</summary>
    event Action? FrameReady;

    /// <summary>Loads and starts playback. Returns false if the engine or file is unusable.</summary>
    Task<bool> LoadAsync(string path, CancellationToken cancellationToken);

    void Play();
    void Pause();
    void Stop();
    void SetVolume(int volume);
    void SetMuted(bool muted);

    /// <summary>Snapshots the current frame as a Bitmap for theme-color extraction, or null.</summary>
    Bitmap? CaptureFrame();
}
