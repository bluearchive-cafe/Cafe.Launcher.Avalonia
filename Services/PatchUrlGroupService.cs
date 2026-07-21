using System;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class PatchUrlGroupService
{
    private const string CafePackageHost = "launcher-pkg-ba-jp.bluearchive.cafe";

    public PatchUrlGroupDefinition Resolve(string? group)
    {
        return group == PatchUrlGroups.Cafe
            ? new PatchUrlGroupDefinition
            {
                Code = PatchUrlGroups.Cafe,
                PackageHostFrom = ApiConfig.OfficialPackageHost,
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
        if (string.IsNullOrWhiteSpace(definition.PackageHostFrom)
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, definition.PackageHostFrom, StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        var authority = uri.GetLeftPart(UriPartial.Authority);
        var hostIndex = authority.IndexOf(uri.Host, StringComparison.OrdinalIgnoreCase);
        return hostIndex < 0
            ? text
            : string.Concat(
                text.AsSpan(0, hostIndex),
                definition.PackageHostTo,
                text.AsSpan(hostIndex + uri.Host.Length));
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
