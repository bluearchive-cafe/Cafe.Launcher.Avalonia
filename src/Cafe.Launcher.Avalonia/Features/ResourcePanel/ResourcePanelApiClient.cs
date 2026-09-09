using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Features.ResourcePanel;

public sealed class ResourcePanelApiClient : IDisposable
{
    private static readonly string ApiBaseUrl = ApiConfig.ResourcePanelApiBaseUrl;
    private readonly JsonSerializerOptions jsonOptions = JsonDefaults.Strict;

    /// <summary>
    /// Network resilience parameters mirrored from the dashboard's
    /// <c>fetchWithRetry</c>: 30s lease timeout, 2 retries,
    /// 800ms × attempt linear backoff. Retries fire only on thrown network
    /// errors (timeout/socket), not on HTTP non-2xx — matching the dashboard
    /// which only retries <c>catch</c> blocks, leaving HTTP status handling
    /// to the caller. Redirects follow the shared manual-redirect path
    /// (<see cref="RemoteHttpRequestService.SendAsync(HttpClient, Uri, Func{Uri, HttpRequestMessage}, RemoteHttpUrlValidator, CancellationToken, IWebProxy?)"/>)
    /// with per-hop URL revalidation, like every other remote client — the
    /// pooled handlers have <c>AllowAutoRedirect=false</c>, so a bare
    /// <c>HttpClient.GetAsync</c> would hard-fail on any 3xx.
    /// </summary>
    private const int MaxRetries = 2;
    private const int RetryDelayMs = 800;

    private readonly IHttpClientLeaseSource leaseSource;
    private readonly RemoteHttpUrlValidator urlValidator;

    public ResourcePanelApiClient(
        HttpClientFactory httpClientFactory,
        RemoteHttpUrlValidator urlValidator)
    {
        this.urlValidator = urlValidator;
        leaseSource = new ProxyAwareHttpClientLeaseSource(
            httpClientFactory,
            new Uri(ApiBaseUrl),
            TimeSpan.FromSeconds(30));
    }

    internal ResourcePanelApiClient(HttpMessageHandler handler)
    {
        leaseSource = new FixedHttpClientLeaseSource(
            handler,
            new Uri(ApiBaseUrl),
            TimeSpan.FromSeconds(30));
        urlValidator = RemoteHttpUrlValidator.CreateForTesting();
    }

    public Task<ResourcePanelStatusResponse> GetStatusAsync(
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<ResourcePanelStatusResponse>(
            "/status/list",
            proxyMode,
            cancellationToken);
    }

    public async Task<ResourcePanelConfigResponse> GetConfigAsync(
        string uid,
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        var path = $"/config/get?uid={Uri.EscapeDataString(uid)}";
        using var lease = await leaseSource.CreateLeaseAsync(proxyMode, cancellationToken).ConfigureAwait(false);
        using var response = await SendWithRetryAsync(
            lease.Client, path, lease.ConnectionProxy, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new ResourcePanelConfigResponse();
        }
        response.EnsureSuccessStatusCode();
        return await RemoteHttpRequestService.DeserializeJsonAsync<ResourcePanelConfigResponse>(
            response, new Uri(ApiBaseUrl + path), jsonOptions, cancellationToken).ConfigureAwait(false)
            ?? new ResourcePanelConfigResponse();
    }

    public async Task SaveConfigAsync(
        string uid,
        string text,
        string voice,
        string media,
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        var path = "/config/set"
            + $"?uid={Uri.EscapeDataString(uid)}"
            + $"&text={Uri.EscapeDataString(text)}"
            + $"&voice={Uri.EscapeDataString(voice)}"
            + $"&media={Uri.EscapeDataString(media)}";
        using var lease = await leaseSource.CreateLeaseAsync(proxyMode, cancellationToken).ConfigureAwait(false);
        using var response = await SendWithRetryAsync(
            lease.Client, path, lease.ConnectionProxy, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task<T> GetJsonAsync<T>(
        string path,
        string proxyMode,
        CancellationToken cancellationToken)
        where T : new()
    {
        using var lease = await leaseSource.CreateLeaseAsync(proxyMode, cancellationToken).ConfigureAwait(false);
        using var response = await SendWithRetryAsync(
            lease.Client, path, lease.ConnectionProxy, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await RemoteHttpRequestService.DeserializeJsonAsync<T>(
            response, new Uri(ApiBaseUrl + path), jsonOptions, cancellationToken).ConfigureAwait(false) ?? new T();
    }

    /// <summary>
    /// Sends a GET request with bounded retry + linear backoff on network
    /// errors only (not HTTP non-2xx), mirroring the dashboard's
    /// <c>fetchWithRetry</c>. Cancellation always propagates immediately.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client,
        string path,
        IWebProxy? connectionProxy,
        CancellationToken cancellationToken)
    {
        var requestUri = new Uri(ApiBaseUrl + path);
        return await RetryPolicy.ExecuteWithRetryAsync(
            async ct => await RemoteHttpRequestService.SendAsync(
                client,
                requestUri,
                static uri => new HttpRequestMessage(HttpMethod.Get, uri),
                urlValidator,
                ct,
                connectionProxy).ConfigureAwait(false),
            MaxRetries + 1,
            i => TimeSpan.FromMilliseconds(RetryDelayMs * (i + 1)),
            cancellationToken,
            ex => ex is HttpRequestException or TaskCanceledException);
    }

    public void Dispose()
    {
        leaseSource.Dispose();
        GC.SuppressFinalize(this);
    }
}
