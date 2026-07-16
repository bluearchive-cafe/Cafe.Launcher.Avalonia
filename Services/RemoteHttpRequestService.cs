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
        bool connectionUsesProxy = false)
    {
        var currentUri = initialUri;
        for (var redirectCount = 0; ; redirectCount++)
        {
            currentUri = await urlValidator
                .ValidateAsync(currentUri, connectionUsesProxy, cancellationToken)
                .ConfigureAwait(false);

            using var request = createRequest(currentUri);
            var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
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

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Moved
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

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
    public static async Task<T?> DeserializeJsonAsync<T>(
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
}
