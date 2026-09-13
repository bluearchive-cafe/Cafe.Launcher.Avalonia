using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Features.ResourcePanel;

public sealed class ResourcePanelApiClient
{
    private static readonly string ApiBaseUrl = ApiConfig.ResourcePanelApiBaseUrl;

    /// <summary>
    /// Network resilience parameters mirrored from the dashboard's
    /// <c>fetchWithRetry</c>: 30s lease timeout, 2 retries,
    /// 800ms × attempt linear backoff. Retries fire only on thrown network
    /// errors (timeout/socket — no answer produced), not on HTTP non-2xx —
    /// matching the dashboard which only retries <c>catch</c> blocks, leaving
    /// HTTP status handling to the caller. Redirects and SSRF revalidation are
    /// the transport's job, as for every remote client.
    /// </summary>
    private const int MaxRetries = 2;
    private const int RetryDelayMs = 800;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly IRemoteHttpTransport transport;

    public ResourcePanelApiClient(IRemoteHttpTransport transport)
    {
        this.transport = transport;
    }

    public async Task<ResourcePanelStatusResponse> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        return await FetchJsonAsync<ResourcePanelStatusResponse>(
            "/status/list",
            cancellationToken).ConfigureAwait(false)
            ?? new ResourcePanelStatusResponse();
    }

    public async Task<ResourcePanelConfigResponse> GetConfigAsync(
        string uid,
        CancellationToken cancellationToken = default)
    {
        var path = $"/config/get?uid={Uri.EscapeDataString(uid)}";
        try
        {
            return await FetchJsonAsync<ResourcePanelConfigResponse>(
                path,
                cancellationToken).ConfigureAwait(false)
                ?? new ResourcePanelConfigResponse();
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // 未配置过的 UID 以 404 回答：按空配置处理，而非失败。
            return new ResourcePanelConfigResponse();
        }
    }

    public async Task SaveConfigAsync(
        string uid,
        string text,
        string voice,
        string media,
        CancellationToken cancellationToken = default)
    {
        var path = "/config/set"
            + $"?uid={Uri.EscapeDataString(uid)}"
            + $"&text={Uri.EscapeDataString(text)}"
            + $"&voice={Uri.EscapeDataString(voice)}"
            + $"&media={Uri.EscapeDataString(media)}";
        var remote = await transport.GetStreamAsync(
            new Uri(ApiBaseUrl + path),
            CreateOptions(),
            cancellationToken).ConfigureAwait(false);
        using var body = remote.Content;
    }

    private async Task<T?> FetchJsonAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        return await transport.GetJsonAsync<T>(
            new Uri(ApiBaseUrl + path),
            CreateOptions(),
            cancellationToken).ConfigureAwait(false);
    }

    private static RemoteRequestOptions CreateOptions() => new()
    {
        MaxAttempts = MaxRetries + 1,
        Backoff = i => TimeSpan.FromMilliseconds(RetryDelayMs * (i + 1)),
        RetryScope = RemoteRetryScope.Network,
        Timeout = RequestTimeout
    };
}
