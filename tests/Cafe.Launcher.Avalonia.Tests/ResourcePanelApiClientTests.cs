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

        var status = await client.GetStatusAsync();

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

        var config = await client.GetConfigAsync("UID123");

        Assert.Equal(ResourcePanelResourceModes.Chinese, config.Text);
        Assert.Equal(ResourcePanelResourceModes.Japanese, config.Voice);
        Assert.Equal(ResourcePanelResourceModes.Chinese, config.Media);
    }

    [Fact]
    public async Task SaveConfigAsync_SendsExactJsonBody()
    {
        var handler = new JsonHandler("{}");
        using var client = new ResourcePanelApiClient(handler);

        await client.SaveConfigAsync(
            "UID123",
            ResourcePanelResourceModes.Chinese,
            ResourcePanelResourceModes.Japanese,
            ResourcePanelResourceModes.Chinese);

        Assert.Equal("POST", handler.LastRequestMethod);
        Assert.Equal("/config/set", handler.LastRequestPathAndQuery);
        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"uid\":\"UID123\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"cn\"", handler.LastRequestBody);
        Assert.Contains("\"voice\":\"jp\"", handler.LastRequestBody);
        Assert.Contains("\"media\":\"cn\"", handler.LastRequestBody);
        Assert.Contains("application/json", handler.LastRequestContentType);
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
}
