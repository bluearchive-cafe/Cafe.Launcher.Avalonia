using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services.VideoWallpaper;

internal static class VideoWallpaperEngineFactory
{
    /// <summary>Creates the native engine, or a Null engine if libvlc is unavailable.</summary>
    public static IVideoWallpaperEngine Create()
    {
        try
        {
            return new VideoWallpaperEngine();
        }
        catch (System.Exception ex)
        {
            LocalDiagnostics.LogSync("VideoWallpaper", $"Engine create failed; using null engine: {ex.Message}");
            return new NullVideoWallpaperEngine();
        }
    }
}
