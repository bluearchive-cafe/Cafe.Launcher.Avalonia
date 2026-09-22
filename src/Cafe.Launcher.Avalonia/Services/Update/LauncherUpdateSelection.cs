using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services.Update;

/// <summary>
/// The result of matching a release against the current host: which package to fetch,
/// the checksum manifest that must accompany it, and whether an in-app apply is possible.
/// </summary>
internal sealed record LauncherUpdateSelection(
    LauncherUpdateTarget Target,
    ReleaseFile? Package,
    ReleaseFile? ChecksumManifest)
{
    /// <summary>
    /// True when the host has a package to download and the manifest to verify it against.
    /// <see cref="LauncherUpdateTarget.ExternalDownload"/> is the only selection without both.
    /// </summary>
    public bool CanApplyInApp =>
        Target != LauncherUpdateTarget.ExternalDownload
        && Package is not null
        && ChecksumManifest is not null;

    /// <summary>The browser hand-off selection used for unsupported hosts.</summary>
    public static LauncherUpdateSelection External() =>
        new(LauncherUpdateTarget.ExternalDownload, Package: null, ChecksumManifest: null);
}
