using System;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class PatchUrlGroupService
{
    private const string OfficialPackageHost = "launcher-pkg-ba-jp.yo-star.com";
    private const string CafePackageHost = "launcher-pkg-ba-jp.bluearchive.cafe";

    public PatchUrlGroupDefinition Resolve(string? group)
    {
        return group == PatchUrlGroups.Cafe
            ? new PatchUrlGroupDefinition
            {
                Code = PatchUrlGroups.Cafe,
                PackageHostFrom = OfficialPackageHost,
                PackageHostTo = CafePackageHost
            }
            : new PatchUrlGroupDefinition
            {
                Code = PatchUrlGroups.Official
            };
    }

    public string RewritePackageUrl(string? value, string? group)
    {
        var text = value ?? "";
        var definition = Resolve(group);
        return string.IsNullOrWhiteSpace(definition.PackageHostFrom)
            ? text
            : text.Replace(definition.PackageHostFrom, definition.PackageHostTo, StringComparison.Ordinal);
    }

    public ManifestUrlResponse RewriteManifestUrl(ManifestUrlResponse response, string? group)
    {
        response.Url = RewritePackageUrl(response.Url, group);
        return response;
    }

    public CdnConfigResponse RewriteCdnConfig(CdnConfigResponse response, string? group)
    {
        response.PrimaryCdn = RewritePackageUrl(response.PrimaryCdn, group);
        response.BackUpCdn = RewritePackageUrl(response.BackUpCdn, group);
        return response;
    }
}
