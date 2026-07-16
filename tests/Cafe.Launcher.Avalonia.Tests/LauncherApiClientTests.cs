using System.Net.Http;
using System.Net;
using System.Text;
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
}
