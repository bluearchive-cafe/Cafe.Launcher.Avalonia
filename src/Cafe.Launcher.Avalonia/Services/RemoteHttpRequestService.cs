using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// The manual-redirect sender shared by <see cref="RemoteHttpTransport"/> and the
/// game-file download path: per-hop URL revalidation, egress-aware SSRF skipping,
/// redirect limits and HTTPS→HTTP downgrade rejection. JSON buffering lives in the
/// transport — this type no longer owns response-body handling.
/// </summary>
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
}
