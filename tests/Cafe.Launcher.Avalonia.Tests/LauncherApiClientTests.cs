using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Cafe.Launcher.Avalonia.Helpers;
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
    public void RewriteCdnConfig_WhenCafe_RewritesPrimaryAndUsesPrimaryForBackup()
    {
        using var client = new LauncherApiClient(new HttpClientHandler(), new AuthorizationHeaderFactory(), new PatchUrlGroupService());
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

    [Fact]
    public async Task GetBaseConfigAsync_WhenEnvelopeCodeIsNot200_FailsFastWithoutRetry()
    {
        using var handler = new EnvelopeCodeHandler(503, "service under maintenance");
        using var client = new LauncherApiClient(
            handler,
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());

        var ex = await Assert.ThrowsAsync<LauncherApiEnvelopeException>(
            () => client.GetBaseConfigAsync(ProxyModes.Direct));

        Assert.Equal(1, handler.CallCount);
        Assert.Contains("service under maintenance", ex.Message);
    }

    [Fact]
    public async Task GetBaseConfigAsync_WhenEnvelopeDataIsMissing_RetriesBeforeFailing()
    {
        using var handler = new EnvelopeCodeHandler(200, message: null, includeData: false);
        using var client = new LauncherApiClient(
            handler,
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetBaseConfigAsync(ProxyModes.Direct));

        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task GetRemoteManifestAsync_WhenFirstAttemptsReturnNonJson_RetriesAndSucceedsOnThirdAttempt()
    {
        var badBytes = new byte[] { 0x8B, 0x0B, 0x00, 0x01 };
        const string goodJson = """{"source":"test","file":[]}""";
        using var handler = new FlakyManifestHandler(badBytes, goodJson, failFirstAttempts: 2);
        using var client = new LauncherApiClient(
            handler,
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());

        var manifest = await client.GetRemoteManifestAsync(
            "https://example.com/manifest.json",
            ProxyModes.Direct);

        Assert.Equal("test", manifest.Source);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task GetBaseConfigAsync_WhenDeclaredContentLengthExceedsLimit_ThrowsHttpRequestExceptionWithContext()
    {
        using var handler = new OversizedContentLengthHandler(128L * 1024 * 1024);
        using var client = new LauncherApiClient(
            handler,
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetBaseConfigAsync(ProxyModes.Direct));

        Assert.Contains("exceeds", ex.Message);
        Assert.Contains("url:", ex.Message);
        Assert.Contains("status: 200", ex.Message);
    }

    [Fact]
    public async Task DeserializeJsonAsync_WhenStreamedBodyExceedsLimit_ThrowsHttpRequestException()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new RepeatingStream(4096))
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
            }
        };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            RemoteHttpRequestService.DeserializeJsonAsync<LauncherApiEnvelope<BaseConfigResponse>>(
                response,
                new Uri("https://example.test/api"),
                JsonDefaults.Strict,
                maxBytes: 1024,
                CancellationToken.None));

        Assert.Contains("exceeds", ex.Message);
        Assert.Contains("buffered: 4096", ex.Message);
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

    /// <summary>Serves a fixed envelope payload; used to pin envelope retry semantics.</summary>
    private sealed class EnvelopeCodeHandler(int code, string? message, bool includeData = true) : HttpMessageHandler
    {
        private int _callCount;

        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            var data = includeData ? """{"launcher_background_img":null}""" : "null";
            var content = message is null
                ? $$"""{"code":{{code}},"data":{{data}}}"""
                : $$"""{"code":{{code}},"data":{{data}},"message":{{JsonSerializer.Serialize(message)}}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    content,
                    Encoding.UTF8,
                    "application/json")
            });
        }
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

    private sealed class FlakyManifestHandler(byte[] badBytes, string goodJson, int failFirstAttempts) : HttpMessageHandler
    {
        private int _callCount;

        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _callCount);
            HttpResponseMessage response;
            if (count <= failFirstAttempts)
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(badBytes)
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") }
                    }
                };
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(goodJson, Encoding.UTF8, "application/json")
                };
            }

            return Task.FromResult(response);
        }
    }

    /// <summary>Declares an oversized Content-Length while the actual body is small.</summary>
    private sealed class OversizedContentLengthHandler(long declaredLength) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = new StringContent(
                """{"code":200,"data":{}}""",
                Encoding.UTF8,
                "application/json")
            {
                Headers = { ContentLength = declaredLength }
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            });
        }
    }

    /// <summary>Non-seekable stream that repeats a byte pattern; used to exercise the chunked-body limit.</summary>
    private sealed class RepeatingStream(int totalBytes) : Stream
    {
        private int produced;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => produced;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (produced >= totalBytes)
            {
                return 0;
            }

            var read = Math.Min(count, totalBytes - produced);
            Array.Fill(buffer, (byte)'A', offset, read);
            produced += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
