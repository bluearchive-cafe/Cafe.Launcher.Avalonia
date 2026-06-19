using System;
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
    private static readonly string ApiBaseUrl = LauncherConstants.ResourcePanelApiBaseUrl;
    private readonly IHttpClientLeaseSource leaseSource;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

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

    public Task<ResourcePanelConfigResponse> GetConfigAsync(
        string uid,
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        var path = $"/config/get?uid={Uri.EscapeDataString(uid)}";
        return GetJsonAsync<ResourcePanelConfigResponse>(
            path,
            proxyMode,
            cancellationToken);
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
        using var lease = await leaseSource.CreateLeaseAsync(proxyMode, cancellationToken);
        using var response = await lease.Client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<T> GetJsonAsync<T>(
        string path,
        string proxyMode,
        CancellationToken cancellationToken)
        where T : new()
    {
        using var lease = await leaseSource.CreateLeaseAsync(proxyMode, cancellationToken);
        using var response = await lease.Client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, jsonOptions, cancellationToken) ?? new T();
    }

    public void Dispose()
    {
        leaseSource.Dispose();
    }
}
