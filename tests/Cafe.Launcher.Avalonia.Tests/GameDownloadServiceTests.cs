using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Models;

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
        using var apiClient = new LauncherApiClient();
        var service = CreateService(apiClient);

        service.Dispose();
        service.Dispose();
    }

    [Fact]
    public void Dispose_AfterStop_DoesNotThrow()
    {
        using var apiClient = new LauncherApiClient();
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

    private static GameDownloadService CreateService(LauncherApiClient apiClient)
    {
        var localGameStateService = new LocalGameStateService();
        var diagnostics = new LocalDiagnostics();
        return new GameDownloadService(
            apiClient,
            localGameStateService,
            new LauncherSettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json")),
            new ProxySettingsService(),
            new Crc64Service(),
            new DiskSpaceService(),
            diagnostics,
            new DownloadStateService());
    }
}
