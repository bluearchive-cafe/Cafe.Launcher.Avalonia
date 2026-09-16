using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Lease/client plumbing over the shared connection pool: hands out proxy-aware
/// leases (direct clients share the pooled default handler; proxy handlers come
/// from <see cref="ProxySettingsService"/>) and owns the connection-level defaults.
/// 客户端偏好由注入的来源按租约解析（见 ADR-028），本模块不接受偏好推送。
/// </summary>
public sealed class HttpClientFactory : IDisposable
{
    private readonly SocketsHttpHandler defaultHandler;
    private readonly ProxySettingsService proxySettingsService;
    private readonly Func<bool> enableHttp2Preference;
    private bool disposed;

    /// <summary>
    /// <paramref name="enableHttp2Preference"/> 在每次租约创建时求值，缺省为 HTTP/2 开启。
    /// 偏好与代理模式同源——组合根上的一次闭包读已保存设置——所以没有调用方需要
    /// 「记得推送」，也不会出现某条租约用了过期开关。
    /// </summary>
    public HttpClientFactory(
        ProxySettingsService proxySettingsService,
        Func<bool>? enableHttp2Preference = null)
    {
        this.proxySettingsService = proxySettingsService;
        this.enableHttp2Preference = enableHttp2Preference ?? (static () => true);
        defaultHandler = new SocketsHttpHandler();
        ConfigureConnectionDefaults(defaultHandler);
        defaultHandler.UseProxy = false;
    }

    /// <summary>
    /// Shared connection-level defaults for every pooled handler the launcher
    /// creates (direct and proxy alike). The handler must be freshly constructed;
    /// callers layer their proxy-specific settings afterwards.
    /// </summary>
    /// <remarks>
    /// <para><c>ConnectTimeout</c> replaces the 100s runtime default: the shortest
    /// request timeout in the app is the 15s update check, so an unresponsive dial
    /// would otherwise consume the entire request budget before the bounded retries
    /// even begin.</para>
    /// <para><c>KeepAlivePingDelay</c> enables HTTP/2 PING on idle connections
    /// (no effect on HTTP/1.1). With HTTP/2 enabled by default, a dead multiplexed
    /// connection now surfaces within roughly ping delay + ping timeout instead of
    /// waiting for the 60s body-stall budget on the next read.</para>
    /// </remarks>
    internal static void ConfigureConnectionDefaults(SocketsHttpHandler handler)
    {
        handler.AllowAutoRedirect = false;
        handler.AutomaticDecompression = DecompressionMethods.All;
        handler.PooledConnectionLifetime = TimeSpan.FromMinutes(15);
        handler.ConnectTimeout = TimeSpan.FromSeconds(15);
        handler.KeepAlivePingDelay = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Returns a lease to a proxy-aware HttpClient. Direct clients share the long-lived
    /// default handler; proxy modes share a cached handler keyed by proxy mode and
    /// revalidated against the current proxy fingerprint on every lease, so a system
    /// proxy change replaces the handler instead of being baked into every lease.
    /// Each lease disposes only its own HttpClient instance.
    /// </summary>
    public async Task<HttpClientLease> CreateLeaseAsync(
        string proxyMode,
        Uri? baseAddress = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (proxyMode == ProxyModes.Direct)
        {
            return new HttpClientLease(CreateClient(defaultHandler, baseAddress, timeout), ownsClient: true);
        }

        var handler = await proxySettingsService
            .GetOrCreateHandlerAsync(proxyMode, cancellationToken)
            .ConfigureAwait(false);
        var proxyClient = CreateClient(handler, baseAddress, timeout);
        return new HttpClientLease(proxyClient, ownsClient: true)
        {
            ConnectionProxy = handler.UseProxy ? handler.Proxy : null
        };
    }

    /// <summary>
    /// 两条租约分支共用的客户端构造：handler 始终归工厂所有（disposeHandler: false），
    /// 租约只释放自己那个 HttpClient。
    /// </summary>
    private HttpClient CreateClient(SocketsHttpHandler handler, Uri? baseAddress, TimeSpan? timeout)
    {
        var client = new HttpClient(handler, disposeHandler: false);
        if (baseAddress is not null) client.BaseAddress = baseAddress;
        if (timeout.HasValue) client.Timeout = timeout.Value;
        ApplyHttpVersion(client);
        return client;
    }

    private void ApplyHttpVersion(HttpClient client)
    {
        client.DefaultRequestVersion = enableHttp2Preference()
            ? HttpVersion.Version20
            : HttpVersion.Version11;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        // 代理处理器的缓存与生命周期由 ProxySettingsService 拥有；
        // 这里只释放直连默认处理器。
        defaultHandler.Dispose();
        GC.SuppressFinalize(this);
    }
}
