using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class HttpClientFactoryTests
{
    [Fact]
    public async Task CreateLeaseAsync_WhenDirectLeaseIsDisposed_DisposesClient()
    {
        using var factory = new HttpClientFactory(new ProxySettingsService());
        var lease = await factory.CreateLeaseAsync(ProxyModes.Direct);

        lease.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => lease.Client.GetAsync("https://example.invalid"));
    }
}
