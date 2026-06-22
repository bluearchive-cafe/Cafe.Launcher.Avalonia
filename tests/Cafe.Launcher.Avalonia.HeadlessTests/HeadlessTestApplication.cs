using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(Cafe.Launcher.Avalonia.HeadlessTests.HeadlessTestAppBuilder))]

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public static class HeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<Cafe.Launcher.Avalonia.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
