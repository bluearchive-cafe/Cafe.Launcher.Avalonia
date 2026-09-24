using System.Net;
using System.Net.Http;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LauncherUpdateServiceTests
{
    // 降级语义按 URI 区分两端点：server 端与 GitHub 端分别以配置常量组装。
    private static readonly Uri ProxyReleasesUri =
        new(new Uri(ApiConfig.LauncherApiBaseUrl), ApiConfig.LauncherReleasesPath);

    private static readonly Uri GitHubReleasesUri = new(ApiConfig.GitHubReleasesApiUrl);

    private const string GitHubReleasesJson =
        """
        [
          {
            "tag_name":"v1.0.0-beta.8",
            "draft":false,
            "published_at":"2026-07-19T14:28:42Z",
            "body":"## Beta notes",
            "assets":[
              {
                "name":"Cafe.Launcher.Avalonia_v1.0.0-beta.8_setup.exe",
                "browser_download_url":"https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.0.0-beta.8/Cafe.Launcher.Avalonia_v1.0.0-beta.8_setup.exe",
                "size":54170696,
                "state":"uploaded"
              }
            ]
          }
        ]
        """;

    [Fact]
    public async Task CheckForUpdateAsync_WhenNewerReleaseExists_ReturnsAllReleaseFilesInApiOrder()
    {
        var transport = CreateReleasesTransport(
            """
            [
              {
                "version": "1.2.0",
                "releaseNotes": "## Highlights\n\n- Faster updates",
                "files": [
                  {
                    "name": "Cafe.Launcher_v1.2.0.zip",
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.2.0/Cafe.Launcher_v1.2.0.zip",
                    "sha512": "",
                    "size": 5000000
                  },
                  {
                    "name": "Cafe.Launcher_Setup_v1.2.0.exe",
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.2.0/Cafe.Launcher_Setup_v1.2.0.exe",
                    "sha512": "def456",
                    "size": 6000000
                  }
                ],
                "releaseDate": "2026-06-15T00:00:00Z"
              }
            ]
            """);
        var service = new LauncherUpdateService(transport);

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.2.0", result.LatestVersion);
        Assert.Equal("## Highlights\n\n- Faster updates", result.ReleaseNotes);
        Assert.Collection(
            result.Files,
            file =>
            {
                Assert.Equal("Cafe.Launcher_v1.2.0.zip", file.Name);
                Assert.Equal("https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.2.0/Cafe.Launcher_v1.2.0.zip", file.Url);
                Assert.Equal(5000000, file.Size);
            },
            file =>
            {
                Assert.Equal("Cafe.Launcher_Setup_v1.2.0.exe", file.Name);
                Assert.Equal("https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.2.0/Cafe.Launcher_Setup_v1.2.0.exe", file.Url);
                Assert.Equal(6000000, file.Size);
            });
        Assert.Equal(ProxyReleasesUri, Assert.Single(transport.RequestedUris));
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenReleaseMatchesCurrentVersion_ReturnsNoUpdate()
    {
        var currentVersion = BuildInfo.LauncherVersion;
        var transport = CreateReleasesTransport(
            $$"""
            [
              {
                "version": "{{currentVersion}}",
                "files": [
                  {
                    "name": "Cafe.Launcher.zip",
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.0.0/Cafe.Launcher.zip",
                    "sha512": "abc",
                    "size": 100
                  }
                ],
                "releaseDate": "2026-06-15T00:00:00Z"
              }
            ]
            """);
        var service = new LauncherUpdateService(transport);

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta);

        Assert.True(result.IsSuccessful);
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal(BuildInfo.LauncherVersion, result.LatestVersion);
        Assert.Single(result.Files);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenServerReturnsNotFound_ReturnsFailure()
    {
        // 原假体对两端点都应答 404：代理端点的失败会降级到 GitHub 端点，
        // 后者再次 404 后失败才落为返回值。
        var transport = new StubRemoteHttpTransport(
            _ => new HttpRequestException("Not Found", null, HttpStatusCode.NotFound));
        var service = new LauncherUpdateService(transport);

        var result = await service.CheckForUpdateAsync(UpdateChannels.Stable);

        Assert.False(result.IsSuccessful);
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal(2, transport.RequestedUris.Count);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenProxyIsUnavailable_UsesGitHubReleases()
    {
        var transport = CreateReleasesTransport(
            gitHubReleasesJson: GitHubReleasesJson,
            proxyFailure: new HttpRequestException("proxy unavailable"));
        var service = new LauncherUpdateService(transport, currentVersionOverride: "1.0.0-beta.7");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.0.0-beta.8", result.LatestVersion);
        Assert.Equal("## Beta notes", result.ReleaseNotes);
        Assert.Single(result.Files);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenProxyOmitsNotes_LoadsBodyFromGitHubTag()
    {
        var transport = new StubRemoteHttpTransport(uri =>
            uri == ProxyReleasesUri
                ? """
                  [{
                    "version":"1.0.0-beta.8",
                    "files":[{
                      "name":"Cafe.Launcher.Avalonia_v1.0.0-beta.8_win-x64.zip",
                      "url":"https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.0.0-beta.8/Cafe.Launcher.Avalonia_v1.0.0-beta.8_win-x64.zip",
                      "size":100
                    }]
                  }]
                  """
                : """{"body":"## Highlights\n\n- Faster updates"}""");
        var service = new LauncherUpdateService(transport, currentVersionOverride: "1.0.0-beta.7");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("## Highlights\n\n- Faster updates", result.ReleaseNotes);
        Assert.Equal(
            new Uri(ApiConfig.GitHubReleaseByTagApiUrl + "v1.0.0-beta.8"),
            transport.RequestedUris[1]);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenProxyTimesOut_UsesGitHubReleases()
    {
        var transport = CreateReleasesTransport(
            gitHubReleasesJson: GitHubReleasesJson,
            proxyFailure: new TaskCanceledException("simulated proxy timeout"));
        var service = new LauncherUpdateService(transport, currentVersionOverride: "1.0.0-beta.7");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.0.0-beta.8", result.LatestVersion);
        Assert.Single(result.Files);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenProxyUrlIsRejected_UsesGitHubReleases()
    {
        // 与 RemoteHttpUrlValidator 的拒绝异常同形：传输契约把 URL 校验失败
        // 映射为 InvalidOperationException（CR-20260921-070313-7BDC——本机 DNS
        // 以私网 ULA 应答更新端点曾使检查直接崩溃）。
        var transport = CreateReleasesTransport(
            gitHubReleasesJson: GitHubReleasesJson,
            proxyFailure: new InvalidOperationException(
                "Remote URL resolves to a blocked network address. Blocked: fdfe:dcba:9876::14a"));
        var service = new LauncherUpdateService(transport, currentVersionOverride: "1.0.0-beta.7");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.0.0-beta.8", result.LatestVersion);
        Assert.Single(result.Files);
        Assert.Equal(GitHubReleasesUri, transport.RequestedUris[1]);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenUrlIsRejectedOnBothEndpoints_ReturnsFailure()
    {
        // 两个端点的 DNS 都被拒绝时检查必须落为失败返回值，而不是异常逃逸到
        // Dispatcher（CR-20260921-070313-7BDC 的崩溃形态）。
        var transport = new StubRemoteHttpTransport(_ => new InvalidOperationException(
            "Remote URL resolves to a blocked network address. Blocked: fdfe:dcba:9876::14a"));
        var service = new LauncherUpdateService(transport);

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta);

        Assert.False(result.IsSuccessful);
        Assert.False(result.IsUpdateAvailable);
        Assert.IsType<InvalidOperationException>(result.FailureException);
        Assert.Equal(2, transport.RequestedUris.Count);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenRequiredFieldsAreMissing_ReturnsFailure()
    {
        var transport = CreateReleasesTransport("""[{"files":[]}]""");
        var service = new LauncherUpdateService(transport);

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta);

        Assert.False(result.IsSuccessful);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenVersionIsNotSemver_ReturnsFailure()
    {
        var transport = CreateReleasesTransport(
            """
            [
              {
                "version": "latest",
                "files": [
                  {
                    "name": "Cafe.Launcher.zip",
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.0.0/Cafe.Launcher.zip",
                    "sha512": "abc",
                    "size": 100
                  }
                ],
                "releaseDate": "2026-06-15T00:00:00Z"
              }
            ]
            """);
        var service = new LauncherUpdateService(transport);

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta);

        Assert.False(result.IsSuccessful);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenInvalidVersionPrecedesValidVersion_UsesValidVersion()
    {
        var transport = CreateReleasesTransport(
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
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.2.0/Cafe.Launcher_v1.2.0.zip",
                    "sha512": "abc123",
                    "size": 5000000
                  }
                ]
              }
            ]
            """);
        var service = new LauncherUpdateService(transport, currentVersionOverride: "1.0.0");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.2.0", result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenReleaseFileIsOutsideReleaseRepository_ReturnsFailure()
    {
        var transport = CreateReleasesTransport(
            """[{"version":"1.2.0","files":[{"name":"update.zip","url":"https://example.com/update.zip","size":100}]}]""");
        var service = new LauncherUpdateService(transport, currentVersionOverride: "1.0.0");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta);

        Assert.False(result.IsSuccessful);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenReleaseFilesAreMissing_ReturnsValidationFailureMessage()
    {
        var transport = CreateReleasesTransport("""[{"version":"1.2.0","files":[]}]""");
        var service = new LauncherUpdateService(transport, currentVersionOverride: "1.0.0");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta);

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
        var transport = CreateReleasesTransport(response);
        var service = new LauncherUpdateService(transport, currentVersionOverride: "1.0.0");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta);

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
        var transport = CreateReleasesTransport(
            """
            [
              {
                "version": "1.1.0-beta.2",
                "files": [
                  {
                    "name": "Cafe.Launcher_v1.1.0-beta.2.zip",
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.1.0-beta.2/Cafe.Launcher_v1.1.0-beta.2.zip",
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
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.0.0/Cafe.Launcher_v1.0.0.zip",
                    "sha512": "abc",
                    "size": 100
                  }
                ],
                "releaseDate": "2026-06-15T00:00:00Z"
              }
            ]
            """);
        // Current version "1.0.0-beta.1" (prerelease) — override version
        var service = new LauncherUpdateService(transport, currentVersionOverride: "1.0.0-beta.1");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Beta);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.1.0-beta.2", result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenStableChannel_SkipsPrerelease()
    {
        var transport = CreateReleasesTransport(
            """
            [
              {
                "version": "2.0.0-beta.1",
                "files": [
                  {
                    "name": "Cafe.Launcher_v2.0.0-beta.1.zip",
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v2.0.0-beta.1/Cafe.Launcher_v2.0.0-beta.1.zip",
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
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.5.0/Cafe.Launcher_v1.5.0.zip",
                    "sha512": "abc",
                    "size": 100
                  }
                ],
                "releaseDate": "2026-06-15T00:00:00Z"
              }
            ]
            """);
        var service = new LauncherUpdateService(transport, currentVersionOverride: "1.0.0");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Stable);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.5.0", result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenStableChannel_OnlyPrereleasesExist_ReturnsUpToDate()
    {
        var transport = CreateReleasesTransport(
            """
            [
              {
                "version": "1.5.0-beta.1",
                "files": [
                  {
                    "name": "Cafe.Launcher_v1.5.0-beta.1.zip",
                    "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.5.0-beta.1/Cafe.Launcher_v1.5.0-beta.1.zip",
                    "sha512": "abc",
                    "size": 100
                  }
                ],
                "releaseDate": "2026-06-15T00:00:00Z"
              }
            ]
            """);
        var service = new LauncherUpdateService(transport, currentVersionOverride: "1.0.0");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Stable);

        Assert.True(result.IsSuccessful);
        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenStableChannel_CurrentPrereleaseReceivesStableRelease()
    {
        var transport = CreateReleasesTransport(
            """
            [{
              "version": "1.0.0",
              "files": [{
                "name": "Cafe.Launcher.Avalonia_v1.0.0_setup.exe",
                "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.0.0/Cafe.Launcher.Avalonia_v1.0.0_setup.exe",
                "size": 100
              }]
            }]
            """);
        var service = new LauncherUpdateService(transport, currentVersionOverride: "1.0.0-beta.10");

        var result = await service.CheckForUpdateAsync(UpdateChannels.Stable);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.0.0", result.LatestVersion);
        Assert.Single(result.Files);
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

    /// <summary>
    /// Builds a transport that answers the server proxy releases endpoint with
    /// <paramref name="proxyReleasesJson"/> (or fails it with
    /// <paramref name="proxyFailure"/>) and the GitHub releases endpoint with
    /// <paramref name="gitHubReleasesJson"/>, mirroring the server-to-GitHub
    /// fallback discipline under test.
    /// </summary>
    private static StubRemoteHttpTransport CreateReleasesTransport(
        string proxyReleasesJson = "",
        string? gitHubReleasesJson = null,
        Exception? proxyFailure = null) =>
        new(uri =>
        {
            if (uri == ProxyReleasesUri)
            {
                return (object?)proxyFailure ?? proxyReleasesJson;
            }

            if (uri == GitHubReleasesUri)
            {
                return gitHubReleasesJson
                    ?? throw new InvalidOperationException($"Unexpected request to {uri} without GitHub release data.");
            }

            throw new InvalidOperationException($"Unexpected request URI: {uri}");
        });
}
