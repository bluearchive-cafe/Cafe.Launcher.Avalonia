using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Services;

internal interface IHttpClientLeaseSource : IDisposable
{
    Task<HttpClientLease> CreateLeaseAsync(CancellationToken cancellationToken = default);
}

internal sealed class ProxyAwareHttpClientLeaseSource : IHttpClientLeaseSource
{
    private readonly HttpClientFactory httpClientFactory;
    private readonly Func<string> getProxyMode;
    private readonly Uri? baseAddress;
    private readonly TimeSpan? timeout;

    public ProxyAwareHttpClientLeaseSource(
        HttpClientFactory httpClientFactory,
        Func<string> getProxyMode,
        Uri? baseAddress,
        TimeSpan? timeout)
    {
        this.httpClientFactory = httpClientFactory;
        this.getProxyMode = getProxyMode;
        this.baseAddress = baseAddress;
        this.timeout = timeout;
    }

    public Task<HttpClientLease> CreateLeaseAsync(CancellationToken cancellationToken = default) =>
        httpClientFactory.CreateLeaseAsync(
            getProxyMode(),
            baseAddress,
            timeout,
            cancellationToken);

    public void Dispose()
    {
    }
}

internal sealed class FixedHttpClientLeaseSource : IHttpClientLeaseSource
{
    private readonly HttpClient httpClient;

    public FixedHttpClientLeaseSource(
        HttpMessageHandler handler,
        Uri? baseAddress,
        TimeSpan? timeout)
    {
        httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = baseAddress,
            Timeout = timeout ?? System.Threading.Timeout.InfiniteTimeSpan
        };
    }

    public Task<HttpClientLease> CreateLeaseAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new HttpClientLease(httpClient));

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
