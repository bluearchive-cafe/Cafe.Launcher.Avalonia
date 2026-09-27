using System;
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
    /// while no window is attached or its layout has not produced a size yet.
    /// </summary>
    PixelSize GetPhysicalClientSize();

    /// <summary>
    /// Raised on the UI thread after the physical client size changed (resize or DPI
    /// change). Consumers that bound work to a previously reported size re-check here.
    /// </summary>
    event Action? PhysicalSizeChanged;
}
