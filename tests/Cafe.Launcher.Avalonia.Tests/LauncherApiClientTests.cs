using System.Net.Http;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Auth;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LauncherApiClientTests
{
    [Fact]
    public void RewriteManifestUrl_WhenCafe_RewritesPackageHost()
    {
        using var client = new LauncherApiClient(new HttpClientHandler(), new AuthorizationHeaderFactory(), new PatchUrlGroupService());
        var response = new ManifestUrlResponse
        {
            Url = "https://launcher-pkg-ba-jp.yo-star.com/zip_online_config_json/test.json"
        };

        var result = client.RewriteManifestUrl(response, PatchUrlGroups.Cafe);

        Assert.Equal("https://launcher-pkg-ba-jp.bluearchive.cafe/zip_online_config_json/test.json", result.Url);
    }

    [Fact]
    public void RewriteCdnConfig_WhenCafe_RewritesPackageHosts()
    {
        using var client = new LauncherApiClient(new HttpClientHandler(), new AuthorizationHeaderFactory(), new PatchUrlGroupService());
        var response = new CdnConfigResponse
        {
            PrimaryCdn = "https://launcher-pkg-ba-jp.yo-star.com",
            BackUpCdn = "https://launcher-pkg-ba-jp.yo-star.com/backup"
        };

        var result = client.RewriteCdnConfig(response, PatchUrlGroups.Cafe);

        Assert.Equal("https://launcher-pkg-ba-jp.bluearchive.cafe", result.PrimaryCdn);
        Assert.Equal("https://launcher-pkg-ba-jp.bluearchive.cafe/backup", result.BackUpCdn);
    }
}
