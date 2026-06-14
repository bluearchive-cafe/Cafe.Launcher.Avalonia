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
    public async Task SaveConfigAsync_SendsExactQueryParameters()
    {
        var handler = new JsonHandler("{}");
        using var client = new ResourcePanelApiClient(handler);

        await client.SaveConfigAsync(
            "UID123",
            ResourcePanelResourceModes.Chinese,
            ResourcePanelResourceModes.Japanese,
            ResourcePanelResourceModes.Chinese);

        Assert.Equal("/config/set?uid=UID123&text=cn&voice=jp&media=cn", handler.LastRequestPathAndQuery);
    }

    private sealed class JsonHandler : HttpMessageHandler
    {
        private readonly string json;

        public JsonHandler(string json)
        {
            this.json = json;
        }

        public string LastRequestPathAndQuery { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestPathAndQuery = request.RequestUri?.PathAndQuery ?? "";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
