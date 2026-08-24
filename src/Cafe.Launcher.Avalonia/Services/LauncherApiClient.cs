using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Auth;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class LauncherApiClient : IDisposable
{
    private readonly IHttpClientLeaseSource leaseSource;
    private readonly AuthorizationHeaderFactory authorizationHeaderFactory;
    private readonly PatchUrlGroupService patchUrlGroupService;
    private readonly RemoteHttpUrlValidator urlValidator;
    private readonly JsonSerializerOptions jsonOptions = JsonDefaults.Strict;

    /// <summary>Production constructor — accepts dependencies from DI.</summary>
    public LauncherApiClient(
        HttpClientFactory httpClientFactory,
        AuthorizationHeaderFactory authorizationHeaderFactory,
        PatchUrlGroupService patchUrlGroupService,
        RemoteHttpUrlValidator urlValidator)
    {
        leaseSource = new ProxyAwareHttpClientLeaseSource(
            httpClientFactory,
            new Uri(ApiConfig.ApiBaseUrl),
            TimeSpan.FromSeconds(30));
        this.authorizationHeaderFactory = authorizationHeaderFactory;
        this.patchUrlGroupService = patchUrlGroupService;
        this.urlValidator = urlValidator;
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
        urlValidator = RemoteHttpUrlValidator.CreateForTesting();
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

    public async Task<BaseConfigResponse> GetBaseConfigAsync(
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        var response = await GetEnvelopeDataAsync<BaseConfigResponse>(
            "/api/launcher/base/config",
            proxyMode,
            cancellationToken).ConfigureAwait(false);
        response.LauncherBackgroundImg = ResolveLauncherBackgroundUrl(
            response.LauncherBackgroundImg);
        return response;
    }

    private static string? ResolveLauncherBackgroundUrl(string? value)
    {
        const string packageRelativePrefix =
            "/prod/BlueArchive_JP/launcher_background_img/";
        return value?.StartsWith(packageRelativePrefix, StringComparison.Ordinal) == true
            ? ApiConfig.OfficialPackageBaseUrl + value
            : value;
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

    internal string RestoreOfficialPackageUrl(string url)
    {
        return patchUrlGroupService.RestoreOfficialPackageUrl(url);
    }

    internal CdnConfigResponse RewriteCdnConfig(CdnConfigResponse response, string patchUrlGroup)
    {
        return patchUrlGroupService.RewriteCdnConfig(response, patchUrlGroup);
    }

    /// <summary>
    /// Maximum number of attempts for transient manifest fetch failures
    /// (initial attempt + retries). Mirrors the bounded retry philosophy of
    /// <see cref="FileDownloadService.RetryDomainOrder"/> but with fewer
    /// attempts — manifests are small metadata payloads, not large file
    /// downloads. Backoff: 500ms, 1000ms.
    /// </summary>
    private const int MaxManifestFetchAttempts = 3;
    private static readonly TimeSpan[] ManifestFetchBackoff =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1000)
    ];

    /// <summary>
    /// Maximum retry attempts for core API envelope calls (game config, CDN config, etc.).
    /// These are more critical than manifest downloads because they determine startup state.
    /// Uses the same backoff sequence as manifest fetches.
    /// </summary>
    private const int MaxEnvelopeFetchAttempts = 3;

    public async Task<RemoteManifest> GetRemoteManifestAsync(
        string url,
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        var requestUri = new Uri(url);

        return await RetryPolicy.ExecuteWithRetryAsync(
            async ct =>
            {
                using var lease = await leaseSource
                    .CreateLeaseAsync(proxyMode, ct)
                    .ConfigureAwait(false);
                using var response = await RemoteHttpRequestService.SendAsync(
                    lease.Client,
                    requestUri,
                    static uri => new HttpRequestMessage(HttpMethod.Get, uri),
                    urlValidator,
                    ct,
                    connectionUsesProxy: proxyMode != ProxyModes.Direct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var manifest = await RemoteHttpRequestService.DeserializeJsonAsync<RemoteManifest>(
                    response, requestUri, jsonOptions, ct).ConfigureAwait(false);
                return manifest ?? new RemoteManifest();
            },
            MaxManifestFetchAttempts,
            i => ManifestFetchBackoff[i],
            cancellationToken,
            ex => IsRetryableRequestFailure(ex) || ex is JsonException);
    }

    private async Task<T> GetEnvelopeDataAsync<T>(
        string path,
        string proxyMode,
        CancellationToken cancellationToken)
    {
        return await RetryPolicy.ExecuteWithRetryAsync(
            async ct =>
            {
                var sw = Stopwatch.StartNew();
                using var lease = await leaseSource.CreateLeaseAsync(proxyMode, ct).ConfigureAwait(false);
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                request.Headers.TryAddWithoutValidation(
                    "Authorization",
                    authorizationHeaderFactory.Create("", ApiConfig.YostarAuthorizationVersion));

                using var response = await lease.Client.SendAsync(request, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                LocalDiagnostics.LogSync(
                    LogEntrySeverity.Debug,
                    "ApiClient",
                    $"GET {path} -> {(int)response.StatusCode}, {sw.ElapsedMilliseconds}ms (attempt N/A)");

                var envelope = await RemoteHttpRequestService.DeserializeJsonAsync<LauncherApiEnvelope<T>>(response, request.RequestUri, jsonOptions, ct).ConfigureAwait(false);

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
            },
            MaxEnvelopeFetchAttempts,
            i => ManifestFetchBackoff[i],
            cancellationToken,
            ex => IsRetryableRequestFailure(ex)
                || ex is JsonException or InvalidOperationException);
    }

    private static bool IsRetryableRequestFailure(Exception exception)
    {
        if (exception is TaskCanceledException)
        {
            return true;
        }

        if (exception is not HttpRequestException { StatusCode: { } statusCode })
        {
            return exception is HttpRequestException;
        }

        return statusCode == System.Net.HttpStatusCode.RequestTimeout
            || statusCode == System.Net.HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;
    }

    public void Dispose()
    {
        leaseSource.Dispose();
        GC.SuppressFinalize(this);
    }
}
