using System.Net;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ProxySettingsServiceTests : IDisposable
{
    [Fact]
    public async Task GetOrCreateHandlerAsync_WhenModeIsDirect_DisablesProxyAndDoesNotCache()
    {
        var service = new ProxySettingsService(() => throw new InvalidOperationException());

        using var handler = await service.GetOrCreateHandlerAsync(ProxyModes.Direct);

        Assert.False(handler.UseProxy);
        Assert.Null(handler.Proxy);
        Assert.False(handler.AllowAutoRedirect);
        Assert.Equal(0, service.CachedHandlerCount);
    }

    [Fact]
    public async Task GetOrCreateHandlerAsync_WhenModeIsAuto_EnablesProxyWithDefaultDetection()
    {
        // Auto 的指纹同样取自快照（与旧 GetProxyFingerprintAsync 一致），
        // 但处理器恒用实时系统默认检测——快照内容不改变 Auto 的出口。
        var service = new ProxySettingsService(() => null);

        using var handler = await service.GetOrCreateHandlerAsync(ProxyModes.Auto);

        Assert.True(handler.UseProxy);
        Assert.NotNull(handler.Proxy);
        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task GetOrCreateHandlerAsync_WhenModeIsSystem_UsesConfiguredProxy()
    {
        var service = new ProxySettingsService(() => new SystemProxySettings(
            "http://proxy.example.invalid:8080",
            ["localhost"]));

        using var handler = await service.GetOrCreateHandlerAsync(ProxyModes.System);

        Assert.True(handler.UseProxy);
        var proxy = Assert.IsType<WebProxy>(handler.Proxy);
        Assert.Equal(
            new Uri("http://proxy.example.invalid:8080"),
            proxy.GetProxy(new Uri("https://public.example.invalid")));
        Assert.True(proxy.IsBypassed(new Uri("http://localhost")));
        Assert.False(handler.AllowAutoRedirect);
        // 代理路径 handler 必须携带与直连 handler 相同的连接默认值
        // （见 HttpClientFactory.ConfigureConnectionDefaults）。
        Assert.Equal(TimeSpan.FromSeconds(15), handler.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), handler.KeepAlivePingDelay);
    }

    [Fact]
    public async Task GetOrCreateHandlerAsync_WhenSnapshotIsEmpty_FallsBackToSystemDetection()
    {
        var service = new ProxySettingsService(() => null);

        using var handler = await service.GetOrCreateHandlerAsync(ProxyModes.System);

        // 快照为空的 System 与 Auto 一致退回系统默认检测；Web 空快照不影响
        // 缓存身份（指纹取 "system-default"）。
        Assert.True(handler.UseProxy);
        Assert.NotNull(handler.Proxy);
        Assert.Equal(1, service.CachedHandlerCount);
    }

    [Fact]
    public async Task GetOrCreateHandlerAsync_WhenProxyIsSocks_ProducesSchemeAcceptedBySocketsHttpHandler()
    {
        var service = new ProxySettingsService(() => new SystemProxySettings(
            "socks://socks.example.invalid:1080",
            []));

        using var handler = await service.GetOrCreateHandlerAsync(ProxyModes.System);
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

        // SocketsHttpHandler rejects unversioned "socks://" with NotSupportedException at
        // request time; the normalized "socks5://" must instead attempt a real connection
        // (failing here with a socket/HTTP error because the .invalid proxy host cannot
        // resolve, which is the accepted outcome for this offline guard).
        var ex = await Record.ExceptionAsync(
            () => client.GetAsync("https://target.example.invalid/"));

        Assert.NotNull(ex);
        Assert.False(ex is NotSupportedException, $"Unexpected NotSupportedException: {ex.Message}");
    }

    [Theory]
    [InlineData("proxy.example.invalid:8080", "http://proxy.example.invalid:8080")]
    [InlineData("https://proxy.example.invalid:8443", "https://proxy.example.invalid:8443")]
    [InlineData("http=web.example.invalid:80;https=secure.example.invalid:443", "http://web.example.invalid:80")]
    [InlineData("socks=socks.example.invalid:1080;https=secure.example.invalid:443", "socks5://socks.example.invalid:1080")]
    [InlineData("socks://socks.example.invalid:1080", "socks5://socks.example.invalid:1080")]
    [InlineData("https=secure.example.invalid:443", "http://secure.example.invalid:443")]
    public async Task GetOrCreateHandlerAsync_WhenRegistryUsesRawUrlForm_NormalizesProxyAddress(
        string rawUrl,
        string expectedAddress)
    {
        // 规范化的唯一实现点在设置摄入处：提供者交付原始注册表值，
        // 构造出的代理地址必须是 SocketsHttpHandler 接受的完整形式。
        var service = new ProxySettingsService(() => new SystemProxySettings(rawUrl, []));

        using var handler = await service.GetOrCreateHandlerAsync(ProxyModes.System);

        var proxy = Assert.IsType<WebProxy>(handler.Proxy);
        Assert.Equal(new Uri(expectedAddress), proxy.Address);
    }

    [Fact]
    public async Task GetOrCreateHandlerAsync_WhenBypassListContainsWildcards_TranslatesToRegexAndBypassesMatches()
    {
        var service = new ProxySettingsService(() => new SystemProxySettings(
            "http://proxy.example.invalid:8080",
            ["*zhihu.com", "*.bilibili.com", "192.168.*", "<local>", "<-loopback>"]));

        using var handler = await service.GetOrCreateHandlerAsync(ProxyModes.System);

        var proxy = Assert.IsType<WebProxy>(handler.Proxy);
        // IsBypassed forces WebProxy.UpdateRegexList(), which previously threw
        // RegexParseException on the raw "*zhihu.com" wildcard.
        Assert.True(proxy.IsBypassed(new Uri("https://www.zhihu.com")));
        Assert.True(proxy.IsBypassed(new Uri("https://api.bilibili.com")));
        Assert.True(proxy.IsBypassed(new Uri("http://192.168.1.1")));
        Assert.Contains(".*zhihu\\.com", proxy.BypassList);
        Assert.Contains(".*\\.bilibili\\.com", proxy.BypassList);
        Assert.Contains("192\\.168\\..*", proxy.BypassList);
        Assert.True(proxy.BypassProxyOnLocal);
        Assert.Equal(
            new Uri("http://proxy.example.invalid:8080"),
            proxy.GetProxy(new Uri("https://api-launcher-jp.yo-star.com")));
    }

    [Theory]
    [InlineData("host?.example", "host.\\.example")]
    [InlineData("plain.example.com", "plain\\.example\\.com")]
    public async Task GetOrCreateHandlerAsync_WhenBypassEntryUsesPlainHostPattern_TranslatesEscaped(
        string entry,
        string expectedPattern)
    {
        var service = new ProxySettingsService(() => new SystemProxySettings(
            "http://proxy.example.invalid:8080",
            [entry]));

        using var handler = await service.GetOrCreateHandlerAsync(ProxyModes.System);

        var proxy = Assert.IsType<WebProxy>(handler.Proxy);
        Assert.Contains(expectedPattern, proxy.BypassList);
    }

    [Fact]
    public async Task GetOrCreateHandlerAsync_WhenBypassListContainsLocalToken_EnablesLocalBypass()
    {
        var service = new ProxySettingsService(() => new SystemProxySettings(
            "http://proxy.example.invalid:8080",
            ["<local>"]));

        using var handler = await service.GetOrCreateHandlerAsync(ProxyModes.System);

        var proxy = Assert.IsType<WebProxy>(handler.Proxy);
        Assert.True(proxy.BypassProxyOnLocal);
    }

    [Fact]
    public async Task GetOrCreateHandlerAsync_WithUnchangedSettings_ReusesCachedHandler()
    {
        var service = new ProxySettingsService(() => new SystemProxySettings(
            "http://proxy.example.invalid:8080",
            ["localhost"]));

        using var first = await service.GetOrCreateHandlerAsync(ProxyModes.System);
        using var second = await service.GetOrCreateHandlerAsync(ProxyModes.System);

        Assert.Same(first, second);
        Assert.Equal(1, service.CachedHandlerCount);
    }

    [Fact]
    public async Task GetOrCreateHandlerAsync_WhenSettingsChange_ReplacesCachedHandler()
    {
        // 守卫（指纹⇄处理器等价）：指纹计算与处理器构造在单一方法内相遇，
        // 注册表中途变更必然得到新处理器，而不可能拿到旧配置的缓存。
        var settings = new SystemProxySettings(
            "http://proxy-a.example.invalid:8080",
            ["localhost"]);
        var service = new ProxySettingsService(() => settings);

        using var first = await service.GetOrCreateHandlerAsync(ProxyModes.System);
        settings = new SystemProxySettings(
            "http://proxy-b.example.invalid:8080",
            []);
        using var second = await service.GetOrCreateHandlerAsync(ProxyModes.System);

        Assert.NotSame(first, second);
        Assert.Equal(1, service.CachedHandlerCount);
        var proxy = Assert.IsType<WebProxy>(second.Proxy);
        Assert.Equal(new Uri("http://proxy-b.example.invalid:8080"), proxy.Address);
    }

    [Fact]
    public async Task GetOrCreateHandlerAsync_WhenModesDiffer_CachesPerMode()
    {
        var service = new ProxySettingsService(() => new SystemProxySettings(
            "http://proxy.example.invalid:8080",
            ["localhost"]));

        using var systemHandler = await service.GetOrCreateHandlerAsync(ProxyModes.System);
        using var autoHandler = await service.GetOrCreateHandlerAsync(ProxyModes.Auto);

        // Auto 与 System 语义不同（前者恒用系统默认检测），缓存按模式区分。
        Assert.NotSame(systemHandler, autoHandler);
        Assert.Equal(2, service.CachedHandlerCount);
    }

    [Fact]
    public async Task GetOrCreateHandlerAsync_AfterDispose_Throws()
    {
        var service = new ProxySettingsService(() => null);
        service.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.GetOrCreateHandlerAsync(ProxyModes.System));
    }

    public void Dispose()
    {
    }
}
