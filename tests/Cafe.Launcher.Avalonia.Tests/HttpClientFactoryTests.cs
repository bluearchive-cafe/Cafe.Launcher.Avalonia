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
