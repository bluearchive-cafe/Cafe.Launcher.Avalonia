using System;
using System.Collections.Generic;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services.Update;

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
