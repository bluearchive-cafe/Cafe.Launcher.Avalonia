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

    /// <summary>
    /// <see cref="Exception.Data"/> 标记键：本次失败发生在直连出口，且目标主机在
    /// 失败前的 DNS 解析整体落在 Fake-IP 应答段（198.18/15、fc00::/7）。异常类型
    /// 保持不变——传输层的错误模式契约是"没有第二种异常类型"；表示层经由
    /// <see cref="HasFakeIpDnsMarker"/> 读出标记，把笼统的网络归因换成针对性的
    /// Fake-IP 指引。
    /// </summary>
    internal const string FakeIpDnsDataKey = "Cafe.Launcher.Avalonia.FakeIpDns";

    /// <summary>Checks the exception chain for the Fake-IP DNS failure marker.</summary>
    internal static bool HasFakeIpDnsMarker(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Data.Contains(FakeIpDnsDataKey))
            {
                return true;
            }
        }

        return false;
    }

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
            var egressesThroughProxy = EgressesThroughProxy(connectionProxy, currentUri);
            currentUri = await urlValidator
                .ValidateAsync(currentUri, egressesThroughProxy, cancellationToken)
                .ConfigureAwait(false);

            using var request = createRequest(currentUri);
            if (!IsSameAuthority(currentUri, initialUri))
            {
                // .NET 自带的 HttpClient 在跨主机重定向时会剥离 Authorization；
                // 手写循环保持同一约定——调用方经 ConfigureRequest 逐跳重建的
                // 凭据头只允许交给初始授权方，不跟随跨主机跳外泄（AUD-SEC-003）。
                request.Headers.Remove("Authorization");
            }
            HttpResponseMessage response;
            try
            {
                response = await SendAsync(
                        client,
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsAnnotatableNetworkFailure(exception, cancellationToken)
                && !egressesThroughProxy
                && urlValidator.IsFakeIpResolution(currentUri.IdnHost))
            {
                // 直连出口 + Fake-IP DNS 解析 + 连接失败：把可行动的根因钉在异常上
                // （TUN 接管的 fake-ip 能直拨成功，所以只在真失败时标注，不预判）。
                exception.Data[FakeIpDnsDataKey] = true;
                throw;
            }

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

    private static bool IsSameAuthority(Uri left, Uri right) =>
        left.Scheme == right.Scheme
        && left.Host == right.Host
        && left.Port == right.Port;

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
    /// 连接级失败家族：连接被拒、不可达以 <see cref="HttpRequestException"/>（无状态码）
    /// 浮出，拨号超时以 <see cref="TaskCanceledException"/> 浮出。调用方主动取消
    /// （<c>cancellationToken.IsCancellationRequested</c>）不是网络失败，不做标注。
    /// </summary>
    private static bool IsAnnotatableNetworkFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or TaskCanceledException
        && !cancellationToken.IsCancellationRequested;
}
