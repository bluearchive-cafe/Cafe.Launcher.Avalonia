using System.Net;
using System.Net.Http;
using System.Text;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
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
            [
              {
                "tag_name": "v1.2.0",
                "html_url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/tag/v1.2.0"
              }
            ]
            """);
        using var service = new LauncherUpdateService(handler);

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta, ProxyModes.Direct);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.2.0", result.LatestVersion);
        Assert.Equal(
            "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/tag/v1.2.0",
            result.ReleaseUrl);
        Assert.Equal(LauncherConstants.GitHubReleasesPath, handler.RequestPath);
        Assert.Equal("application/vnd.github+json", handler.AcceptMediaType);
        Assert.Equal("2022-11-28", handler.ApiVersion);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenReleaseMatchesCurrentVersion_ReturnsNoUpdate()
    {
        var handler = new GitHubReleaseHandler(
            HttpStatusCode.OK,
            $$"""
            [
              {
                "tag_name": "v{{LauncherConstants.LauncherVersion}}",
                "html_url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/latest",
                "prerelease": true
              }
            ]
            """);
        using var service = new LauncherUpdateService(handler);

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta, ProxyModes.Direct);

        Assert.True(result.IsSuccessful);
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal(LauncherConstants.LauncherVersion, result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenGitHubReturnsNotFound_ReturnsFailure()
    {
        using var service = new LauncherUpdateService(
            new GitHubReleaseHandler(HttpStatusCode.NotFound, """{"message":"Not Found"}"""));

        var result = await service.CheckForUpdateAsync(UpdateChannels.Stable, ProxyModes.Direct);

        Assert.False(result.IsSuccessful);
        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenRequiredFieldsAreMissing_ReturnsFailure()
    {
        using var service = new LauncherUpdateService(
            new GitHubReleaseHandler(HttpStatusCode.OK, """[{"tag_name":"v1.2.0"}]"""));

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta, ProxyModes.Direct);

        Assert.False(result.IsSuccessful);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenTagIsNotNumericVersion_ReturnsFailure()
    {
        using var service = new LauncherUpdateService(
            new GitHubReleaseHandler(
                HttpStatusCode.OK,
                """
                [
                  {
                    "tag_name": "latest",
                    "html_url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/latest"
                  }
                ]
                """));

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta, ProxyModes.Direct);

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

    // ── Channel filtering ──────────────────────────────────────────────────

    [Fact]
    public async Task CheckForUpdateAsync_WhenBetaChannel_PicksFirstRelease()
    {
        var handler = new GitHubReleaseHandler(
            HttpStatusCode.OK,
            """
            [
              {
                "tag_name": "v1.1.0-beta.2",
                "html_url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/tag/v1.1.0-beta.2",
                "prerelease": true
              },
              {
                "tag_name": "v1.0.0",
                "html_url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/tag/v1.0.0",
                "prerelease": false
              }
            ]
            """);
        // Current version "1.0.0-beta.1" (prerelease) from LauncherVersion
        using var service = new LauncherUpdateService(handler);

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta, ProxyModes.Direct);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.1.0-beta.2", result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenStableChannel_SkipsPrerelease()
    {
        var handler = new GitHubReleaseHandler(
            HttpStatusCode.OK,
            """
            [
              {
                "tag_name": "v2.0.0-beta.1",
                "html_url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/tag/v2.0.0-beta.1",
                "prerelease": true
              },
              {
                "tag_name": "v1.5.0",
                "html_url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/tag/v1.5.0",
                "prerelease": false
              }
            ]
            """);
        using var service = new LauncherUpdateService(
            handler,
            currentVersionOverride: "1.0.0");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Stable, ProxyModes.Direct);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.5.0", result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenStableChannel_OnlyPrereleasesExist_ReturnsUpToDate()
    {
        var handler = new GitHubReleaseHandler(
            HttpStatusCode.OK,
            """
            [
              {
                "tag_name": "v1.5.0-beta.1",
                "html_url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/tag/v1.5.0-beta.1",
                "prerelease": true
              }
            ]
            """);
        using var service = new LauncherUpdateService(
            handler,
            currentVersionOverride: "1.0.0");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Stable, ProxyModes.Direct);

        Assert.True(result.IsSuccessful);
        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdateAsync_SendsAuthorizationHeader_WhenTokenIsSet()
    {
        var handler = new GitHubReleaseHandler(
            HttpStatusCode.OK,
            """
            [
              {
                "tag_name": "v1.2.0",
                "html_url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/tag/v1.2.0"
              }
            ]
            """);
        using var service = new LauncherUpdateService(
            handler,
            gitHubTokenOverride: "ghp_test_token");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta, ProxyModes.Direct);

        Assert.True(result.IsSuccessful);
        Assert.Equal("Bearer ghp_test_token", handler.AuthorizationHeader);
    }

    // ── Prerelease label comparison ────────────────────────────────────────

    [Fact]
    public void IsNewerVersion_WhenBothPrereleases_HigherNumericSuffix_ReturnsTrue()
    {
        Assert.True(LauncherUpdateService.IsNewerVersion("1.2.0-beta.2", "1.2.0-beta.1"));
    }

    [Fact]
    public void IsNewerVersion_WhenBothPrereleases_SameSuffix_ReturnsFalse()
    {
        Assert.False(LauncherUpdateService.IsNewerVersion("1.2.0-beta.1", "1.2.0-beta.1"));
    }

    [Fact]
    public void IsNewerVersion_WhenLatestPrerelease_CurrentStable_ReturnsFalse()
    {
        Assert.False(LauncherUpdateService.IsNewerVersion("1.2.0-beta.1", "1.2.0"));
    }

    [Fact]
    public void IsNewerVersion_WhenBothPrereleases_BetaVsAlpha_ReturnsTrue()
    {
        Assert.True(LauncherUpdateService.IsNewerVersion("1.2.0-beta.1", "1.2.0-alpha.1"));
    }

    [Fact]
    public void IsNewerVersion_WhenBothPrereleases_NumericLowerThanAlpha_ReturnsFalse()
    {
        Assert.False(LauncherUpdateService.IsNewerVersion("1.2.0-1", "1.2.0-alpha"));
    }

    [Fact]
    public void IsNewerVersion_WhenBothPrereleases_MoreFieldsHigher_ReturnsTrue()
    {
        Assert.True(LauncherUpdateService.IsNewerVersion("1.2.0-beta.1.fix", "1.2.0-beta.1"));
    }

    [Fact]
    public void IsNewerVersion_WhenBothPrereleases_Beta11VsBeta2_ReturnsTrue()
    {
        Assert.True(LauncherUpdateService.IsNewerVersion("1.0.0-beta.11", "1.0.0-beta.2"));
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
        public string? AuthorizationHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestPath = request.RequestUri?.PathAndQuery ?? "";
            AcceptMediaType = request.Headers.Accept.Single().MediaType ?? "";
            ApiVersion = request.Headers.GetValues("X-GitHub-Api-Version").Single();
            AuthorizationHeader = request.Headers.Authorization?.ToString();

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }
}
