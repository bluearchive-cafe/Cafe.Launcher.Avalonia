using System.Net;
using System.Text;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ResourcePanelApiClientTests
{
    [Fact]
    public async Task GetStatusAsync_ParsesExactStatusJsonPaths()
    {
        using var client = new ResourcePanelApiClient(new JsonHandler(
            """
            {
              "text": {
                "official": { "version": "1.0.0" },
                "localized": { "version": "1.0.0" }
              },
              "voice": {
                "official": { "version": "2.0.0" },
                "localized": { "version": "2.1.0" }
              },
              "media": {
                "official": { "version": "3.0.0" },
                "localized": { "version": "3.0.0" }
              }
            }
            """));

        var status = await client.GetStatusAsync(ProxyModes.Direct);

        Assert.Equal("1.0.0", status.Text.Official.Version);
        Assert.Equal("1.0.0", status.Text.Localized.Version);
        Assert.Equal("2.0.0", status.Voice.Official.Version);
        Assert.Equal("2.1.0", status.Voice.Localized.Version);
        Assert.Equal("3.0.0", status.Media.Official.Version);
        Assert.Equal("3.0.0", status.Media.Localized.Version);
    }

    [Fact]
    public async Task GetConfigAsync_ParsesResourceModes()
    {
        using var client = new ResourcePanelApiClient(new JsonHandler(
            """
            {
              "text": "cn",
              "voice": "jp",
              "media": "cn"
            }
            """));

        var config = await client.GetConfigAsync("UID123", ProxyModes.Direct);

        Assert.Equal(ResourcePanelResourceModes.Chinese, config.Text);
        Assert.Equal(ResourcePanelResourceModes.Japanese, config.Voice);
        Assert.Equal(ResourcePanelResourceModes.Chinese, config.Media);
    }

    [Fact]
    public async Task SaveConfigAsync_SendsExactQueryString()
    {
        var handler = new JsonHandler("{}");
        using var client = new ResourcePanelApiClient(handler);

        await client.SaveConfigAsync(
            "UID123",
            ResourcePanelResourceModes.Chinese,
            ResourcePanelResourceModes.Japanese,
            ResourcePanelResourceModes.Chinese,
            ProxyModes.Direct);

        Assert.Equal("GET", handler.LastRequestMethod);
        Assert.Equal("/config/set?uid=UID123&text=cn&voice=jp&media=cn", handler.LastRequestPathAndQuery);
        Assert.Null(handler.LastRequestBody);
        Assert.Null(handler.LastRequestContentType);
    }

    [Fact]
    public async Task GetConfigAsync_WhenNotFound_ReturnsEmptyConfig()
    {
        using var client = new ResourcePanelApiClient(new NotFoundHandler());

        var config = await client.GetConfigAsync("UID_NOT_FOUND", ProxyModes.Direct);

        Assert.NotNull(config);
        Assert.Null(config.Text);
        Assert.Null(config.Voice);
        Assert.Null(config.Media);
    }

    [Fact]
    public async Task GetStatusAsync_WhenFirstAttemptThrows_RetriesAndSucceeds()
    {
        var handler = new FlakyHandler(
            new HttpRequestException("simulated network failure"),
            """{"text":{"official":{"version":"1.0.0"},"localized":{"version":"1.0.0"}},"voice":{"official":{"version":"2.0.0"},"localized":{"version":"2.0.0"}},"media":{"official":{"version":"3.0.0"},"localized":{"version":"3.0.0"}}}""",
            failFirstAttempts: 1);
        using var client = new ResourcePanelApiClient(handler);

        var status = await client.GetStatusAsync(ProxyModes.Direct);

        Assert.Equal("1.0.0", status.Text.Official.Version);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetStatusAsync_WhenAllAttemptsThrow_ThrowsAfterMaxRetries()
    {
        var handler = new FlakyHandler(
            new HttpRequestException("persistent failure"),
            "{}",
            failFirstAttempts: 99);
        using var client = new ResourcePanelApiClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetStatusAsync(ProxyModes.Direct));
        Assert.Equal(3, handler.CallCount);
    }

    private sealed class JsonHandler : HttpMessageHandler
    {
        private readonly string json;

        public JsonHandler(string json)
        {
            this.json = json;
        }

        public string LastRequestMethod { get; private set; } = "";
        public string LastRequestPathAndQuery { get; private set; } = "";
        public string? LastRequestBody { get; private set; }
        public string? LastRequestContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestMethod = request.Method.Method;
            LastRequestPathAndQuery = request.RequestUri?.PathAndQuery ?? "";
            LastRequestContentType = request.Content?.Headers.ContentType?.ToString();
            LastRequestBody = request.Content is not null ? await request.Content.ReadAsStringAsync(cancellationToken) : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FlakyHandler : HttpMessageHandler
    {
        private readonly Exception _firstFailure;
        private readonly string _successJson;
        private readonly int _failFirstAttempts;
        private int _callCount;

        public int CallCount => _callCount;

        public FlakyHandler(Exception firstFailure, string successJson, int failFirstAttempts)
        {
            _firstFailure = firstFailure;
            _successJson = successJson;
            _failFirstAttempts = failFirstAttempts;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _callCount);
            if (count <= _failFirstAttempts)
            {
                return Task.FromException<HttpResponseMessage>(_firstFailure);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_successJson, Encoding.UTF8, "application/json")
            });
        }
    }
}
