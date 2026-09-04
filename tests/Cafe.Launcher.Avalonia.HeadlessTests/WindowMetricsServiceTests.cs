using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Services;
using Xunit;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary><see cref="WindowMetricsService"/> 的窗口挂接与物理尺寸快照回归。</summary>
public sealed class WindowMetricsServiceTests
{
    [AvaloniaFact]
    public void GetPhysicalClientSize_WithoutAttachedWindow_ReturnsFallbackAndRecoversAfterDetach()
    {
        var service = new WindowMetricsService();
        Assert.Equal(BackgroundImageDecoder.FallbackTarget, service.GetPhysicalClientSize());

        var window = new Window();
        service.Attach(window);
        var attachedSize = service.GetPhysicalClientSize();
        Assert.Equal(
            (int)Math.Ceiling(window.ClientSize.Width * window.RenderScaling),
            attachedSize.Width);
        Assert.Equal(
            (int)Math.Ceiling(window.ClientSize.Height * window.RenderScaling),
            attachedSize.Height);

        service.Detach(window);
        Assert.Equal(BackgroundImageDecoder.FallbackTarget, service.GetPhysicalClientSize());
    }
}
