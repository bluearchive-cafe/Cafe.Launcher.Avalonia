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
            || string.IsNullOrWhiteSpace(definition.PackageHostTo))
        {
            return text;
        }

        return RewritePackageHost(text, definition.PackageHostFrom, definition.PackageHostTo);
    }

    /// <summary>
    /// Restores a Cafe package URL to the official package host. This is used only
    /// as a one-time fallback when the Cafe mirror does not have a manifest yet.
    /// </summary>
    public string RestoreOfficialPackageUrl(string? value)
    {
        return RewritePackageHost(value ?? "", CafePackageHost, ApiConfig.OfficialPackageHost);
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

    private static string RewritePackageHost(string text, string fromHost, string toHost)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, fromHost, StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        var authority = uri.GetLeftPart(UriPartial.Authority);
        var hostIndex = authority.IndexOf(uri.Host, StringComparison.OrdinalIgnoreCase);
        return hostIndex < 0
            ? text
            : string.Concat(
                text.AsSpan(0, hostIndex),
                toHost,
                text.AsSpan(hostIndex + uri.Host.Length));
    }
}
