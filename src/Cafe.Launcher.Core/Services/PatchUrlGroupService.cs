using System;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// 补丁下载源的 URL 归属改写：官方主机与产品自有镜像主机之间互转。
/// 两个主机分别来自游戏档案与产品档案——官方主机随游戏变，镜像主机归产品方所有。
/// </summary>
internal sealed class PatchUrlGroupService
{
    private readonly YostarGameProfile gameProfile;
    private readonly LauncherProductProfile productProfile;

    public PatchUrlGroupService(
        YostarGameProfile gameProfile,
        LauncherProductProfile productProfile)
    {
        this.gameProfile = gameProfile;
        this.productProfile = productProfile;
    }

    public PatchUrlGroupDefinition Resolve(string? group)
    {
        return group == PatchUrlGroups.Cafe
            ? new PatchUrlGroupDefinition
            {
                Code = PatchUrlGroups.Cafe,
                PackageHostFrom = gameProfile.OfficialPackageHost,
                PackageHostTo = productProfile.CafePackageHost
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
        return RewritePackageHost(
            value ?? "",
            productProfile.CafePackageHost,
            gameProfile.OfficialPackageHost);
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
        if (group == PatchUrlGroups.Cafe)
        {
            // The Cafe mirror is a single host; the official backup path does not
            // exist there, so primary and backup share the same URL.
            response.BackUpCdn = response.PrimaryCdn;
        }

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
