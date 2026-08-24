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
    /// <c>fetchWithRetry</c>: 10s timeout (enforced by the lease), 2 retries,
    /// 800ms × attempt linear backoff. Retries fire only on thrown network
    /// errors (timeout/socket), not on HTTP non-2xx — matching the dashboard
    /// which only retries <c>catch</c> blocks, leaving HTTP status handling
    /// to the caller.
    /// </summary>
    private const int MaxRetries = 2;
    private const int RetryDelayMs = 800;

    private readonly IHttpClientLeaseSource leaseSource;

    public ResourcePanelApiClient(HttpClientFactory httpClientFactory)
    {
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
            lease.Client, path, cancellationToken).ConfigureAwait(false);
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
            lease.Client, path, cancellationToken).ConfigureAwait(false);
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
            lease.Client, path, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await RemoteHttpRequestService.DeserializeJsonAsync<T>(
            response, new Uri(ApiBaseUrl + path), jsonOptions, cancellationToken).ConfigureAwait(false) ?? new T();
    }

    /// <summary>
    /// Sends a GET request with bounded retry + linear backoff on network
    /// errors only (not HTTP non-2xx), mirroring the dashboard's
    /// <c>fetchWithRetry</c>. Cancellation always propagates immediately.
    /// </summary>
    private static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        return await RetryPolicy.ExecuteWithRetryAsync(
            async ct => await client.GetAsync(path, ct).ConfigureAwait(false),
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
