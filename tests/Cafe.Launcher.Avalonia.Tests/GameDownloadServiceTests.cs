using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Auth;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class GameDownloadServiceTests
{
    [Fact]
    public void GetSafePath_WhenPathIsRelative_ReturnsPathInsideGameDirectory()
    {
        var gamePath = Path.Combine(Path.GetTempPath(), "YostarGames", "BlueArchive_JP");

        var result = GamePathValidator.GetSafePath(gamePath, "data/file.bin");

        Assert.Equal(Path.Combine(Path.GetFullPath(gamePath), "data", "file.bin"), result);
    }

    [Theory]
    [InlineData("../outside.bin")]
    [InlineData("..\\outside.bin")]
    [InlineData("data/../../outside.bin")]
    public void GetSafePath_WhenPathEscapesGameDirectory_Throws(string relativePath)
    {
        var gamePath = Path.Combine(Path.GetTempPath(), "YostarGames", "BlueArchive_JP");

        Assert.Throws<InvalidOperationException>(() => GamePathValidator.GetSafePath(gamePath, relativePath));
    }

    [Fact]
    public void Dispose_WhenCalledTwice_DoesNotThrow()
    {
        using var apiClient = new LauncherApiClient(new AuthorizationHeaderFactory(), new PatchUrlGroupService(), new ProxySettingsService());
        var service = CreateService(apiClient);

        service.Dispose();
        service.Dispose();
    }

    [Fact]
    public void Dispose_AfterStop_DoesNotThrow()
    {
        using var apiClient = new LauncherApiClient(new AuthorizationHeaderFactory(), new PatchUrlGroupService(), new ProxySettingsService());
        var service = CreateService(apiClient);

        service.Stop();
        service.Dispose();
    }

    [Fact]
    public void RetryDomainOrder_MatchesCafeLauncherOld()
    {
        Assert.Equal([1, 1, 1, 1, 0, 0, 0, 1, 1, 1], GameDownloadService.RetryDomainOrder);
    }

    [Fact]
    public void BuildDownloadUrl_WhenCafeGroupCdnConfigIsUsed_UsesCafePackageHost()
    {
        var patchUrlGroupService = new PatchUrlGroupService();
        var cdnConfig = patchUrlGroupService.RewriteCdnConfig(
            new CdnConfigResponse
            {
                PrimaryCdn = "https://launcher-pkg-ba-jp.yo-star.com",
                BackUpCdn = "https://launcher-pkg-ba-jp.yo-star.com"
            },
            PatchUrlGroups.Cafe);

        var url = GameDownloadService.BuildDownloadUrl(
            cdnConfig.PrimaryCdn,
            "/source/root",
            "/data/file name.bin");

        Assert.Equal("https://launcher-pkg-ba-jp.bluearchive.cafe/source/root/data/file%20name.bin", url);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenNoFilesNeedChanges_ClearsDownloadState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        var settingsPath = Path.Combine(tempDir, "settings.json");
        var statePath = Path.Combine(tempDir, "download_state.json");
        var settingsService = new LauncherSettingsService(settingsPath);
        await settingsService.SaveAsync(new LauncherSettings { GamePath = gamePath });
        await WriteLocalGameFilesAsync(gamePath);
        var downloadStateService = new DownloadStateService(statePath);
        using var apiClient = CreateManifestApiClient();
        var service = CreateService(apiClient, settingsService, downloadStateService);

        var result = await service.InstallOrUpdateAsync(CreateSnapshot(gamePath), _ => { });

        Assert.True(result.Success);
        Assert.False(File.Exists(statePath));
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task ResumePersistedAsync_WhenStateDoesNotMatchCurrentVersion_ClearsDownloadState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var statePath = Path.Combine(tempDir, "download_state.json");
        var downloadStateService = new DownloadStateService(statePath);
        await downloadStateService.SaveAsync(new DownloadTaskState
        {
            Version = "0.9.0",
            Basis = "manifest.json",
            GamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP")
        });
        using var apiClient = CreateManifestApiClient();
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        var service = CreateService(apiClient, settingsService, downloadStateService);

        var result = await service.ResumePersistedAsync(CreateSnapshot(Path.Combine(tempDir, "YostarGames", "BlueArchive_JP")), _ => { });

        Assert.Null(result);
        Assert.False(File.Exists(statePath));
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task ResumePersistedAsync_WhenStateUsesDifferentPatchUrlGroup_ClearsDownloadState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        var statePath = Path.Combine(tempDir, "download_state.json");
        var downloadStateService = new DownloadStateService(statePath);
        await downloadStateService.SaveAsync(new DownloadTaskState
        {
            Version = "1.0.0",
            Basis = "manifest.json",
            GamePath = gamePath,
            PatchUrlGroup = PatchUrlGroups.Official
        });
        using var apiClient = CreateManifestApiClient();
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        var service = CreateService(apiClient, settingsService, downloadStateService);
        var snapshot = CreateSnapshot(gamePath);
        snapshot.Settings.PatchUrlGroup = PatchUrlGroups.Cafe;

        var result = await service.ResumePersistedAsync(snapshot, _ => { });

        Assert.Null(result);
        Assert.False(File.Exists(statePath));
        Directory.Delete(tempDir, recursive: true);
    }

    private static GameDownloadService CreateService(LauncherApiClient apiClient)
    {
        return CreateService(
            apiClient,
            new LauncherSettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json")),
            new DownloadStateService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "download_state.json")));
    }

    private static GameDownloadService CreateService(
        LauncherApiClient apiClient,
        LauncherSettingsService settingsService,
        DownloadStateService downloadStateService)
    {
        var localGameStateService = new LocalGameStateService();
        var diagnostics = new LocalDiagnostics();
        return new GameDownloadService(
            apiClient,
            localGameStateService,
            settingsService,
            new ProxySettingsService(),
            new Crc64Service(),
            new DiskSpaceService(),
            diagnostics,
            downloadStateService);
    }

    private static LauncherStatusSnapshot CreateSnapshot(string gamePath)
    {
        return new LauncherStatusSnapshot
        {
            Settings = new LauncherSettings { GamePath = gamePath },
            LocalGame = new LocalGameState { GamePath = gamePath },
            Remote = new LauncherRemoteState
            {
                GameConfig = new GameConfigResponse
                {
                    GameLatestVersion = "1.0.0",
                    GameLatestFilePath = "manifest.json",
                    GameStartExeName = "BlueArchive"
                },
                CdnConfig = new CdnConfigResponse
                {
                    PrimaryCdn = "https://cdn.example.invalid",
                    BackUpCdn = "https://backup.example.invalid"
                }
            }
        };
    }

    private static async Task WriteLocalGameFilesAsync(string gamePath)
    {
        var gameConfig = new GameLauncherConfig
        {
            Tag = "BlueArchive_JP",
            Name = "CafeLauncherAvaloniaTestGame",
            Version = "1.0.0"
        };
        var manifest = new LocalManifest
        {
            Name = "BlueArchive_JP",
            Version = "1.0.0",
            Basis = "manifest.json",
            Files = []
        };
        await File.WriteAllTextAsync(Path.Combine(gamePath, "game-launcher-config.json"), JsonSerializer.Serialize(gameConfig));
        await File.WriteAllTextAsync(Path.Combine(gamePath, "manifest.json"), JsonSerializer.Serialize(manifest));
    }

    private static LauncherApiClient CreateManifestApiClient()
    {
        return new LauncherApiClient(
            new ManifestHandler(),
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());
    }

    private sealed class ManifestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestUri = request.RequestUri?.ToString() ?? "";
            var json = requestUri.Contains("/api/launcher/game/config/json", StringComparison.Ordinal)
                ? "{\"code\":200,\"data\":{\"url\":\"https://manifest.example.invalid/manifest.json\"}}"
                : "{\"source\":\"\",\"file\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
