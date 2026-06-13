using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LauncherApplicationServicesTests
{
    [Fact]
    public void Dispose_WhenCalledTwice_DoesNotThrow()
    {
        var services = new LauncherApplicationServices();

        services.Dispose();
        services.Dispose();
    }
}
