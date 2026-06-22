using System.Net;
using System.Net.Http;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class RemoteHttpUrlValidatorTests
{
    [Theory]
    [InlineData("http://127.0.0.1/file")]
    [InlineData("http://10.0.0.1/file")]
    [InlineData("http://169.254.1.1/file")]
    [InlineData("http://192.168.1.1/file")]
    [InlineData("http://[::1]/file")]
    [InlineData("http://[fe80::1]/file")]
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
}
