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
    private bool disposed;

    public HttpClientFactory(ProxySettingsService proxySettingsService)
    {
        this.proxySettingsService = proxySettingsService;
        defaultHandler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        };
    }

    /// <summary>
    /// Creates an HttpClient with a BaseAddress and timeout (direct connection, no proxy).
    /// The returned client shares the pooled handler and must be disposed by the caller.
    /// </summary>
    public HttpClient CreateClient(string baseAddress, TimeSpan timeout)
    {
        ThrowIfDisposed();
        return new HttpClient(defaultHandler, disposeHandler: false)
        {
            BaseAddress = new Uri(baseAddress),
            Timeout = timeout
        };
    }

    /// <summary>
    /// Creates an HttpClient with a timeout and no base address (direct connection, no proxy).
    /// The returned client shares the pooled handler and must be disposed by the caller.
    /// </summary>
    public HttpClient CreateClient(TimeSpan timeout)
    {
        ThrowIfDisposed();
        return new HttpClient(defaultHandler, disposeHandler: false)
        {
            Timeout = timeout
        };
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
            return new HttpClientLease(client, ownsClient: true);
        }

        var handler = await GetOrAddProxyHandlerAsync(proxyMode, cancellationToken).ConfigureAwait(false);
        var proxyClient = new HttpClient(handler, disposeHandler: false);
        if (baseAddress is not null) proxyClient.BaseAddress = baseAddress;
        if (timeout.HasValue) proxyClient.Timeout = timeout.Value;
        return new HttpClientLease(proxyClient, ownsClient: true);
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
