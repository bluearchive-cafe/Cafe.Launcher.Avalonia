using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Auth;
using Cafe.Launcher.Avalonia.Testing;

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
        var transport = new StubRemoteHttpTransport(_ => responseJson);
        var client = new LauncherApiClient(
            transport,
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());

        var result = await client.GetBaseConfigAsync();

        Assert.Equal(
            "https://launcher-pkg-ba-jp.yo-star.com/prod/BlueArchive_JP/launcher_background_img/82f20f8436deddb6bcdceddfa3b1955b.jpg",
            result.LauncherBackgroundImg);
    }

    [Fact]
    public void RewriteManifestUrl_WhenCafe_RewritesPackageHost()
    {
        var client = new LauncherApiClient(new StubRemoteHttpTransport(), new AuthorizationHeaderFactory(), new PatchUrlGroupService());
        var response = new ManifestUrlResponse
        {
            Url = "https://launcher-pkg-ba-jp.yo-star.com/zip_online_config_json/test.json"
        };

        var result = client.RewriteManifestUrl(response, PatchUrlGroups.Cafe);

        Assert.Equal("https://launcher-pkg-ba-jp.bluearchive.cafe/zip_online_config_json/test.json", result.Url);
    }

    [Fact]
    public void RewriteCdnConfig_WhenCafe_RewritesPrimaryAndUsesPrimaryForBackup()
    {
        var client = new LauncherApiClient(new StubRemoteHttpTransport(), new AuthorizationHeaderFactory(), new PatchUrlGroupService());
        var response = new CdnConfigResponse
        {
            PrimaryCdn = "https://launcher-pkg-ba-jp.yo-star.com",
            BackUpCdn = "https://launcher-pkg-ba-jp.yo-star.com/backup"
        };

        var result = client.RewriteCdnConfig(response, PatchUrlGroups.Cafe);

        Assert.Equal("https://launcher-pkg-ba-jp.bluearchive.cafe", result.PrimaryCdn);
        Assert.Equal(result.PrimaryCdn, result.BackUpCdn);
    }

    [Fact]
    public async Task GetBaseConfigAsync_WhenEnvelopeCodeIsNot200_FailsFastWithoutRetry()
    {
        var transport = new StubRemoteHttpTransport(
            _ => """{"code":503,"data":{"launcher_background_img":null},"message":"service under maintenance"}""");
        var client = new LauncherApiClient(
            transport,
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());

        var ex = await Assert.ThrowsAsync<LauncherApiEnvelopeException>(
            () => client.GetBaseConfigAsync());

        Assert.Single(transport.RequestedUris);
        Assert.Contains("service under maintenance", ex.Message);
    }

    [Fact]
    public async Task GetBaseConfigAsync_WhenEnvelopeDataIsMissing_ThrowsInvalidOperationExceptionAfterSingleRequest()
    {
        // 行为精炼：重试归传输层后，空 envelope 数据由客户端在首次请求后即以
        // InvalidOperationException 终结，不再进入客户端重试预算。
        var transport = new StubRemoteHttpTransport(_ => """{"code":200,"data":null}""");
        var client = new LauncherApiClient(
            transport,
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetBaseConfigAsync());

        Assert.Single(transport.RequestedUris);
    }

    [Fact]
    public async Task GetRemoteManifestAsync_WhenBodyIsEmpty_ReturnsEmptyManifestFallback()
    {
        var transport = new StubRemoteHttpTransport(_ => null);
        var client = new LauncherApiClient(
            transport,
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());

        var manifest = await client.GetRemoteManifestAsync("https://example.com/manifest.json");

        Assert.Null(manifest.Source);
        Assert.Empty(manifest.File);
    }

    [Fact]
    public async Task DeserializeJsonAsync_WhenStreamedBodyExceedsLimit_ThrowsHttpRequestException()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new SyntheticReadStream(4096, (byte)'A', declaresLength: false))
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
            }
        };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            RemoteHttpTransport.DeserializeJsonAsync<LauncherApiEnvelope<BaseConfigResponse>>(
                response,
                new Uri("https://example.test/api"),
                JsonDefaults.Strict,
                maxBytes: 1024,
                CancellationToken.None));

        Assert.Contains("exceeds", ex.Message);
        Assert.Contains("buffered: 4096", ex.Message);
    }

    [Fact]
    public async Task DeserializeJsonAsync_WhenBodyIsNotJson_ReportsTheUrlWithoutItsQuery()
    {
        // 守卫（AUD-SEC-007）：诊断消息里的 URL 必须去掉查询串。资源面板把玩家 UID 放在查询
        // 参数上且不发送任何鉴权头，而这条消息会写进恒被导出的 unified.log。
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>captive portal</html>", Encoding.UTF8, "application/json")
        };

        var ex = await Assert.ThrowsAsync<JsonException>(() =>
            RemoteHttpTransport.DeserializeJsonAsync<LauncherApiEnvelope<BaseConfigResponse>>(
                response,
                new Uri("https://example.test/config/get?uid=UID-SECRET-VALUE"),
                JsonDefaults.Strict,
                maxBytes: 1024 * 1024,
                CancellationToken.None));

        Assert.Contains("https://example.test/config/get", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("UID-SECRET-VALUE", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeserializeJsonAsync_WhenBodyExceedsLimit_ReportsTheUrlWithoutItsQuery()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new SyntheticReadStream(4096, (byte)'A', declaresLength: false))
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
            }
        };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            RemoteHttpTransport.DeserializeJsonAsync<LauncherApiEnvelope<BaseConfigResponse>>(
                response,
                new Uri("https://example.test/config/get?uid=UID-SECRET-VALUE"),
                JsonDefaults.Strict,
                maxBytes: 1024,
                CancellationToken.None));

        Assert.Contains("https://example.test/config/get", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("UID-SECRET-VALUE", ex.Message, StringComparison.Ordinal);
    }
}
