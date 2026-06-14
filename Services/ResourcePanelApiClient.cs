using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class ResourcePanelApiClient : IDisposable
{
    private const string ApiBaseUrl = "https://api.bluearchive.cafe";
    private readonly SocketsHttpHandler? ownedHandler;
    private readonly HttpClient httpClient;
    private readonly ProxySettingsService? proxySettingsService;
    private string proxyMode = ProxyModes.Direct;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public ResourcePanelApiClient()
    {
        ownedHandler = new SocketsHttpHandler
        {
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        };
        httpClient = new HttpClient(ownedHandler)
        {
            BaseAddress = new Uri(ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        proxySettingsService = new ProxySettingsService();
    }

    internal ResourcePanelApiClient(HttpMessageHandler handler)
    {
        httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
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

    private async Task<RequestHttpClientLease> CreateRequestClientAsync(CancellationToken cancellationToken)
    {
        if (proxySettingsService is null || proxyMode != ProxyModes.System)
        {
            return new RequestHttpClientLease(httpClient);
        }

        var handler = await proxySettingsService.CreateHttpHandlerAsync(proxyMode, cancellationToken);
        var client = new HttpClient(handler)
        {
            BaseAddress = httpClient.BaseAddress,
            Timeout = httpClient.Timeout
        };
        return new RequestHttpClientLease(client, handler);
    }

    public void Dispose()
    {
        httpClient.Dispose();
        ownedHandler?.Dispose();
    }

    private sealed class RequestHttpClientLease : IDisposable
    {
        private readonly SocketsHttpHandler? handler;
        private readonly bool ownsClient;

        public RequestHttpClientLease(HttpClient client)
        {
            Client = client;
        }

        public RequestHttpClientLease(HttpClient client, SocketsHttpHandler handler)
        {
            Client = client;
            this.handler = handler;
            ownsClient = true;
        }

        public HttpClient Client { get; }

        public void Dispose()
        {
            if (ownsClient)
            {
                Client.Dispose();
                handler?.Dispose();
            }
        }
    }
}
