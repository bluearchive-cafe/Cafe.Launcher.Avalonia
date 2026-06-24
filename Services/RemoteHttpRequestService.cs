using System;
using System.Net;
using System.Net.Http;
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
}
