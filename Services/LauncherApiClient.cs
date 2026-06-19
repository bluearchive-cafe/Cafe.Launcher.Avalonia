using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Auth;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class LauncherApiClient : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly HttpClientFactory? httpClientFactory;
    private readonly AuthorizationHeaderFactory authorizationHeaderFactory;
    private readonly PatchUrlGroupService patchUrlGroupService;
    private readonly bool ownsHttpClient;
    public string ProxyMode { get; set; } = ProxyModes.Direct;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    /// <summary>Production constructor — accepts dependencies from DI.</summary>
    public LauncherApiClient(
        HttpClientFactory httpClientFactory,
        AuthorizationHeaderFactory authorizationHeaderFactory,
        PatchUrlGroupService patchUrlGroupService)
    {
        this.httpClientFactory = httpClientFactory;
        httpClient = httpClientFactory.CreateClient(
            LauncherConstants.ApiBaseUrl,
            TimeSpan.FromSeconds(30));
        this.authorizationHeaderFactory = authorizationHeaderFactory;
        this.patchUrlGroupService = patchUrlGroupService;
        ownsHttpClient = true;
    }

    /// <summary>
    /// Injectable constructor — accepts an <see cref="HttpMessageHandler"/> for testability.
    /// The handler is NOT disposed by this class (caller owns its lifetime).
    /// </summary>
    internal LauncherApiClient(
        HttpMessageHandler handler,
        AuthorizationHeaderFactory authorizationHeaderFactory,
        PatchUrlGroupService patchUrlGroupService)
    {
        httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(LauncherConstants.ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        this.authorizationHeaderFactory = authorizationHeaderFactory;
        this.patchUrlGroupService = patchUrlGroupService;
        ownsHttpClient = true; // Test path — own the client
    }

    public Task<GameConfigResponse> GetGameConfigAsync(CancellationToken cancellationToken = default)
    {
        return GetEnvelopeDataAsync<GameConfigResponse>("/api/launcher/game/config", cancellationToken);
    }

    public Task<BaseConfigResponse> GetBaseConfigAsync(CancellationToken cancellationToken = default)
    {
        return GetEnvelopeDataAsync<BaseConfigResponse>("/api/launcher/base/config", cancellationToken);
    }

    public Task<CdnConfigResponse> GetCdnConfigAsync(CancellationToken cancellationToken = default)
    {
        return GetEnvelopeDataAsync<CdnConfigResponse>("/api/launcher/advanced/game/download/cdn", cancellationToken);
    }

    public async Task<CdnConfigResponse> GetCdnConfigAsync(string patchUrlGroup, CancellationToken cancellationToken = default)
    {
        var response = await GetCdnConfigAsync(cancellationToken);
        return RewriteCdnConfig(response, patchUrlGroup);
    }

    public Task<OperationsResourceResponse> GetOperationsResourceAsync(CancellationToken cancellationToken = default)
    {
        return GetEnvelopeDataAsync<OperationsResourceResponse>("/api/launcher/operations/resource", cancellationToken);
    }

    public Task<SocialMediaResourceResponse> GetSocialMediaResourceAsync(CancellationToken cancellationToken = default)
    {
        return GetEnvelopeDataAsync<SocialMediaResourceResponse>("/api/launcher/social/media/resource", cancellationToken);
    }

    public Task<InstallationConfigResponse> GetInstallationConfigAsync(CancellationToken cancellationToken = default)
    {
        return GetEnvelopeDataAsync<InstallationConfigResponse>("/api/launcher/installation/config", cancellationToken);
    }

    public Task<ManifestUrlResponse> GetManifestUrlAsync(string version, string filePath, CancellationToken cancellationToken = default)
    {
        var requestPath = $"/api/launcher/game/config/json?version={Uri.EscapeDataString(version)}&file_path={Uri.EscapeDataString(filePath)}";
        return GetEnvelopeDataAsync<ManifestUrlResponse>(requestPath, cancellationToken);
    }

    public async Task<ManifestUrlResponse> GetManifestUrlAsync(string version, string filePath, string patchUrlGroup, CancellationToken cancellationToken = default)
    {
        var response = await GetManifestUrlAsync(version, filePath, cancellationToken);
        return RewriteManifestUrl(response, patchUrlGroup);
    }

    internal ManifestUrlResponse RewriteManifestUrl(ManifestUrlResponse response, string patchUrlGroup)
    {
        return patchUrlGroupService.RewriteManifestUrl(response, patchUrlGroup);
    }

    internal CdnConfigResponse RewriteCdnConfig(CdnConfigResponse response, string patchUrlGroup)
    {
        return patchUrlGroupService.RewriteCdnConfig(response, patchUrlGroup);
    }

    public async Task<RemoteManifest> GetRemoteManifestAsync(string url, CancellationToken cancellationToken = default)
    {
        using var lease = await CreateLeaseAsync(cancellationToken);
        await using var stream = await lease.Client.GetStreamAsync(url, cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<RemoteManifest>(stream, jsonOptions, cancellationToken);
        return manifest ?? new RemoteManifest();
    }

    private async Task<T> GetEnvelopeDataAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var lease = await CreateLeaseAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            authorizationHeaderFactory.Create("", LauncherConstants.YostarAuthorizationVersion));

        using var response = await lease.Client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var envelope = await JsonSerializer.DeserializeAsync<LauncherApiEnvelope<T>>(stream, jsonOptions, cancellationToken);

        if (envelope is null)
        {
            throw new InvalidOperationException("API response body is empty.");
        }

        if (envelope.Code != 200)
        {
            var message = envelope.Message ?? envelope.Msg ?? $"API response code: {envelope.Code}";
            throw new InvalidOperationException(message);
        }

        if (envelope.Data is null)
        {
            throw new InvalidOperationException("API response data is empty.");
        }

        return envelope.Data;
    }

    private Task<HttpClientLease> CreateLeaseAsync(CancellationToken ct) =>
        httpClientFactory is not null
            ? httpClientFactory.CreateLeaseAsync(ProxyMode, httpClient.BaseAddress, httpClient.Timeout, ct)
            : Task.FromResult(new HttpClientLease(httpClient));

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }
}
