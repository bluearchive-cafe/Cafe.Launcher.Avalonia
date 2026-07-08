using System;
using System.Globalization;
using System.IO;
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

    internal CdnConfigResponse RewriteCdnConfig(CdnConfigResponse response, string patchUrlGroup)
    {
        return patchUrlGroupService.RewriteCdnConfig(response, patchUrlGroup);
    }

    public async Task<RemoteManifest> GetRemoteManifestAsync(
        string url,
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        var requestUri = new Uri(url);
        using var lease = await leaseSource.CreateLeaseAsync(proxyMode, cancellationToken).ConfigureAwait(false);
        using var response = await RemoteHttpRequestService.SendAsync(
            lease.Client,
            requestUri,
            static uri => new HttpRequestMessage(HttpMethod.Get, uri),
            urlValidator,
            cancellationToken,
            connectionUsesProxy: proxyMode == ProxyModes.System).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var manifest = await DeserializeRemoteJsonAsync<RemoteManifest>(response, requestUri, jsonOptions, cancellationToken).ConfigureAwait(false);
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

        var envelope = await DeserializeRemoteJsonAsync<LauncherApiEnvelope<T>>(response, request.RequestUri, jsonOptions, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// Buffers a remote HTTP response body and deserializes it as JSON. When
    /// the body is not valid JSON (a CDN error page, compressed bytes served
    /// with a 200 status and no <c>Content-Encoding</c>, or a binary blob),
    /// throws a <see cref="JsonException"/> carrying the request URL, status
    /// code, content type and a hex/ASCII preview of the first bytes so the
    /// failure is actionable in logs. This replaces the opaque
    /// <c>ExpectedStartOfValueNotFound, 0x8B</c> message that the strict
    /// <see cref="Utf8JsonReader"/> emits on the first invalid byte, which
    /// carries no request context. Manifests and API envelopes are small
    /// metadata payloads, so buffering into memory is safe.
    /// </summary>
    private static async Task<T?> DeserializeRemoteJsonAsync<T>(
        HttpResponseMessage response,
        Uri? requestUri,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        await using var networkStream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await networkStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;

        try
        {
            return await JsonSerializer
                .DeserializeAsync<T>(buffer, options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw BuildRemoteJsonException(requestUri, response, buffer, ex);
        }
    }

    private static JsonException BuildRemoteJsonException(
        Uri? requestUri,
        HttpResponseMessage response,
        MemoryStream buffer,
        JsonException inner)
    {
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "(none)";
        var contentLength = response.Content.Headers.ContentLength;
        var snapshotLength = (int)Math.Min(buffer.Length, 16);
        var hex = snapshotLength > 0
            ? Convert.ToHexString(buffer.GetBuffer(), 0, snapshotLength)
            : "(empty)";
        var preview = BuildAsciiPreview(new ReadOnlySpan<byte>(buffer.GetBuffer(), 0, snapshotLength));
        var encodingHint = DetectCompression(new ReadOnlySpan<byte>(buffer.GetBuffer(), 0, snapshotLength));
        var invariant = CultureInfo.InvariantCulture;

        var message =
            $"Remote response is not valid JSON ({inner.Message}). "
            + $"url: {requestUri?.ToString() ?? "(unknown)"} | "
            + $"status: {((int)response.StatusCode).ToString(invariant)} {response.ReasonPhrase} | "
            + $"content-type: {contentType} | "
            + $"content-length: {(contentLength.HasValue ? contentLength.Value.ToString(invariant) : "unknown")} | "
            + $"actual-bytes: {buffer.Length.ToString(invariant)} | "
            + $"first-bytes: {hex} | "
            + $"preview: {preview}{encodingHint}";

        return new JsonException(message, inner);
    }

    private static string BuildAsciiPreview(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return "(empty)";
        }

        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            chars[i] = b >= 0x20 && b < 0x7F ? (char)b : '.';
        }

        return new string(chars);
    }

    private static string DetectCompression(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
        {
            return " [looks gzip-compressed; Content-Encoding was not decompressed]";
        }

        if (bytes.Length >= 2 && bytes[0] == 0x78 && (bytes[1] == 0x9C || bytes[1] == 0x01 || bytes[1] == 0xDA))
        {
            return " [looks zlib/deflate-compressed]";
        }

        if (bytes.Length >= 4 && bytes[0] == 0x28 && bytes[1] == 0xB5 && bytes[2] == 0x2F && bytes[3] == 0xFD)
        {
            return " [looks zstd-compressed]";
        }

        if (bytes.Length >= 3 && bytes[0] == 0x42 && bytes[1] == 0x5A && bytes[2] == 0x68)
        {
            return " [looks bzip2-compressed]";
        }

        return "";
    }

    public void Dispose()
    {
        leaseSource.Dispose();
        GC.SuppressFinalize(this);
    }
}
