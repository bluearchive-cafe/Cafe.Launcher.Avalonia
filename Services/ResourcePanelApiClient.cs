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
    private readonly HttpClient httpClient;
    private readonly HttpClientFactory httpClientFactory;
    private readonly bool ownsHttpClient;
    private string proxyMode = ProxyModes.Direct;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public ResourcePanelApiClient(HttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
        httpClient = httpClientFactory.CreateClient(
            ApiBaseUrl,
            TimeSpan.FromSeconds(30));
        ownsHttpClient = true;
    }

    internal ResourcePanelApiClient(HttpMessageHandler handler)
    {
        httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        httpClientFactory = null!; // Not used in test path
        ownsHttpClient = true;
    }

    public void SetProxyMode(string value)
    {
        proxyMode = value == ProxyModes.System ? ProxyModes.System : ProxyModes.Direct;
    }

    public Task<ResourcePanelStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<ResourcePanelStatusResponse>("/status/list", cancellationToken);
    }

    public Task<ResourcePanelConfigResponse> GetConfigAsync(string uid, CancellationToken cancellationToken = default)
    {
        var path = $"/config/get?uid={Uri.EscapeDataString(uid)}";
        return GetJsonAsync<ResourcePanelConfigResponse>(path, cancellationToken);
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
        using var lease = await CreateRequestClientAsync(cancellationToken);
        using var response = await lease.Client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
        where T : new()
    {
        using var lease = await CreateRequestClientAsync(cancellationToken);
        using var response = await lease.Client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, jsonOptions, cancellationToken) ?? new T();
    }

    private async Task<HttpClientLease> CreateRequestClientAsync(CancellationToken cancellationToken)
    {
        if (httpClientFactory is not null)
        {
            return await httpClientFactory.CreateLeaseAsync(
                proxyMode,
                httpClient.BaseAddress,
                httpClient.Timeout,
                cancellationToken);
        }

        // Fallback for test constructor
        if (proxyMode != ProxyModes.System)
        {
            return new HttpClientLease(httpClient);
        }

        var handler = new SocketsHttpHandler
        {
            UseProxy = true,
            Proxy = System.Net.WebRequest.GetSystemWebProxy(),
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        };
        var client = new HttpClient(handler)
        {
            BaseAddress = httpClient.BaseAddress,
            Timeout = httpClient.Timeout
        };
        return new HttpClientLease(client, handler);
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }
}
