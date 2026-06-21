using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class ResourcePanelApiClient : IDisposable
{
    private static readonly string ApiBaseUrl = ApiConfig.ResourcePanelApiBaseUrl;
    private readonly IHttpClientLeaseSource leaseSource;
    private readonly JsonSerializerOptions jsonOptions = JsonDefaults.Strict;

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
        using var response = await lease.Client.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new ResourcePanelConfigResponse();
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<ResourcePanelConfigResponse>(stream, jsonOptions, cancellationToken).ConfigureAwait(false)
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
        using var response = await lease.Client.GetAsync(path, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task<T> GetJsonAsync<T>(
        string path,
        string proxyMode,
        CancellationToken cancellationToken)
        where T : new()
    {
        using var lease = await leaseSource.CreateLeaseAsync(proxyMode, cancellationToken).ConfigureAwait(false);
        using var response = await lease.Client.GetAsync(path, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, jsonOptions, cancellationToken).ConfigureAwait(false) ?? new T();
    }

    public void Dispose()
    {
        leaseSource.Dispose();
    }
}
