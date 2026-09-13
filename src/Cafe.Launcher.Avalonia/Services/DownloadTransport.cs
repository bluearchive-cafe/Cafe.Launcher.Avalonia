using System;
using System.Net.Http;
using Cafe.Launcher.Avalonia.Helpers;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Batch-scoped connection for the game-file download channel: one transport
/// per download batch wraps one proxy-aware lease, so thousands of files share
/// a single validated, redirect-following send path instead of threading a raw
/// <see cref="HttpClient"/> and its connection proxy through every call.
/// </summary>
public interface IDownloadTransport : IDisposable
{
    /// <summary>
    /// Sends one request through the manual redirect loop: per-hop URL
    /// revalidation, egress-aware SSRF skipping, redirect limits, and
    /// HTTPS→HTTP downgrade rejection. Non-2xx answers are the caller's
    /// business — the transport enforces no status.
    /// </summary>
    Task<HttpResponseMessage> SendAsync(
        Uri uri,
        Func<Uri, HttpRequestMessage> createRequest,
        CancellationToken cancellationToken);
}

/// <summary>Creates one <see cref="IDownloadTransport"/> per download batch.</summary>
public interface IDownloadTransportSource
{
    Task<IDownloadTransport> CreateAsync(string proxyMode, CancellationToken cancellationToken);
}

/// <summary>Production transport over one proxy-aware lease.</summary>
public sealed class LeaseBackedDownloadTransport(
    HttpClientLease lease,
    RemoteHttpUrlValidator urlValidator) : IDownloadTransport
{
    public async Task<HttpResponseMessage> SendAsync(
        Uri uri,
        Func<Uri, HttpRequestMessage> createRequest,
        CancellationToken cancellationToken) =>
        await RemoteHttpRequestService.SendAsync(
            lease.Client,
            uri,
            createRequest,
            urlValidator,
            cancellationToken,
            connectionProxy: lease.ConnectionProxy).ConfigureAwait(false);

    public void Dispose() => lease.Dispose();
}

/// <summary>
/// Production transport source: leases come from the shared
/// <see cref="HttpClientFactory"/> with the download batch's long timeout.
/// </summary>
public sealed class LeaseBackedDownloadTransportSource(
    HttpClientFactory httpClientFactory,
    RemoteHttpUrlValidator urlValidator,
    TimeSpan leaseTimeout) : IDownloadTransportSource
{
    public async Task<IDownloadTransport> CreateAsync(
        string proxyMode,
        CancellationToken cancellationToken)
    {
        var lease = await httpClientFactory
            .CreateLeaseAsync(proxyMode, baseAddress: null, leaseTimeout, cancellationToken)
            .ConfigureAwait(false);
        return new LeaseBackedDownloadTransport(lease, urlValidator);
    }
}
