using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(Cafe.Launcher.HeadlessTests.HeadlessTestAppBuilder))]

namespace Cafe.Launcher.HeadlessTests;

public static class HeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<Cafe.Launcher.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                // M5: golden screenshots need real rendering (Skia) to capture
                // pixel-accurate baselines (spec §9).
                UseHeadlessDrawing = false
            })
            .UseSkia()
            .AfterSetup(_ =>
            {
                if (!OperatingSystem.IsWindows())
                {
                    return;
                }

                // FluentAvalonia 3.1 skips Win32 HWND subclassing in preview mode.
                // Headless windows share a stub HWND and cannot use that native path.
                typeof(Design).GetProperty(nameof(Design.IsDesignMode))!.SetValue(null, true);
            });
}
