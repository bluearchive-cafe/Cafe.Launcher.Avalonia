using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class PatchUrlGroupServiceTests
{
    [Fact]
    public void RewritePackageUrl_WhenOfficial_ReturnsOriginalUrl()
    {
        var service = new PatchUrlGroupService();
        const string url = "https://launcher-pkg-ba-jp.yo-star.com/zip_online_config_json/test.json";

        var result = service.RewritePackageUrl(url, PatchUrlGroups.Official);

        Assert.Equal(url, result);
    }

    [Fact]
    public void RewritePackageUrl_WhenCafe_RewritesPackageHost()
    {
        var service = new PatchUrlGroupService();
        const string url = "https://launcher-pkg-ba-jp.yo-star.com/zip_online_config_json/test.json";

        var result = service.RewritePackageUrl(url, PatchUrlGroups.Cafe);

        Assert.Equal("https://launcher-pkg-ba-jp.bluearchive.cafe/zip_online_config_json/test.json", result);
    }

    [Fact]
    public void RewriteCdnConfig_WhenCafe_RewritesPrimaryAndBackupCdn()
    {
        var service = new PatchUrlGroupService();
        var cdn = new CdnConfigResponse
        {
            PrimaryCdn = "https://launcher-pkg-ba-jp.yo-star.com",
            BackUpCdn = "https://launcher-pkg-ba-jp.yo-star.com/backup"
        };

        var result = service.RewriteCdnConfig(cdn, PatchUrlGroups.Cafe);

        Assert.Equal("https://launcher-pkg-ba-jp.bluearchive.cafe", result.PrimaryCdn);
        Assert.Equal("https://launcher-pkg-ba-jp.bluearchive.cafe/backup", result.BackUpCdn);
    }

    [Fact]
    public void RewritePackageUrl_WhenInvalid_ReturnsOriginalUrl()
    {
        var service = new PatchUrlGroupService();
        const string url = "https://launcher-pkg-ba-jp.yo-star.com/zip_online_config_json/test.json";

        var result = service.RewritePackageUrl(url, "invalid");

        Assert.Equal(url, result);
    }

    [Theory]
    [InlineData("https://example.invalid/launcher-pkg-ba-jp.yo-star.com/file.bin")]
    [InlineData("https://example.invalid/file.bin?host=launcher-pkg-ba-jp.yo-star.com")]
    [InlineData("https://launcher-pkg-ba-jp.yo-star.com.example.invalid/file.bin")]
    public void RewritePackageUrl_WhenPackageHostAppearsOutsideExactHost_ReturnsOriginalUrl(string url)
    {
        var service = new PatchUrlGroupService();

        var result = service.RewritePackageUrl(url, PatchUrlGroups.Cafe);

        Assert.Equal(url, result);
    }

    /// <summary>
    /// Sentinel: ensures the URL rewriting scope is strictly limited to the package download
    /// host. If future changes add serverinfo, SDK netloc, or status/list rewriting, this test
    /// will fail to flag that those endpoints are being touched.
    /// </summary>
    [Fact]
    public void Resolve_WhenCafe_DoesNotExposeStatusListOrGameConfigPatchTargets()
    {
        var service = new PatchUrlGroupService();

        var group = service.Resolve(PatchUrlGroups.Cafe);

        Assert.Equal(PatchUrlGroups.Cafe, group.Code);
        Assert.DoesNotContain("status/list", group.PackageHostFrom);
        Assert.DoesNotContain("status/list", group.PackageHostTo);
        Assert.DoesNotContain("serverinfo", group.PackageHostFrom);
        Assert.DoesNotContain("serverinfo", group.PackageHostTo);
        Assert.DoesNotContain("SdkNetloc", group.PackageHostFrom);
        Assert.DoesNotContain("SdkNetloc", group.PackageHostTo);
    }
}
