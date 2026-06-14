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
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public ResourcePanelApiClient()
    {
        ownedHandler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        };
        httpClient = new HttpClient(ownedHandler)
        {
            BaseAddress = new Uri(ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    internal ResourcePanelApiClient(HttpMessageHandler handler)
    {
        httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
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
        using var response = await httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
        where T : new()
    {
        using var response = await httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, jsonOptions, cancellationToken) ?? new T();
    }

    public void Dispose()
    {
        httpClient.Dispose();
        ownedHandler?.Dispose();
    }
}
