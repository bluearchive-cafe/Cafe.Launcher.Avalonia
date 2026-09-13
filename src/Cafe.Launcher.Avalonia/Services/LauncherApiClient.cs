using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Auth;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Client for the Yostar launcher API (envelope endpoints) and remote manifests.
/// Transport concerns — proxy-aware leasing, SSRF validation, redirects, status
/// enforcement, buffering and retries — live in <see cref="IRemoteHttpTransport"/>;
/// this class owns only the API vocabulary: request paths, the time-signed
/// Authorization header (rebuilt per attempt and per redirect hop by the
/// transport's <see cref="RemoteRequestOptions.ConfigureRequest"/>), envelope
/// business codes, and manifest URL rewriting.
/// </summary>
public sealed class LauncherApiClient
{
    private readonly IRemoteHttpTransport transport;
    private readonly AuthorizationHeaderFactory authorizationHeaderFactory;
    private readonly PatchUrlGroupService patchUrlGroupService;
    private readonly JsonSerializerOptions jsonOptions = JsonDefaults.Strict;

    /// <summary>
    /// Maximum number of attempts for transient manifest/envelope fetch failures
    /// (initial attempt + retries). Backoff: 500ms, 1000ms; scope
    /// <see cref="RemoteRetryScope.Transient"/>. Envelope business codes are
    /// authoritative and never retried. Mirrors the bounded retry philosophy of
    /// <see cref="FileDownloadService.RetryDomainOrder"/> but with fewer
    /// attempts — manifests are small metadata payloads, not large file downloads.
    /// </summary>
    private const int MaxFetchAttempts = 3;
    private static readonly TimeSpan[] FetchBackoff =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1000)
    ];

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public LauncherApiClient(
        IRemoteHttpTransport transport,
        AuthorizationHeaderFactory authorizationHeaderFactory,
        PatchUrlGroupService patchUrlGroupService)
    {
        this.transport = transport;
        this.authorizationHeaderFactory = authorizationHeaderFactory;
        this.patchUrlGroupService = patchUrlGroupService;
    }

    public Task<GameConfigResponse> GetGameConfigAsync(
        CancellationToken cancellationToken = default)
    {
        return GetEnvelopeDataAsync<GameConfigResponse>(
            "/api/launcher/game/config",
            cancellationToken);
    }

    public async Task<BaseConfigResponse> GetBaseConfigAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await GetEnvelopeDataAsync<BaseConfigResponse>(
            "/api/launcher/base/config",
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
        CancellationToken cancellationToken)
    {
        return GetEnvelopeDataAsync<CdnConfigResponse>(
            "/api/launcher/advanced/game/download/cdn",
            cancellationToken);
    }

    public async Task<CdnConfigResponse> GetCdnConfigAsync(
        string patchUrlGroup,
        CancellationToken cancellationToken = default)
    {
        var response = await GetCdnConfigAsync(cancellationToken).ConfigureAwait(false);
        return RewriteCdnConfig(response, patchUrlGroup);
    }

    public Task<OperationsResourceResponse> GetOperationsResourceAsync(
        CancellationToken cancellationToken = default)
    {
        return GetEnvelopeDataAsync<OperationsResourceResponse>(
            "/api/launcher/operations/resource",
            cancellationToken);
    }

    public Task<SocialMediaResourceResponse> GetSocialMediaResourceAsync(
        CancellationToken cancellationToken = default)
    {
        return GetEnvelopeDataAsync<SocialMediaResourceResponse>(
            "/api/launcher/social/media/resource",
            cancellationToken);
    }

    public Task<InstallationConfigResponse> GetInstallationConfigAsync(
        CancellationToken cancellationToken = default)
    {
        return GetEnvelopeDataAsync<InstallationConfigResponse>(
            "/api/launcher/installation/config",
            cancellationToken);
    }

    private Task<ManifestUrlResponse> GetManifestUrlAsync(
        string version,
        string filePath,
        CancellationToken cancellationToken)
    {
        var requestPath = $"/api/launcher/game/config/json?version={Uri.EscapeDataString(version)}&file_path={Uri.EscapeDataString(filePath)}";
        return GetEnvelopeDataAsync<ManifestUrlResponse>(
            requestPath,
            cancellationToken);
    }

    public async Task<ManifestUrlResponse> GetManifestUrlAsync(
        string version,
        string filePath,
        string patchUrlGroup,
        CancellationToken cancellationToken = default)
    {
        var response = await GetManifestUrlAsync(
            version,
            filePath,
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

    public async Task<RemoteManifest> GetRemoteManifestAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        var requestUri = new Uri(url);
        var manifest = await transport.GetJsonAsync<RemoteManifest>(
            requestUri,
            new RemoteRequestOptions
            {
                MaxAttempts = MaxFetchAttempts,
                Backoff = i => FetchBackoff[i],
                RetryScope = RemoteRetryScope.Transient,
                Timeout = RequestTimeout
            },
            cancellationToken).ConfigureAwait(false);
        return manifest ?? new RemoteManifest();
    }

    private async Task<T> GetEnvelopeDataAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        // ApiConfig.ApiBaseUrl ends with '/' and the path starts with '/', so
        // Uri-relative resolution replaces the base path instead of concatenating.
        var requestUri = new Uri(new Uri(ApiConfig.ApiBaseUrl), path);
        var stopwatch = Stopwatch.StartNew();
        var envelope = await transport.GetJsonAsync<LauncherApiEnvelope<T>>(
            requestUri,
            new RemoteRequestOptions
            {
                MaxAttempts = MaxFetchAttempts,
                Backoff = i => FetchBackoff[i],
                RetryScope = RemoteRetryScope.Transient,
                Timeout = RequestTimeout,
                ConfigureRequest = request => request.Headers.TryAddWithoutValidation(
                    "Authorization",
                    authorizationHeaderFactory.Create("", ApiConfig.YostarAuthorizationVersion)),
                Json = jsonOptions
            },
            cancellationToken).ConfigureAwait(false);
        await LocalDiagnostics.LogAsync(
            LogEntrySeverity.Debug,
            "ApiClient",
            $"GET {path} -> {stopwatch.ElapsedMilliseconds}ms").ConfigureAwait(false);

        if (envelope is null)
        {
            // 行为精炼：重试归传输层后，空体不再参与重试预算——它只会以
            // InvalidOperationException 终结一次调用；业务码异常照旧不重试。
            throw new InvalidOperationException("API response body is empty.");
        }

        if (envelope.Code != 200)
        {
            var message = envelope.Message ?? envelope.Msg ?? $"API response code: {envelope.Code}";
            throw new LauncherApiEnvelopeException(message);
        }

        if (envelope.Data is null)
        {
            throw new InvalidOperationException("API response data is empty.");
        }

        return envelope.Data;
    }
}

/// <summary>
/// The API envelope answered with a non-200 business code (e.g. a server-side
/// rejection such as maintenance or a rate limit). The server responded, so the
/// failure is authoritative rather than transient — the transport's retry
/// scope deliberately does not match it. Derives from
/// <see cref="InvalidOperationException"/> so existing catch sites that treat
/// envelope failures as protocol errors keep working unchanged.
/// </summary>
internal sealed class LauncherApiEnvelopeException : InvalidOperationException
{
    public LauncherApiEnvelopeException(string message) : base(message)
    {
    }
}
