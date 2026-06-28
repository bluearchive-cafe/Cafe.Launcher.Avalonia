using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class WindowsAnimationSettingsProviderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetWindowsAnimationsEnabled_WhenReadSucceeds_ReturnsValue(bool enabled)
    {
        var provider = new WindowsAnimationSettingsProvider(() => (true, enabled));

        Assert.Equal(enabled, provider.GetWindowsAnimationsEnabled());
    }

    [Fact]
    public void GetWindowsAnimationsEnabled_WhenReadFails_ReturnsNull()
    {
        var provider = new WindowsAnimationSettingsProvider(() => (false, true));

        Assert.Null(provider.GetWindowsAnimationsEnabled());
    }
}
