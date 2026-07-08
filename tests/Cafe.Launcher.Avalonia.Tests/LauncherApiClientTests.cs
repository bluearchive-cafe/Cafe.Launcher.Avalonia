using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Auth;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LauncherApiClientTests
{
    [Fact]
    public async Task GetBaseConfigAsync_WhenBackgroundImageIsPackageRelative_ReturnsAbsoluteUrl()
    {
        const string responseJson =
            """
            {
              "code": 200,
              "data": {
                "launcher_background_img": "/prod/BlueArchive_JP/launcher_background_img/82f20f8436deddb6bcdceddfa3b1955b.jpg",
                "launcher_background_img_crc64": "3978501611865773179"
              }
            }
            """;
        using var handler = new JsonResponseHandler(responseJson);
        using var client = new LauncherApiClient(
            handler,
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());

        var result = await client.GetBaseConfigAsync(ProxyModes.Direct);

        Assert.Equal(
            "https://launcher-pkg-ba-jp.yo-star.com/prod/BlueArchive_JP/launcher_background_img/82f20f8436deddb6bcdceddfa3b1955b.jpg",
            result.LauncherBackgroundImg);
    }

    [Fact]
    public void RewriteManifestUrl_WhenCafe_RewritesPackageHost()
    {
        using var client = new LauncherApiClient(new HttpClientHandler(), new AuthorizationHeaderFactory(), new PatchUrlGroupService());
        var response = new ManifestUrlResponse
        {
            Url = "https://launcher-pkg-ba-jp.yo-star.com/zip_online_config_json/test.json"
        };

        var result = client.RewriteManifestUrl(response, PatchUrlGroups.Cafe);

        Assert.Equal("https://launcher-pkg-ba-jp.bluearchive.cafe/zip_online_config_json/test.json", result.Url);
    }

    [Fact]
    public void RewriteCdnConfig_WhenCafe_RewritesPackageHosts()
    {
        using var client = new LauncherApiClient(new HttpClientHandler(), new AuthorizationHeaderFactory(), new PatchUrlGroupService());
        var response = new CdnConfigResponse
        {
            PrimaryCdn = "https://launcher-pkg-ba-jp.yo-star.com",
            BackUpCdn = "https://launcher-pkg-ba-jp.yo-star.com/backup"
        };

        var result = client.RewriteCdnConfig(response, PatchUrlGroups.Cafe);

        Assert.Equal("https://launcher-pkg-ba-jp.bluearchive.cafe", result.PrimaryCdn);
        Assert.Equal("https://launcher-pkg-ba-jp.bluearchive.cafe/backup", result.BackUpCdn);
    }

    [Fact]
    public async Task GetBaseConfigAsync_WhenResponseIsNotValidJson_ThrowsJsonExceptionWithContext()
    {
        var bytes = new byte[] { 0x8B, 0x0B, 0x00, 0x01, 0x41, 0x42, 0x43, 0x44 };
        using var handler = new BinaryResponseHandler(bytes, "application/octet-stream");
        using var client = new LauncherApiClient(
            handler,
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());

        var ex = await Assert.ThrowsAsync<JsonException>(() => client.GetBaseConfigAsync(ProxyModes.Direct));

        Assert.Contains("not valid JSON", ex.Message);
        Assert.Contains("status: 200", ex.Message);
        Assert.Contains("content-type: application/octet-stream", ex.Message);
        Assert.Contains("first-bytes: 8B0B000141424344", ex.Message);
    }

    [Fact]
    public async Task GetBaseConfigAsync_WhenResponseLooksGzip_ThrowsJsonExceptionMentioningGzip()
    {
        var bytes = new byte[] { 0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00 };
        using var handler = new BinaryResponseHandler(bytes, "application/json");
        using var client = new LauncherApiClient(
            handler,
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());

        var ex = await Assert.ThrowsAsync<JsonException>(() => client.GetBaseConfigAsync(ProxyModes.Direct));

        Assert.Contains("gzip", ex.Message);
    }

    private sealed class JsonResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private sealed class BinaryResponseHandler(byte[] bytes, string mediaType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue(mediaType) }
                }
            });
    }
}
