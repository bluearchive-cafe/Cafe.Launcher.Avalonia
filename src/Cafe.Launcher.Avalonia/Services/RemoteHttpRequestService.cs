using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

internal static class RemoteHttpRequestService
{
    private const int MaxRedirects = 5;

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        Uri initialUri,
        Func<Uri, HttpRequestMessage> createRequest,
        RemoteHttpUrlValidator urlValidator,
        CancellationToken cancellationToken,
        IWebProxy? connectionProxy = null)
    {
        var currentUri = initialUri;
        for (var redirectCount = 0; ; redirectCount++)
        {
            currentUri = await urlValidator
                .ValidateAsync(currentUri, EgressesThroughProxy(connectionProxy, currentUri), cancellationToken)
                .ConfigureAwait(false);

            using var request = createRequest(currentUri);
            var response = await SendAsync(
                    client,
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!IsRedirect(response.StatusCode))
            {
                return response;
            }

            if (redirectCount >= MaxRedirects)
            {
                response.Dispose();
                throw new HttpRequestException("Remote request exceeded the redirect limit.");
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
            {
                throw new HttpRequestException("Remote redirect response has no Location header.");
            }

            var nextUri = location.IsAbsoluteUri
                ? location
                : new Uri(currentUri, location);
            if (currentUri.Scheme == Uri.UriSchemeHttps
                && nextUri.Scheme == Uri.UriSchemeHttp)
            {
                throw new HttpRequestException("Remote redirect attempted to downgrade HTTPS to HTTP.");
            }

            currentUri = nextUri;
        }
    }

    /// <summary>
    /// Sends a manually-created request using the client's configured HTTP version preference.
    /// <see cref="HttpClient.DefaultRequestVersion"/> is not automatically copied to an
    /// independently-created <see cref="HttpRequestMessage"/>.
    /// </summary>
    public static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        SendAsync(client, request, HttpCompletionOption.ResponseContentRead, cancellationToken);

    /// <inheritdoc cref="SendAsync(HttpClient, HttpRequestMessage, CancellationToken)"/>
    public static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        request.Version = client.DefaultRequestVersion;
        request.VersionPolicy = client.DefaultVersionPolicy;
        return client.SendAsync(request, completionOption, cancellationToken);
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Moved
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// Decides whether a request to <paramref name="uri"/> actually egresses through
    /// <paramref name="proxy"/>. A proxy-mode lease degrades to a direct connection when
    /// the system proxy bypasses the target (<see cref="IWebProxy.IsBypassed"/>) or
    /// reports the target itself as the route (<see cref="IWebProxy.GetProxy"/> returning
    /// the input URI); in that state the connection dials locally, so the URL validator's
    /// local DNS resolution must stay active. The decision is therefore made per URI from
    /// the effective proxy instead of from the proxy settings enum alone.
    /// </summary>
    internal static bool EgressesThroughProxy(IWebProxy? proxy, Uri uri)
    {
        if (proxy is null || proxy.IsBypassed(uri))
        {
            return false;
        }

        return proxy.GetProxy(uri) is { } via && !via.Equals(uri);
    }

    /// <summary>
    /// Upper bound for buffered JSON responses. Manifests and API envelopes are
    /// small metadata payloads (a manifest with tens of thousands of entries
    /// stays in the low-megabyte range), so this limit is generous while still
    /// preventing an errant remote payload — a CDN error page or a large binary
    /// blob served with a 200 status — from exhausting memory during startup.
    /// </summary>
    internal const int MaxBufferedJsonBytes = 64 * 1024 * 1024;

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
    public static Task<T?> DeserializeJsonAsync<T>(
        HttpResponseMessage response,
        Uri? requestUri,
        JsonSerializerOptions options,
        CancellationToken cancellationToken) =>
        DeserializeJsonAsync<T>(response, requestUri, options, MaxBufferedJsonBytes, cancellationToken);

    internal static async Task<T?> DeserializeJsonAsync<T>(
        HttpResponseMessage response,
        Uri? requestUri,
        JsonSerializerOptions options,
        int maxBytes,
        CancellationToken cancellationToken,
        TimeSpan? idleReadTimeout = null)
    {
        // Reject via the declared length when present; the streaming guard below
        // still bounds responses without a Content-Length (chunked transfer).
        if (response.Content.Headers.ContentLength is { } contentLength && contentLength > maxBytes)
        {
            throw BuildResponseTooLargeException(requestUri, response, contentLength, contentLength);
        }

        await using var networkStream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await ResponseBodyReader
                .ReadAsync(networkStream, chunk, cancellationToken, idleReadTimeout)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > maxBytes)
            {
                throw BuildResponseTooLargeException(
                    requestUri,
                    response,
                    buffer.Length + read,
                    declaredBytes: null);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
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

    /// <summary>
    /// Renders a request URI for a diagnostic message without its query string. Query values can
    /// carry credentials the endpoint accepts on their own — the resource panel's <c>uid</c> is one —
    /// and diagnostics are written to the log that the export feature always bundles, so the
    /// redaction happens here rather than at each call site.
    /// </summary>
    private static string DescribeUri(Uri? requestUri) =>
        requestUri?.GetLeftPart(UriPartial.Path) ?? "(unknown)";

    private static HttpRequestException BuildResponseTooLargeException(
        Uri? requestUri,
        HttpResponseMessage response,
        long actualBytes,
        long? declaredBytes)
    {
        var invariant = CultureInfo.InvariantCulture;
        var declared = declaredBytes.HasValue
            ? declaredBytes.Value.ToString(invariant)
            : "unknown";
        return new HttpRequestException(
            $"Remote response exceeds the {MaxBufferedJsonBytes.ToString(invariant)}-byte limit "
            + $"(declared: {declared}, buffered: {actualBytes.ToString(invariant)}). "
            + $"url: {DescribeUri(requestUri)} | "
            + $"status: {((int)response.StatusCode).ToString(invariant)} {response.ReasonPhrase} | "
            + $"content-type: {response.Content.Headers.ContentType?.ToString() ?? "(none)"}");
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
            + $"url: {DescribeUri(requestUri)} | "
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
}
