using Avalonia;
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
                // pixel-accurate baselines (spec §10).
                UseHeadlessDrawing = false
            })
            .UseSkia();
}
