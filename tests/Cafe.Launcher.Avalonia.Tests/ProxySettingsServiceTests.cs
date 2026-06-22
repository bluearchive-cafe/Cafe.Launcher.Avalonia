using System.Net;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ProxySettingsServiceTests
{
    [Fact]
    public async Task CreateProxyAsync_WhenModeIsDirect_ReturnsNull()
    {
        var service = new ProxySettingsService(() => throw new InvalidOperationException());

        var result = await service.CreateProxyAsync(ProxyModes.Direct);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateProxyAsync_WhenSystemSettingsExist_ReturnsConfiguredProxy()
    {
        var service = new ProxySettingsService(() => new SystemProxySettings(
            "http://proxy.example.invalid:8080",
            ["localhost", @".*\.internal\.example"]));

        var result = Assert.IsType<WebProxy>(
            await service.CreateProxyAsync(ProxyModes.System));

        Assert.Equal(
            new Uri("http://proxy.example.invalid:8080"),
            result.GetProxy(new Uri("https://public.example.invalid")));
        Assert.True(result.IsBypassed(new Uri("http://localhost")));
    }

    [Theory]
    [InlineData("proxy.example.invalid:8080", "http://proxy.example.invalid:8080")]
    [InlineData("https://proxy.example.invalid:8443", "https://proxy.example.invalid:8443")]
    [InlineData("http=web.example.invalid:80;https=secure.example.invalid:443", "http://web.example.invalid:80")]
    [InlineData("socks=socks.example.invalid:1080;https=secure.example.invalid:443", "socks://socks.example.invalid:1080")]
    [InlineData("https=secure.example.invalid:443", "http://secure.example.invalid:443")]
    public void ResolveProxyUrl_UsesExactConfiguredProtocol(string value, string expected)
    {
        Assert.Equal(expected, ProxySettingsService.ResolveProxyUrl(value));
    }

    [Fact]
    public async Task CreateHttpHandlerAsync_WhenModeIsDirect_DisablesProxy()
    {
        var service = new ProxySettingsService(() => throw new InvalidOperationException());

        using var handler = await service.CreateHttpHandlerAsync(ProxyModes.Direct);

        Assert.False(handler.UseProxy);
        Assert.Null(handler.Proxy);
        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task CreateHttpHandlerAsync_WhenModeIsSystem_UsesConfiguredProxy()
    {
        var service = new ProxySettingsService(() => new SystemProxySettings(
            "http://proxy.example.invalid:8080",
            []));

        using var handler = await service.CreateHttpHandlerAsync(ProxyModes.System);

        Assert.True(handler.UseProxy);
        Assert.IsType<WebProxy>(handler.Proxy);
        Assert.False(handler.AllowAutoRedirect);
    }
}
