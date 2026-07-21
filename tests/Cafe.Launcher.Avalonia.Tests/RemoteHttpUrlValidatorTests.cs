using System.Net;
using System.Net.Http;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class RemoteHttpUrlValidatorTests
{
    [Theory]
    [InlineData("file")]
    [InlineData("/relative/path")]
    public async Task ValidateAsync_WhenUrlIsNotAbsolute_Throws(string url)
    {
        var validator = RemoteHttpUrlValidator.CreateForTesting();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync(url));
    }

    [Theory]
    [InlineData("ftp://example.test/file")]
    [InlineData("file:///C:/temp/file.bin")]
    public async Task ValidateAsync_WhenSchemeIsNotHttpOrHttps_Throws(string url)
    {
        var validator = RemoteHttpUrlValidator.CreateForTesting();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync(url));
    }

    [Fact]
    public async Task ValidateAsync_WhenUrlContainsUserInfo_Throws()
    {
        var validator = RemoteHttpUrlValidator.CreateForTesting();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync("https://user:pass@example.test/file"));
    }

    [Theory]
    [InlineData("http://example.test:81/file")]
    [InlineData("https://example.test:444/file")]
    public async Task ValidateAsync_WhenPortIsNotDefaultHttpOrHttps_Throws(string url)
    {
        var validator = RemoteHttpUrlValidator.CreateForTesting();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync(url));
    }

    [Fact]
    public async Task ValidateAsync_WhenHostEndsWithDotLocalhost_Throws()
    {
        var validator = RemoteHttpUrlValidator.CreateForTesting();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync("https://service.localhost/file"));
    }

    [Theory]
    [InlineData("http://127.0.0.1/file")]
    [InlineData("http://10.0.0.1/file")]
    [InlineData("http://169.254.1.1/file")]
    [InlineData("http://172.16.0.1/file")]
    [InlineData("http://192.0.0.1/file")]
    [InlineData("http://192.168.1.1/file")]
    [InlineData("http://224.0.0.1/file")]
    [InlineData("http://[::1]/file")]
    [InlineData("http://[::]/file")]
    [InlineData("http://[fe80::1]/file")]
    [InlineData("http://[fc00::1]/file")]
    [InlineData("http://[2001:db8::1]/file")]
    [InlineData("http://[::ffff:127.0.0.1]/file")]
    [InlineData("http://localhost/file")]
    public async Task ValidateAsync_WhenTargetIsLocalOrPrivate_Throws(string url)
    {
        var validator = RemoteHttpUrlValidator.CreateForTesting();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync(url));
    }

    [Fact]
    public async Task ValidateAsync_WhenDnsContainsPrivateAddress_Throws()
    {
        var validator = new RemoteHttpUrlValidator(
            static (_, _) => Task.FromResult<IPAddress[]>(
                [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("192.168.1.1")]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync("https://example.test/image.png"));
    }

    [Fact]
    public async Task ValidateAsync_WhenDnsReturnsNoAddresses_Throws()
    {
        var validator = new RemoteHttpUrlValidator(
            static (_, _) => Task.FromResult(Array.Empty<IPAddress>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync("https://example.test/image.png"));
    }

    [Fact]
    public async Task ValidateAsync_WhenLiteralAddressIsPublic_ReturnsUri()
    {
        var validator = RemoteHttpUrlValidator.CreateForTesting();

        var uri = await validator.ValidateAsync("https://93.184.216.34/file");

        Assert.Equal("93.184.216.34", uri.Host);
    }

    [Fact]
    public async Task ValidateAsync_WhenLiteralAddressIsCarrierGradeNat_ReturnsUri()
    {
        // RFC 6598 100.64.0.0/10 is ISP-side Shared Address Space.
        // CDN edge nodes commonly use addresses in this range.
        var validator = RemoteHttpUrlValidator.CreateForTesting();

        var uri = await validator.ValidateAsync("https://100.64.0.1/file");

        Assert.Equal("100.64.0.1", uri.Host);
    }

    [Fact]
    public async Task ValidateAsync_WhenConnectionUsesProxy_BypassesLocalDnsResolution()
    {
        // Local DNS for the target host is blocked/poisoned (would resolve to a private
        // address or fail). A proxy connection must not depend on local resolution.
        var validator = new RemoteHttpUrlValidator(
            static (_, _) => throw new InvalidOperationException(
                "Local DNS must not be resolved when the connection egresses through a proxy."));

        var uri = await validator.ValidateAsync(
            new Uri("https://api-launcher-jp.yo-star.com/path"),
            connectionUsesProxy: true);

        Assert.Equal("api-launcher-jp.yo-star.com", uri.Host);
    }

    [Theory]
    [InlineData("http://127.0.0.1/file")]
    [InlineData("http://10.0.0.1/file")]
    [InlineData("http://192.168.1.1/file")]
    [InlineData("http://localhost/file")]
    public async Task ValidateAsync_WhenConnectionUsesProxyAndHostIsLiteralLocalOrPrivate_StillThrows(string url)
    {
        var validator = new RemoteHttpUrlValidator(
            static (_, _) => throw new InvalidOperationException("DNS must not be resolved."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync(new Uri(url), connectionUsesProxy: true));
    }

    [Fact]
    public async Task SendAsync_WhenRedirectTargetsLocalhost_BlocksBeforeSecondRequest()
    {
        var handler = new RedirectHandler();
        using var client = new HttpClient(handler);
        var validator = RemoteHttpUrlValidator.CreateForTesting();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RemoteHttpRequestService.SendAsync(
                client,
                new Uri("http://example.test/start"),
                static uri => new HttpRequestMessage(HttpMethod.Get, uri),
                validator,
                CancellationToken.None));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SendAsync_WhenConnectionUsesProxy_SkipsLocalDnsResolution()
    {
        using var client = new HttpClient(new OkHandler());
        var validator = new RemoteHttpUrlValidator(
            static (_, _) => throw new InvalidOperationException("DNS must not be resolved."));

        using var response = await RemoteHttpRequestService.SendAsync(
            client,
            new Uri("https://example.test/start"),
            static uri => new HttpRequestMessage(HttpMethod.Get, uri),
            validator,
            CancellationToken.None,
            connectionUsesProxy: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_WhenResponseIsNotRedirect_ReturnsFirstResponse()
    {
        var handler = new OkHandler();
        using var client = new HttpClient(handler);

        using var response = await RemoteHttpRequestService.SendAsync(
            client,
            new Uri("https://example.test/start"),
            static uri => new HttpRequestMessage(HttpMethod.Get, uri),
            RemoteHttpUrlValidator.CreateForTesting(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SendAsync_WhenRedirectIsRelative_FollowsRedirect()
    {
        var handler = new RelativeRedirectHandler();
        using var client = new HttpClient(handler);

        using var response = await RemoteHttpRequestService.SendAsync(
            client,
            new Uri("https://example.test/start"),
            static uri => new HttpRequestMessage(HttpMethod.Get, uri),
            RemoteHttpUrlValidator.CreateForTesting(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["https://example.test/start", "https://example.test/final"],
            handler.RequestUris);
    }

    [Fact]
    public async Task SendAsync_WhenRedirectHasNoLocation_Throws()
    {
        using var client = new HttpClient(new MissingLocationHandler());

        await Assert.ThrowsAsync<HttpRequestException>(
            () => RemoteHttpRequestService.SendAsync(
                client,
                new Uri("https://example.test/start"),
                static uri => new HttpRequestMessage(HttpMethod.Get, uri),
                RemoteHttpUrlValidator.CreateForTesting(),
                CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_WhenRedirectDowngradesHttpsToHttp_Throws()
    {
        using var client = new HttpClient(new DowngradeRedirectHandler());

        await Assert.ThrowsAsync<HttpRequestException>(
            () => RemoteHttpRequestService.SendAsync(
                client,
                new Uri("https://example.test/start"),
                static uri => new HttpRequestMessage(HttpMethod.Get, uri),
                RemoteHttpUrlValidator.CreateForTesting(),
                CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_WhenRedirectLimitIsExceeded_ThrowsAfterSixRequests()
    {
        var handler = new EndlessRedirectHandler();
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => RemoteHttpRequestService.SendAsync(
                client,
                new Uri("https://example.test/start"),
                static uri => new HttpRequestMessage(HttpMethod.Get, uri),
                RemoteHttpUrlValidator.CreateForTesting(),
                CancellationToken.None));

        Assert.Equal(6, handler.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Moved)]
    [InlineData(HttpStatusCode.RedirectMethod)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task SendAsync_WhenRedirectUsesSupportedStatusCode_FollowsRedirect(HttpStatusCode statusCode)
    {
        var handler = new SingleRedirectHandler(statusCode);
        using var client = new HttpClient(handler);

        using var response = await RemoteHttpRequestService.SendAsync(
            client,
            new Uri("https://example.test/start"),
            static uri => new HttpRequestMessage(HttpMethod.Get, uri),
            RemoteHttpUrlValidator.CreateForTesting(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["https://example.test/start", "https://example.test/final"],
            handler.RequestUris);
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers =
                {
                    Location = new Uri("http://localhost/private")
                }
            });
        }
    }

    private sealed class RelativeRedirectHandler : HttpMessageHandler
    {
        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri?.AbsoluteUri ?? "");
            return Task.FromResult(RequestUris.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("/final", UriKind.Relative) }
                }
                : new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class SingleRedirectHandler(HttpStatusCode redirectStatusCode) : HttpMessageHandler
    {
        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri?.AbsoluteUri ?? "");
            return Task.FromResult(RequestUris.Count == 1
                ? new HttpResponseMessage(redirectStatusCode)
                {
                    Headers = { Location = new Uri("/final", UriKind.Relative) }
                }
                : new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class MissingLocationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect));
    }

    private sealed class DowngradeRedirectHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers =
                {
                    Location = new Uri("http://example.test/final")
                }
            });
    }

    private sealed class EndlessRedirectHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers =
                {
                    Location = new Uri($"/redirect-{RequestCount}", UriKind.Relative)
                }
            });
        }
    }
}
