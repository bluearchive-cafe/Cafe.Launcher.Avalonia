using System;
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
    private readonly SocketsHttpHandler defaultHandler;
    private readonly ProxySettingsService proxySettingsService;
    private bool disposed;

    public HttpClientFactory(ProxySettingsService proxySettingsService)
    {
        this.proxySettingsService = proxySettingsService;
        defaultHandler = new SocketsHttpHandler
        {
            UseProxy = false,
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
    /// Returns a lease to a proxy-aware HttpClient. When proxyMode is System,
    /// creates a per-request handler+client. Direct clients share the long-lived handler,
    /// while each lease disposes its own HttpClient instance.
    /// </summary>
    public async Task<HttpClientLease> CreateLeaseAsync(
        string proxyMode,
        Uri? baseAddress = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (proxyMode != ProxyModes.System)
        {
            var client = new HttpClient(defaultHandler, disposeHandler: false);
            if (baseAddress is not null) client.BaseAddress = baseAddress;
            if (timeout.HasValue) client.Timeout = timeout.Value;
            return new HttpClientLease(client, ownsClient: true);
        }

        var handler = await proxySettingsService.CreateHttpHandlerAsync(proxyMode, cancellationToken);
        var proxyClient = new HttpClient(handler, disposeHandler: false);
        if (baseAddress is not null) proxyClient.BaseAddress = baseAddress;
        if (timeout.HasValue) proxyClient.Timeout = timeout.Value;
        return new HttpClientLease(proxyClient, handler);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        defaultHandler.Dispose();
    }
}
