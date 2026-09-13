using System.Net;
using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ResourcePanelApiClientTests
{
    [Fact]
    public async Task GetStatusAsync_ParsesExactStatusJsonPaths()
    {
        var client = new ResourcePanelApiClient(new StubRemoteHttpTransport(
            _ =>
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
        var client = new ResourcePanelApiClient(new StubRemoteHttpTransport(
            _ =>
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
    public async Task SaveConfigAsync_SendsExactQueryString()
    {
        var transport = new StubRemoteHttpTransport(_ => "ok");
        var client = new ResourcePanelApiClient(transport);

        await client.SaveConfigAsync(
            "UID123",
            ResourcePanelResourceModes.Chinese,
            ResourcePanelResourceModes.Japanese,
            ResourcePanelResourceModes.Chinese);

        var request = Assert.Single(transport.RequestedUris);
        Assert.Equal("/config/set", request.AbsolutePath);
        Assert.Equal("?uid=UID123&text=cn&voice=jp&media=cn", request.Query);
    }

    [Fact]
    public async Task GetConfigAsync_WhenNotFound_ReturnsEmptyConfig()
    {
        var client = new ResourcePanelApiClient(new StubRemoteHttpTransport(
            _ => throw new HttpRequestException("not found", null, HttpStatusCode.NotFound)));

        var config = await client.GetConfigAsync("UID_NOT_FOUND");

        Assert.NotNull(config);
        Assert.Null(config.Text);
        Assert.Null(config.Voice);
        Assert.Null(config.Media);
    }

    [Fact]
    public async Task GetStatusAsync_WhenTransportThrows_PropagatesHttpRequestException()
    {
        var transport = new StubRemoteHttpTransport(
            _ => throw new HttpRequestException("persistent failure"));
        var client = new ResourcePanelApiClient(transport);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetStatusAsync());

        // 重试策略已上移到传输层：客户端本身只发起一次请求，不做自重试。
        Assert.Single(transport.RequestedUris);
    }
}
