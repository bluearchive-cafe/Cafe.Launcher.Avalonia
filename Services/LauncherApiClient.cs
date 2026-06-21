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
    private readonly IHttpClientLeaseSource leaseSource;
    private readonly AuthorizationHeaderFactory authorizationHeaderFactory;
    private readonly PatchUrlGroupService patchUrlGroupService;
    private readonly JsonSerializerOptions jsonOptions = JsonDefaults.Strict;

    /// <summary>Production constructor — accepts dependencies from DI.</summary>
    public LauncherApiClient(
        HttpClientFactory httpClientFactory,
        AuthorizationHeaderFactory authorizationHeaderFactory,
        PatchUrlGroupService patchUrlGroupService)
    {
        leaseSource = new ProxyAwareHttpClientLeaseSource(
            httpClientFactory,
            new Uri(ApiConfig.ApiBaseUrl),
            TimeSpan.FromSeconds(30));
        this.authorizationHeaderFactory = authorizationHeaderFactory;
        this.patchUrlGroupService = patchUrlGroupService;
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
        leaseSource = new FixedHttpClientLeaseSource(
            handler,
            new Uri(ApiConfig.ApiBaseUrl),
            TimeSpan.FromSeconds(30));
        this.authorizationHeaderFactory = authorizationHeaderFactory;
        this.patchUrlGroupService = patchUrlGroupService;
    }

    public Task<GameConfigResponse> GetGameConfigAsync(
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        return GetEnvelopeDataAsync<GameConfigResponse>(
            "/api/launcher/game/config",
            proxyMode,
            cancellationToken);
    }

    public Task<BaseConfigResponse> GetBaseConfigAsync(
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        return GetEnvelopeDataAsync<BaseConfigResponse>(
            "/api/launcher/base/config",
            proxyMode,
            cancellationToken);
    }

    private Task<CdnConfigResponse> GetCdnConfigAsync(
        string proxyMode,
        CancellationToken cancellationToken)
    {
        return GetEnvelopeDataAsync<CdnConfigResponse>(
            "/api/launcher/advanced/game/download/cdn",
            proxyMode,
            cancellationToken);
    }

    public async Task<CdnConfigResponse> GetCdnConfigAsync(
        string patchUrlGroup,
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        var response = await GetCdnConfigAsync(proxyMode, cancellationToken).ConfigureAwait(false);
        return RewriteCdnConfig(response, patchUrlGroup);
    }

    public Task<OperationsResourceResponse> GetOperationsResourceAsync(
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        return GetEnvelopeDataAsync<OperationsResourceResponse>(
            "/api/launcher/operations/resource",
            proxyMode,
            cancellationToken);
    }

    public Task<SocialMediaResourceResponse> GetSocialMediaResourceAsync(
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        return GetEnvelopeDataAsync<SocialMediaResourceResponse>(
            "/api/launcher/social/media/resource",
            proxyMode,
            cancellationToken);
    }

    public Task<InstallationConfigResponse> GetInstallationConfigAsync(
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        return GetEnvelopeDataAsync<InstallationConfigResponse>(
            "/api/launcher/installation/config",
            proxyMode,
            cancellationToken);
    }

    private Task<ManifestUrlResponse> GetManifestUrlAsync(
        string version,
        string filePath,
        string proxyMode,
        CancellationToken cancellationToken)
    {
        var requestPath = $"/api/launcher/game/config/json?version={Uri.EscapeDataString(version)}&file_path={Uri.EscapeDataString(filePath)}";
        return GetEnvelopeDataAsync<ManifestUrlResponse>(
            requestPath,
            proxyMode,
            cancellationToken);
    }

    public async Task<ManifestUrlResponse> GetManifestUrlAsync(
        string version,
        string filePath,
        string patchUrlGroup,
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        var response = await GetManifestUrlAsync(
            version,
            filePath,
            proxyMode,
            cancellationToken).ConfigureAwait(false);
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

    public async Task<RemoteManifest> GetRemoteManifestAsync(
        string url,
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        using var lease = await leaseSource.CreateLeaseAsync(proxyMode, cancellationToken).ConfigureAwait(false);
        await using var stream = await lease.Client.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
        var manifest = await JsonSerializer.DeserializeAsync<RemoteManifest>(stream, jsonOptions, cancellationToken).ConfigureAwait(false);
        return manifest ?? new RemoteManifest();
    }

    private async Task<T> GetEnvelopeDataAsync<T>(
        string path,
        string proxyMode,
        CancellationToken cancellationToken)
    {
        using var lease = await leaseSource.CreateLeaseAsync(proxyMode, cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            authorizationHeaderFactory.Create("", ApiConfig.YostarAuthorizationVersion));

        using var response = await lease.Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var envelope = await JsonSerializer.DeserializeAsync<LauncherApiEnvelope<T>>(stream, jsonOptions, cancellationToken).ConfigureAwait(false);

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

    public void Dispose()
    {
        leaseSource.Dispose();
    }
}
