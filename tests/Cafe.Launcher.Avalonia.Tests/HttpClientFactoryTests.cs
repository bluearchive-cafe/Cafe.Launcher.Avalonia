using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class HttpClientFactoryTests
{
    [Fact]
    public async Task FixedHttpClientLeaseSource_UsesInjectedHandler()
    {
        var handler = new RecordingHandler();
        using IHttpClientLeaseSource source = new FixedHttpClientLeaseSource(
            handler,
            new Uri("https://example.test/"),
            TimeSpan.FromSeconds(5));

        using var lease = await source.CreateLeaseAsync(ProxyModes.System);
        using var response = await lease.Client.GetAsync("status");

        response.EnsureSuccessStatusCode();
        Assert.Equal("https://example.test/status", handler.RequestUri);
    }

    [Fact]
    public async Task CreateLeaseAsync_WhenDirectLeaseIsDisposed_DisposesClient()
    {
        using var factory = new HttpClientFactory(new ProxySettingsService());
        var lease = await factory.CreateLeaseAsync(ProxyModes.Direct);

        lease.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => lease.Client.GetAsync("https://example.invalid"));
    }

    [Fact]
    public void CreateClient_WithBaseAddressAndTimeout_AppliesConfiguration()
    {
        using var factory = new HttpClientFactory(new ProxySettingsService());
        using var client = factory.CreateClient(
            "https://example.test/api/",
            TimeSpan.FromSeconds(7));

        Assert.Equal(new Uri("https://example.test/api/"), client.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(7), client.Timeout);
    }

    [Fact]
    public async Task CreateLeaseAsync_WithDirectConfiguration_AppliesBaseAddressAndTimeout()
    {
        using var factory = new HttpClientFactory(new ProxySettingsService());

        using var lease = await factory.CreateLeaseAsync(
            ProxyModes.Direct,
            new Uri("https://example.test/"),
            TimeSpan.FromSeconds(9));

        Assert.Equal(new Uri("https://example.test/"), lease.Client.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(9), lease.Client.Timeout);
    }

    [Fact]
    public void CreateClient_AfterFactoryIsDisposed_Throws()
    {
        var factory = new HttpClientFactory(new ProxySettingsService());
        factory.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => factory.CreateClient(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task CreateLeaseAsync_WhenAutoMode_CreatesProxyAwareLease()
    {
        using var factory = new HttpClientFactory(new ProxySettingsService());

        using var lease = await factory.CreateLeaseAsync(ProxyModes.Auto);

        // Auto mode goes through the proxy-aware path (non-direct),
        // so the lease should own a handler and the client should be usable.
        Assert.NotNull(lease.Client);
        Assert.Null(lease.Client.BaseAddress);
    }

    [Fact]
    public async Task CreateLeaseAsync_AfterFactoryIsDisposed_Throws()
    {
        var factory = new HttpClientFactory(new ProxySettingsService());
        factory.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => factory.CreateLeaseAsync(ProxyModes.Direct));
    }

    [Fact]
    public async Task CreateLeaseAsync_WhenProxyModeIsDirect_DoesNotCacheHandler()
    {
        using var factory = new HttpClientFactory(new ProxySettingsService());

        using (await factory.CreateLeaseAsync(ProxyModes.Direct))
        {
        }

        Assert.Equal(0, factory.CachedProxyHandlerCount);
    }

    [Fact]
    public async Task CreateLeaseAsync_WithUnchangedProxySettings_ReusesCachedHandler()
    {
        var factory = CreateFactory(() => new SystemProxySettings(
            "http://proxy.example.invalid:8080",
            ["localhost"]));

        using (await factory.CreateLeaseAsync(ProxyModes.System))
        {
        }

        using (await factory.CreateLeaseAsync(ProxyModes.System))
        {
        }

        Assert.Equal(1, factory.CachedProxyHandlerCount);
    }

    [Fact]
    public async Task CreateLeaseAsync_WhenProxySettingsChange_ReplacesCachedHandler()
    {
        var settings = new SystemProxySettings(
            "http://proxy-a.example.invalid:8080",
            ["localhost"]);
        using var factory = CreateFactory(() => settings);

        using (await factory.CreateLeaseAsync(ProxyModes.System))
        {
        }

        settings = new SystemProxySettings(
            "http://proxy-b.example.invalid:8080",
            []);
        using (await factory.CreateLeaseAsync(ProxyModes.System))
        {
        }

        Assert.Equal(1, factory.CachedProxyHandlerCount);
    }

    private static HttpClientFactory CreateFactory(Func<SystemProxySettings?> provider) =>
        new(new ProxySettingsService(provider));

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
