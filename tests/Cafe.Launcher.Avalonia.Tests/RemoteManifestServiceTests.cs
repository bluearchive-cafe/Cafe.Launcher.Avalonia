using System.Net;
using System.Net.Http;
using System.Text;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Auth;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class RemoteManifestServiceTests
{
    [Fact]
    public async Task GetRequiredManifestAsync_WhenUrlAndManifestAreValid_ReturnsManifest()
    {
        using var apiClient = CreateApiClient(new ManifestProtocolHandler(
            "https://manifest.example.invalid/latest.json",
            "{\"source\":\"packages\",\"file\":[]}"));
        var service = new RemoteManifestService(apiClient);

        var result = await service.GetRequiredManifestAsync(
            "1.0.0",
            "manifest.json",
            PatchUrlGroups.Official,
            ProxyModes.Direct);

        Assert.Equal("packages", result.Source);
    }

    [Fact]
    public async Task GetRequiredManifestAsync_WhenUrlIsEmpty_Throws()
    {
        using var apiClient = CreateApiClient(new ManifestProtocolHandler("", "{}"));
        var service = new RemoteManifestService(apiClient);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetRequiredManifestAsync(
                "1.0.0",
                "manifest.json",
                PatchUrlGroups.Official,
                ProxyModes.Direct));
    }

    [Fact]
    public async Task GetRequiredManifestAsync_WhenCafeManifestIsNotFound_FallsBackToOfficialHost()
    {
        using var handler = new CafeManifestNotFoundHandler();
        using var apiClient = CreateApiClient(handler);
        var service = new RemoteManifestService(apiClient);

        var result = await service.GetRequiredManifestAsync(
            "1.0.0",
            "manifest.json",
            PatchUrlGroups.Cafe,
            ProxyModes.Direct);

        Assert.Equal("official", result.Source);
        Assert.Equal(
            [
                "api-launcher-jp.yo-star.com",
                "launcher-pkg-ba-jp.bluearchive.cafe",
                "launcher-pkg-ba-jp.yo-star.com"
            ],
            handler.RequestHosts);
    }

    [Fact]
    public async Task GetOptionalManifestAsync_WhenUrlIsEmpty_ReturnsNull()
    {
        using var apiClient = CreateApiClient(new ManifestProtocolHandler("", "{}"));
        var service = new RemoteManifestService(apiClient);

        var result = await service.GetOptionalManifestAsync(
            "1.0.0",
            "manifest.json",
            PatchUrlGroups.Official,
            ProxyModes.Direct);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOptionalManifestAsync_WhenRequestFails_ReturnsNull()
    {
        using var apiClient = CreateApiClient(new ThrowingHandler());
        var service = new RemoteManifestService(apiClient);

        var result = await service.GetOptionalManifestAsync(
            "1.0.0",
            "manifest.json",
            PatchUrlGroups.Official,
            ProxyModes.Direct);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOptionalManifestAsync_WhenCanceled_PropagatesCancellation()
    {
        using var apiClient = CreateApiClient(new CancellationHandler());
        var service = new RemoteManifestService(apiClient);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetOptionalManifestAsync(
                "1.0.0",
                "manifest.json",
                PatchUrlGroups.Official,
                ProxyModes.Direct,
                cts.Token));
    }

    private static LauncherApiClient CreateApiClient(HttpMessageHandler handler) =>
        new(handler, new AuthorizationHeaderFactory(), new PatchUrlGroupService());

    private sealed class ManifestProtocolHandler(
        string manifestUrl,
        string manifestJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var isUrlRequest = request.RequestUri?.AbsolutePath.Contains(
                "/api/launcher/game/config/json",
                StringComparison.Ordinal) == true;
            var json = isUrlRequest
                ? $"{{\"code\":200,\"data\":{{\"url\":\"{manifestUrl}\"}}}}"
                : manifestJson;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("network failure");
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken);
    }

    private sealed class CafeManifestNotFoundHandler : HttpMessageHandler
    {
        public List<string> RequestHosts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var host = request.RequestUri?.Host ?? "";
            RequestHosts.Add(host);

            if (request.RequestUri?.AbsolutePath.Contains(
                    "/api/launcher/game/config/json",
                    StringComparison.Ordinal) == true)
            {
                return Task.FromResult(CreateJsonResponse(
                    "{\"code\":200,\"data\":{\"url\":\"https://launcher-pkg-ba-jp.yo-star.com/zip_online_config_json/test.json\"}}"));
            }

            if (host == "launcher-pkg-ba-jp.bluearchive.cafe")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(CreateJsonResponse("{\"source\":\"official\",\"file\":[]}"));
        }

        private static HttpResponseMessage CreateJsonResponse(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }
}
