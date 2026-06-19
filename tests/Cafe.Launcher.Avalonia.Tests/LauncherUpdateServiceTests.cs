using System.Net;
using System.Net.Http;
using System.Text;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LauncherUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdateAsync_WhenNewerReleaseExists_ReturnsReleaseDetails()
    {
        var handler = new GitHubReleaseHandler(
            HttpStatusCode.OK,
            """
            {
              "tag_name": "v1.2.0",
              "html_url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/tag/v1.2.0"
            }
            """);
        using var service = new LauncherUpdateService(handler);

        var result = await service.CheckForUpdateAsync();

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.2.0", result.LatestVersion);
        Assert.Equal(
            "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/tag/v1.2.0",
            result.ReleaseUrl);
        Assert.Equal(LauncherConstants.GitHubLatestReleasePath, handler.RequestPath);
        Assert.Equal("application/vnd.github+json", handler.AcceptMediaType);
        Assert.Equal("2022-11-28", handler.ApiVersion);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenReleaseMatchesCurrentVersion_ReturnsNoUpdate()
    {
        var handler = new GitHubReleaseHandler(
            HttpStatusCode.OK,
            $$"""
            {
              "tag_name": "v{{LauncherConstants.LauncherVersion}}",
              "html_url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/latest"
            }
            """);
        using var service = new LauncherUpdateService(handler);

        var result = await service.CheckForUpdateAsync();

        Assert.True(result.IsSuccessful);
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal(LauncherConstants.LauncherVersion, result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenGitHubReturnsNotFound_ReturnsFailure()
    {
        using var service = new LauncherUpdateService(
            new GitHubReleaseHandler(HttpStatusCode.NotFound, """{"message":"Not Found"}"""));

        var result = await service.CheckForUpdateAsync();

        Assert.False(result.IsSuccessful);
        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenRequiredFieldsAreMissing_ReturnsFailure()
    {
        using var service = new LauncherUpdateService(
            new GitHubReleaseHandler(HttpStatusCode.OK, """{"tag_name":"v1.2.0"}"""));

        var result = await service.CheckForUpdateAsync();

        Assert.False(result.IsSuccessful);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenTagIsNotNumericVersion_ReturnsFailure()
    {
        using var service = new LauncherUpdateService(
            new GitHubReleaseHandler(
                HttpStatusCode.OK,
                """
                {
                  "tag_name": "latest",
                  "html_url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/latest"
                }
                """));

        var result = await service.CheckForUpdateAsync();

        Assert.False(result.IsSuccessful);
    }

    [Fact]
    public void IsNewerVersion_WhenStableReleaseMatchesPrereleaseCore_ReturnsTrue()
    {
        Assert.True(LauncherUpdateService.IsNewerVersion("1.2.0", "1.2.0-beta.1"));
    }

    [Fact]
    public void IsNewerVersion_WhenStableVersionsMatch_ReturnsFalse()
    {
        Assert.False(LauncherUpdateService.IsNewerVersion("1.2.0", "1.2.0"));
    }

    [Fact]
    public void IsNewerVersion_WhenLatestCoreIsLower_ReturnsFalse()
    {
        Assert.False(LauncherUpdateService.IsNewerVersion("1.1.9", "1.2.0-beta.1"));
    }

    private sealed class GitHubReleaseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode statusCode;
        private readonly string content;

        public GitHubReleaseHandler(HttpStatusCode statusCode, string content)
        {
            this.statusCode = statusCode;
            this.content = content;
        }

        public string RequestPath { get; private set; } = "";
        public string AcceptMediaType { get; private set; } = "";
        public string ApiVersion { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestPath = request.RequestUri?.AbsolutePath ?? "";
            AcceptMediaType = request.Headers.Accept.Single().MediaType ?? "";
            ApiVersion = request.Headers.GetValues("X-GitHub-Api-Version").Single();

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }
}
