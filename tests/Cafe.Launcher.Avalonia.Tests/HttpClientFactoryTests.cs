using System.Net;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class HttpClientFactoryTests
{
    [Fact]
    public void ConfigureConnectionDefaults_WhenAppliedToFreshHandler_SetsSharedConnectionDefaults()
    {
        using var handler = new SocketsHttpHandler();

        HttpClientFactory.ConfigureConnectionDefaults(handler);

        Assert.False(handler.AllowAutoRedirect);
        Assert.Equal(DecompressionMethods.All, handler.AutomaticDecompression);
        Assert.Equal(TimeSpan.FromMinutes(15), handler.PooledConnectionLifetime);
        // 运行时默认 ConnectTimeout 为 100s：应用内最短请求超时是自更新的 15s，
        // 连接黑洞会先吃满整个请求预算才轮到有界重试。
        Assert.Equal(TimeSpan.FromSeconds(15), handler.ConnectTimeout);
        // HTTP/2 空闲连接 PING（对 HTTP/1.1 无效）：死连接在 ping 间隔 + 超时内
        // 暴露，而不是等下一次读触发 60s 停滞预算。
        Assert.Equal(TimeSpan.FromSeconds(30), handler.KeepAlivePingDelay);
    }

    [Fact]
    public async Task CreatedClients_WhenHttp2IsDisabled_UseHttp11WithFallback()
    {
        using var factory = new HttpClientFactory(new ProxySettingsService(), () => false);

        using var directLease = await factory.CreateLeaseAsync(ProxyModes.Direct);
        using var proxyLease = await factory.CreateLeaseAsync(ProxyModes.Auto);

        AssertHttpVersion(directLease.Client, HttpVersion.Version11);
        AssertHttpVersion(proxyLease.Client, HttpVersion.Version11);
    }

    [Fact]
    public async Task CreatedClients_WhenHttp2IsEnabled_UseHttp2WithFallback()
    {
        using var factory = new HttpClientFactory(new ProxySettingsService(), () => true);

        using var directLease = await factory.CreateLeaseAsync(ProxyModes.Direct);
        using var proxyLease = await factory.CreateLeaseAsync(ProxyModes.Auto);

        AssertHttpVersion(directLease.Client, HttpVersion.Version20);
        AssertHttpVersion(proxyLease.Client, HttpVersion.Version20);
    }

    /// <summary>
    /// 偏好是拉取的：来源改变后新建的租约跟随新值，全程没有任何「推送」这一步。
    /// </summary>
    [Fact]
    public async Task CreatedClients_WhenThePreferenceSourceChanges_TrackItWithoutAnyPush()
    {
        var enabled = false;
        using var factory = new HttpClientFactory(new ProxySettingsService(), () => enabled);

        using (var disabled = await factory.CreateLeaseAsync(ProxyModes.Direct))
        {
            AssertHttpVersion(disabled.Client, HttpVersion.Version11);
        }

        enabled = true;

        using var enabledLease = await factory.CreateLeaseAsync(ProxyModes.Direct);
        AssertHttpVersion(enabledLease.Client, HttpVersion.Version20);
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
    public async Task CreateLeaseAsync_WhenDirectMode_LeaseHasNoConnectionProxy()
    {
        using var factory = new HttpClientFactory(new ProxySettingsService());

        using var lease = await factory.CreateLeaseAsync(ProxyModes.Direct);

        Assert.Null(lease.ConnectionProxy);
    }

    [Fact]
    public async Task CreateLeaseAsync_WhenProxyMode_LeaseExposesConnectionProxy()
    {
        // 守卫（AUD-NET-002）：租约必须携带生效的代理对象，供 URL 校验按
        // URI 解析旁路/直连退化，而不是只拿到代理模式的设置枚举。
        using var factory = new HttpClientFactory(new ProxySettingsService());

        using var lease = await factory.CreateLeaseAsync(ProxyModes.Auto);

        // WebRequest.GetSystemWebProxy() 永不为 null；是否真经代理由其按 URI 判定。
        Assert.NotNull(lease.ConnectionProxy);
    }

    [Fact]
    public async Task CreateLeaseAsync_AfterFactoryIsDisposed_Throws()
    {
        var factory = new HttpClientFactory(new ProxySettingsService());
        factory.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => factory.CreateLeaseAsync(ProxyModes.Direct));
    }

    // 代理处理器的缓存行为测试（指纹换处理器、直连不缓存）随实现迁移至
    // ProxySettingsServiceTests——缓存由 ProxySettingsService 拥有。

    private static void AssertHttpVersion(HttpClient client, Version expectedVersion)
    {
        Assert.Equal(expectedVersion, client.DefaultRequestVersion);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, client.DefaultVersionPolicy);
    }
}
