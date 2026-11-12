using System;
using System.Collections.Generic;
using System.IO;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services.Update;

/// <summary>
/// Picks the release asset a Windows self-update should download. Windows x64 only:
/// the installer build takes the Inno setup executable, the portable build takes the
/// win-x64 zip. Any other host, or a release that is missing either the package or the
/// SHA256SUMS manifest, degrades to the browser hand-off rather than applying unverified
/// bits — and says which of the two it is, so the dialog can name the cause.
/// </summary>
internal static class LauncherUpdatePackageSelector
{
    /// <summary>The asset that carries the per-file SHA-256 digests for one release.</summary>
    internal const string ChecksumManifestFileName = "SHA256SUMS";

    /// <summary>Suffix of the Inno installer asset for the installer build.</summary>
    internal const string WindowsInstallerSuffix = "_setup.exe";

    /// <summary>Suffix of the portable archive for the unpacked build.</summary>
    internal const string WindowsPortableSuffix = "_win-x64.zip";

    /// <summary>
    /// Resolves the update selection for <paramref name="host"/> against the release assets.
    /// </summary>
    public static LauncherUpdateSelection Select(
        LauncherUpdateHostInfo host,
        IReadOnlyList<ReleaseFile> files)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(files);

        if (!host.IsWindows || !host.IsX64)
        {
            return LauncherUpdateSelection.External(LauncherUpdateInAppAvailability.PlatformUnsupported);
        }

        var packageSuffix = host.IsInstallerInstall
            ? WindowsInstallerSuffix
            : WindowsPortableSuffix;
        var package = FindBySuffix(files, packageSuffix);
        var checksumManifest = FindByName(files, ChecksumManifestFileName);
        if (package is null || checksumManifest is null)
        {
            return LauncherUpdateSelection.External(LauncherUpdateInAppAvailability.PackageUnverifiable);
        }

        var target = host.IsInstallerInstall
            ? LauncherUpdateTarget.WindowsInstaller
            : LauncherUpdateTarget.WindowsPortable;
        return new LauncherUpdateSelection(
            target,
            package,
            checksumManifest,
            LauncherUpdateInAppAvailability.Available);
    }

    /// <summary>
    /// Whether <paramref name="name"/> can be used verbatim as one staged file name under the
    /// update directory: a single segment that is neither empty, <c>"."</c>, <c>".."</c>, nor
    /// carrying a separator or an invalid file-name character.
    /// </summary>
    /// <remarks>
    /// 资产名来自发布元数据（或代理应答），而它最终会成为数据根下的文件名，所以必须在这里
    /// 就拒绝越界形态，而不是依赖拼接后再看结果。'/' 与 '\' 都显式检查：Windows 把两者都当
    /// 分隔符，Unix 只把 '/' 当分隔符，只靠 <c>Path.GetFileName</c> 会让
    /// <c>..\..\x_setup.exe</c> 在 Unix 上原样通过、再由 Windows 消费方重新解释成上跳。
    /// </remarks>
    internal static bool IsSingleFileNameSegment(string? name)
    {
        if (string.IsNullOrEmpty(name)
            || name == "."
            || name == ".."
            || name.Contains('/')
            || name.Contains('\\'))
        {
            return false;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        // 驱动器前缀、尾随分隔符等仍可能让取文件名结果与原串不同；必须原样往返。
        return string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal);
    }

    /// <summary>Finds the single asset whose name ends with <paramref name="suffix"/>, case-insensitively.</summary>
    internal static ReleaseFile? FindBySuffix(IReadOnlyList<ReleaseFile> files, string suffix)
    {
        foreach (var file in files)
        {
            if (file.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }

    /// <summary>Finds the asset with exactly <paramref name="name"/> (ordinal, case-insensitive).</summary>
    internal static ReleaseFile? FindByName(IReadOnlyList<ReleaseFile> files, string name)
    {
        foreach (var file in files)
        {
            if (string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }
}
