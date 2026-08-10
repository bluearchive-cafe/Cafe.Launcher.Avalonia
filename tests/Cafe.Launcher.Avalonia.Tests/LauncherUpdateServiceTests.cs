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
    public async Task CheckForUpdateAsync_WhenNewerReleaseExists_ReturnsAllReleaseFilesInApiOrder()
    {
        var handler = new ReleaseHandler(
            HttpStatusCode.OK,
            """
            [
              {
                "version": "1.2.0",
                "files": [
                  {
                    "name": "Cafe.Launcher_v1.2.0.zip",
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v1.2.0/Cafe.Launcher_v1.2.0.zip",
                    "sha512": "",
                    "size": 5000000
                  },
                  {
                    "name": "Cafe.Launcher_Setup_v1.2.0.exe",
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v1.2.0/Cafe.Launcher_Setup_v1.2.0.exe",
                    "sha512": "def456",
                    "size": 6000000
                  }
                ],
                "releaseDate": "2026-06-15T00:00:00Z"
              }
            ]
            """);
        using var service = new LauncherUpdateService(handler);

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta, ProxyModes.Direct);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.2.0", result.LatestVersion);
        Assert.Collection(
            result.Files,
            file =>
            {
                Assert.Equal("Cafe.Launcher_v1.2.0.zip", file.Name);
                Assert.Equal("https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v1.2.0/Cafe.Launcher_v1.2.0.zip", file.Url);
                Assert.Equal(5000000, file.Size);
            },
            file =>
            {
                Assert.Equal("Cafe.Launcher_Setup_v1.2.0.exe", file.Name);
                Assert.Equal("https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v1.2.0/Cafe.Launcher_Setup_v1.2.0.exe", file.Url);
                Assert.Equal(6000000, file.Size);
            });
        Assert.Equal(ApiConfig.LauncherReleasesPath, handler.RequestPath);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenReleaseMatchesCurrentVersion_ReturnsNoUpdate()
    {
        var currentVersion = BuildInfo.LauncherVersion;
        var handler = new ReleaseHandler(
            HttpStatusCode.OK,
            $$"""
            [
              {
                "version": "{{currentVersion}}",
                "files": [
                  {
                    "name": "Cafe.Launcher.zip",
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v1.0.0/Cafe.Launcher.zip",
                    "sha512": "abc",
                    "size": 100
                  }
                ],
                "releaseDate": "2026-06-15T00:00:00Z"
              }
            ]
            """);
        using var service = new LauncherUpdateService(handler);

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta, ProxyModes.Direct);

        Assert.True(result.IsSuccessful);
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal(BuildInfo.LauncherVersion, result.LatestVersion);
        Assert.Single(result.Files);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenServerReturnsNotFound_ReturnsFailure()
    {
        using var service = new LauncherUpdateService(
            new ReleaseHandler(HttpStatusCode.NotFound, """{"message":"Not Found"}"""));

        var result = await service.CheckForUpdateAsync(UpdateChannels.Stable, ProxyModes.Direct);

        Assert.False(result.IsSuccessful);
        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenRequiredFieldsAreMissing_ReturnsFailure()
    {
        using var service = new LauncherUpdateService(
            new ReleaseHandler(HttpStatusCode.OK, """[{"files":[]}]"""));

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta, ProxyModes.Direct);

        Assert.False(result.IsSuccessful);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenVersionIsNotSemver_ReturnsFailure()
    {
        using var service = new LauncherUpdateService(
            new ReleaseHandler(
                HttpStatusCode.OK,
                """
                [
                  {
                    "version": "latest",
                    "files": [
                      {
                        "name": "Cafe.Launcher.zip",
                        "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v1.0.0/Cafe.Launcher.zip",
                        "sha512": "abc",
                        "size": 100
                      }
                    ],
                    "releaseDate": "2026-06-15T00:00:00Z"
                  }
                ]
                """));

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta, ProxyModes.Direct);

        Assert.False(result.IsSuccessful);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenInvalidVersionPrecedesValidVersion_UsesValidVersion()
    {
        using var service = new LauncherUpdateService(
            new ReleaseHandler(
                HttpStatusCode.OK,
                """
                [
                  {
                    "version": "latest",
                    "files": [
                      {
                        "name": "latest.zip",
                        "url": "https://example.com/latest.zip",
                        "sha512": "invalid",
                        "size": 100
                      }
                    ]
                  },
                  {
                    "version": "1.2.0",
                    "files": [
                      {
                        "name": "Cafe.Launcher_v1.2.0.zip",
                        "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v1.2.0/Cafe.Launcher_v1.2.0.zip",
                        "sha512": "abc123",
                        "size": 5000000
                      }
                    ]
                  }
                ]
                """),
            currentVersionOverride: "1.0.0");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta, ProxyModes.Direct);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.2.0", result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenReleaseFileIsOutsideReleaseRepository_ReturnsFailure()
    {
        using var service = new LauncherUpdateService(
            new ReleaseHandler(
                HttpStatusCode.OK,
                """
                [{"version":"1.2.0","files":[{"name":"update.zip","url":"https://example.com/update.zip","size":100}]}]
                """),
            currentVersionOverride: "1.0.0");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta, ProxyModes.Direct);

        Assert.False(result.IsSuccessful);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenReleaseFilesAreMissing_ReturnsValidationFailureMessage()
    {
        using var service = new LauncherUpdateService(
            new ReleaseHandler(HttpStatusCode.OK, """[{"version":"1.2.0","files":[]}]"""),
            currentVersionOverride: "1.0.0");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta, ProxyModes.Direct);

        Assert.False(result.IsSuccessful);
        Assert.Equal("files must contain at least one entry", result.FailureMessage);
    }

    [Theory]
    [InlineData("""[{"version":"1.2.0","files":[]}]""")]
    [InlineData("""[{"version":"1.2.0","files":[{"name":"","url":"https://example.com/update.zip","sha512":"","size":100}]}]""")]
    [InlineData("""[{"version":"1.2.0","files":[{"name":"update.zip","url":"","sha512":"","size":100}]}]""")]
    [InlineData("""[{"version":"1.2.0","files":[{"name":"update.zip","url":"file:///tmp/update.zip","sha512":"","size":100}]}]""")]
    [InlineData("""[{"version":"1.2.0","files":[{"name":"update.zip","url":"mailto:updates@example.com","sha512":"","size":100}]}]""")]
    [InlineData("""[{"version":"1.2.0","files":[{"name":"update.zip","url":"https://example.com/update.zip","sha512":"","size":0}]}]""")]
    [InlineData("""[{"version":"1.2.0","files":[{"name":"update.zip","url":"https://example.com/update.zip","sha512":"","size":-1}]}]""")]
    [InlineData("""[{"version":"1.2.0","files":[{"name":"valid.zip","url":"https://example.com/valid.zip","sha512":"","size":100},{"name":"","url":"https://example.com/invalid.zip","sha512":"","size":100}]}]""")]
    public async Task CheckForUpdateAsync_WhenDownloadFileIsInvalid_ReturnsFailure(string response)
    {
        using var service = new LauncherUpdateService(
            new ReleaseHandler(HttpStatusCode.OK, response),
            currentVersionOverride: "1.0.0");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta, ProxyModes.Direct);

        Assert.False(result.IsSuccessful);
        Assert.False(result.IsUpdateAvailable);
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
        var handler = new ReleaseHandler(
            HttpStatusCode.OK,
            """
            [
              {
                "version": "1.1.0-beta.2",
                "files": [
                  {
                    "name": "Cafe.Launcher_v1.1.0-beta.2.zip",
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v1.1.0-beta.2/Cafe.Launcher_v1.1.0-beta.2.zip",
                    "sha512": "abc",
                    "size": 100
                  }
                ],
                "releaseDate": "2026-06-15T00:00:00Z"
              },
              {
                "version": "1.0.0",
                "files": [
                  {
                    "name": "Cafe.Launcher_v1.0.0.zip",
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v1.0.0/Cafe.Launcher_v1.0.0.zip",
                    "sha512": "abc",
                    "size": 100
                  }
                ],
                "releaseDate": "2026-06-15T00:00:00Z"
              }
            ]
            """);
        // Current version "1.0.0-beta.1" (prerelease) — override version
        using var service = new LauncherUpdateService(
            handler,
            currentVersionOverride: "1.0.0-beta.1");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta, ProxyModes.Direct);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.1.0-beta.2", result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenStableChannel_SkipsPrerelease()
    {
        var handler = new ReleaseHandler(
            HttpStatusCode.OK,
            """
            [
              {
                "version": "2.0.0-beta.1",
                "files": [
                  {
                    "name": "Cafe.Launcher_v2.0.0-beta.1.zip",
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v2.0.0-beta.1/Cafe.Launcher_v2.0.0-beta.1.zip",
                    "sha512": "abc",
                    "size": 100
                  }
                ],
                "releaseDate": "2026-06-15T00:00:00Z"
              },
              {
                "version": "1.5.0",
                "files": [
                  {
                    "name": "Cafe.Launcher_v1.5.0.zip",
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v1.5.0/Cafe.Launcher_v1.5.0.zip",
                    "sha512": "abc",
                    "size": 100
                  }
                ],
                "releaseDate": "2026-06-15T00:00:00Z"
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
        var handler = new ReleaseHandler(
            HttpStatusCode.OK,
            """
            [
              {
                "version": "1.5.0-beta.1",
                "files": [
                  {
                    "name": "Cafe.Launcher_v1.5.0-beta.1.zip",
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v1.5.0-beta.1/Cafe.Launcher_v1.5.0-beta.1.zip",
                    "sha512": "abc",
                    "size": 100
                  }
                ],
                "releaseDate": "2026-06-15T00:00:00Z"
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

    private sealed class ReleaseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode statusCode;
        private readonly string content;

        public ReleaseHandler(HttpStatusCode statusCode, string content)
        {
            this.statusCode = statusCode;
            this.content = content;
        }

        public string RequestPath { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestPath = request.RequestUri?.PathAndQuery ?? "";

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }
}
