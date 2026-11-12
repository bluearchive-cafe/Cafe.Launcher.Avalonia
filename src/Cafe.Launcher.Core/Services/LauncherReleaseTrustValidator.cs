using System;
using System.Collections.Generic;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services.Update;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// 自更新信任链的元数据校验：声明的版本、下载 URL 的 release tag 与 Windows 包名必须
/// 指向同一个版本，镜像地址必须落在产品档案声明的基址下。
/// </summary>
/// <remarks>
/// 与「检查更新」分开的理由是这条判断的用途单一且失败即拒绝：代理端可以声明一个高版本、
/// 却把包与校验清单指向旧版本资产，此时哈希校验会把降级当成可信更新放行。校验通过后还会
/// 就地清掉不吻合的镜像地址，让下载器退回已校验的 GitHub 地址。
/// </remarks>
internal static class LauncherReleaseTrustValidator
{
    /// <summary>
    /// Validates one release's asset metadata with the self-update trust chain bound:
    /// the declared version, the download URLs' release tag, and the Windows package
    /// asset names must all carry the same version. A proxy that declares a high
    /// version while pointing the package and the checksum manifest at an older
    /// release's assets would otherwise pass hash verification as a trusted downgrade.
    /// </summary>
    public static bool TryValidateFiles(
        string version,
        IReadOnlyList<ReleaseFile>? files,
        string downloadPathPrefix,
        string mirrorBaseUrl,
        out string validationError)
    {
        if (files is null || files.Count == 0)
        {
            validationError = "files must contain at least one entry";
            return false;
        }

        // GitHub 把 tag 命名为 v{version}，资产下载路径是
        // /releases/download/v{version}/；用完整前缀同时钉住仓库与 tag。
        var expectedDownloadPrefix = downloadPathPrefix + "v" + version + "/";

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            if (string.IsNullOrWhiteSpace(file.Name))
            {
                validationError = $"files[{index}].name must not be empty";
                return false;
            }

            if (!IsReleaseDownloadUri(file.Url, expectedDownloadPrefix))
            {
                validationError = $"files[{index}].url must be a GitHub release download URL for v{version}";
                return false;
            }

            if (IsWindowsPackageAsset(file.Name))
            {
                // 包名会成为数据根下的落点文件名：越界形态（分隔符、"." / ".."、非法字符）
                // 在这里就拒绝，别让「路径拼接」替元数据做净化。
                if (!LauncherUpdatePackageSelector.IsSingleFileNameSegment(file.Name))
                {
                    validationError = $"files[{index}].name must be a single file name";
                    return false;
                }

                if (!file.Name.Contains("v" + version + "_", StringComparison.Ordinal))
                {
                    validationError = $"files[{index}].name must carry the release version v{version}";
                    return false;
                }
            }

            if (file.Size <= 0)
            {
                validationError = $"files[{index}].size must be greater than zero";
                return false;
            }

            // 镜像地址只在基址、版本路径与文件名完全吻合时才保留；不吻合的字段
            // 直接忽略，下载器退回已校验的 GitHub 地址，不让元数据把下载带到任意主机。
            if (!string.IsNullOrWhiteSpace(file.MirrorUrl)
                && !IsMirrorDownloadUri(file.MirrorUrl, version, mirrorBaseUrl, file.Name))
            {
                file.MirrorUrl = null;
            }
        }

        validationError = "";
        return true;
    }

    private static bool IsReleaseDownloadUri(string url, string downloadPathPrefix)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var downloadUri)
            && downloadUri.Scheme == Uri.UriSchemeHttps
            && string.Equals(downloadUri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            && downloadUri.AbsolutePath.StartsWith(
                downloadPathPrefix,
                StringComparison.Ordinal);
    }

    /// <summary>
    /// 镜像地址必须落在产品档案声明的基址下，且路径恰好是
    /// <c>v1/downloads/v{version}/{name}</c>；查询串与片段一律拒绝。
    /// </summary>
    private static bool IsMirrorDownloadUri(
        string url,
        string version,
        string mirrorBaseUrl,
        string fileName)
    {
        if (string.IsNullOrWhiteSpace(mirrorBaseUrl) || fileName.Contains('/'))
        {
            return false;
        }

        var baseWithSlash = mirrorBaseUrl.EndsWith('/') ? mirrorBaseUrl : mirrorBaseUrl + "/";
        if (!Uri.TryCreate(baseWithSlash, UriKind.Absolute, out var mirrorBase)
            || !Uri.TryCreate(url, UriKind.Absolute, out var mirror))
        {
            return false;
        }

        var expected = new Uri(
            mirrorBase,
            "v1/downloads/v" + version + "/" + Uri.EscapeDataString(fileName));

        return mirror.Scheme == Uri.UriSchemeHttps
            && expected.Scheme == Uri.UriSchemeHttps
            && string.Equals(mirror.Host, expected.Host, StringComparison.OrdinalIgnoreCase)
            && mirror.IsDefaultPort
            && expected.IsDefaultPort
            && string.Equals(
                Uri.UnescapeDataString(mirror.AbsolutePath),
                Uri.UnescapeDataString(expected.AbsolutePath),
                StringComparison.Ordinal)
            && string.IsNullOrEmpty(mirror.Query)
            && string.IsNullOrEmpty(mirror.Fragment);
    }

    /// <summary>
    /// The two assets the Windows self-update can apply in-app. Only their names are
    /// bound to the version: SHA256SUMS has no version segment by design, and other
    /// platforms' assets are only ever handed to the browser.
    /// </summary>
    private static bool IsWindowsPackageAsset(string name) =>
        name.EndsWith(LauncherUpdatePackageSelector.WindowsInstallerSuffix, StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(LauncherUpdatePackageSelector.WindowsPortableSuffix, StringComparison.OrdinalIgnoreCase);
}
