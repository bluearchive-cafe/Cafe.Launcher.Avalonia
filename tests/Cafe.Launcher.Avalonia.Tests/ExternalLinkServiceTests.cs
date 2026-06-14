using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ExternalLinkServiceTests
{
    [Theory]
    [InlineData("https://example.invalid/path")]
    [InlineData("http://example.invalid/path")]
    [InlineData("mailto:support@example.invalid")]
    public void TryCreateAllowedUri_WhenSchemeIsAllowed_ReturnsTrue(string url)
    {
        var result = ExternalLinkService.TryCreateAllowedUri(url, out _);

        Assert.True(result);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("cmd://example")]
    [InlineData("not a url")]
    public void TryCreateAllowedUri_WhenSchemeIsBlocked_ReturnsFalse(string url)
    {
        var result = ExternalLinkService.TryCreateAllowedUri(url, out _);

        Assert.False(result);
    }
}
