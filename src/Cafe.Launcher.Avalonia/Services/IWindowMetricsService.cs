using Avalonia;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Exposes the attached window's physical client pixel size. Consumers bound work that
/// scales with output resolution (e.g. wallpaper decoding) to what the window can
/// actually show instead of paying for pixels that will be downsampled away.
/// </summary>
public interface IWindowMetricsService
{
    /// <summary>
    /// Physical client size (ClientSize × RenderScaling). Returns a 1920×1080 default
    /// while no window is attached — headless tests and early startup.
    /// </summary>
    PixelSize GetPhysicalClientSize();
}
