using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Centralized factory for creating pre-configured <see cref="HttpClient"/> instances
/// and proxy-aware leases. Eliminates duplicate SocketsHttpHandler/HttpClient creation
/// across LauncherApiClient, ImageCacheService, ResourcePanelApiClient, and LauncherUpdateService.
/// Registered as a singleton in DI.
/// </summary>
public sealed class HttpClientFactory : IDisposable
{
    private sealed record CachedProxyHandler(string Fingerprint, SocketsHttpHandler Handler);

    private readonly SocketsHttpHandler defaultHandler;
    private readonly ProxySettingsService proxySettingsService;
    private readonly Dictionary<string, CachedProxyHandler> proxyHandlers = new(StringComparer.Ordinal);
    private readonly object proxyHandlerLock = new();
    private bool enableHttp2 = true;
    private bool disposed;

    public HttpClientFactory(ProxySettingsService proxySettingsService)
    {
        this.proxySettingsService = proxySettingsService;
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
    /// Configures the preferred HTTP version for clients created after this call.
    /// HTTP/2 remains optional and falls back to HTTP/1.1 when unavailable.
    /// </summary>
    public void ConfigureHttp2(bool enabled)
    {
        ThrowIfDisposed();
        Volatile.Write(ref enableHttp2, enabled);
    }

    /// <summary>
    /// Creates an HttpClient with a BaseAddress and timeout (direct connection, no proxy).
    /// The returned client shares the pooled handler and must be disposed by the caller.
    /// </summary>
    public HttpClient CreateClient(string baseAddress, TimeSpan timeout)
    {
        ThrowIfDisposed();
        var client = new HttpClient(defaultHandler, disposeHandler: false)
        {
            BaseAddress = new Uri(baseAddress),
            Timeout = timeout
        };
        ApplyHttpVersion(client);
        return client;
    }

    /// <summary>
    /// Creates an HttpClient with a timeout and no base address (direct connection, no proxy).
    /// The returned client shares the pooled handler and must be disposed by the caller.
    /// </summary>
    public HttpClient CreateClient(TimeSpan timeout)
    {
        ThrowIfDisposed();
        var client = new HttpClient(defaultHandler, disposeHandler: false)
        {
            Timeout = timeout
        };
        ApplyHttpVersion(client);
        return client;
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
            var client = new HttpClient(defaultHandler, disposeHandler: false);
            if (baseAddress is not null) client.BaseAddress = baseAddress;
            if (timeout.HasValue) client.Timeout = timeout.Value;
            ApplyHttpVersion(client);
            return new HttpClientLease(client, ownsClient: true);
        }

        var handler = await GetOrAddProxyHandlerAsync(proxyMode, cancellationToken).ConfigureAwait(false);
        var proxyClient = new HttpClient(handler, disposeHandler: false);
        if (baseAddress is not null) proxyClient.BaseAddress = baseAddress;
        if (timeout.HasValue) proxyClient.Timeout = timeout.Value;
        ApplyHttpVersion(proxyClient);
        return new HttpClientLease(proxyClient, ownsClient: true)
        {
            ConnectionProxy = handler.UseProxy ? handler.Proxy : null
        };
    }

    private void ApplyHttpVersion(HttpClient client)
    {
        client.DefaultRequestVersion = Volatile.Read(ref enableHttp2)
            ? HttpVersion.Version20
            : HttpVersion.Version11;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
    }

    private async Task<SocketsHttpHandler> GetOrAddProxyHandlerAsync(
        string proxyMode,
        CancellationToken cancellationToken)
    {
        var fingerprint = await proxySettingsService
            .GetProxyFingerprintAsync(proxyMode, cancellationToken)
            .ConfigureAwait(false);

        lock (proxyHandlerLock)
        {
            if (proxyHandlers.TryGetValue(proxyMode, out var cached)
                && cached.Fingerprint == fingerprint)
            {
                return cached.Handler;
            }
        }

        // Handler creation is async (proxy resolution), so it happens outside the lock;
        // a concurrent lease may cache an equivalent handler first, in which case the
        // freshly created one is disposed unused.
        var created = await proxySettingsService
            .CreateHttpHandlerAsync(proxyMode, cancellationToken)
            .ConfigureAwait(false);
        lock (proxyHandlerLock)
        {
            if (proxyHandlers.TryGetValue(proxyMode, out var existing)
                && existing.Fingerprint == fingerprint)
            {
                created.Dispose();
                return existing.Handler;
            }

            if (proxyHandlers.TryGetValue(proxyMode, out var stale))
            {
                stale.Handler.Dispose();
            }

            proxyHandlers[proxyMode] = new CachedProxyHandler(fingerprint, created);
            return created;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    /// <summary>Only for use by test projects (see <c>InternalsVisibleTo</c>).</summary>
    internal int CachedProxyHandlerCount
    {
        get { lock (proxyHandlerLock) return proxyHandlers.Count; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lock (proxyHandlerLock)
        {
            foreach (var cached in proxyHandlers.Values)
            {
                cached.Handler.Dispose();
            }

            proxyHandlers.Clear();
        }

        defaultHandler.Dispose();
        GC.SuppressFinalize(this);
    }
}
