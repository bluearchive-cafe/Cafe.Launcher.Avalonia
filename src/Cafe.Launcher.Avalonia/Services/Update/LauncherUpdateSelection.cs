using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services.Update;

/// <summary>
/// The result of matching a release against the current host: which package to fetch, the
/// checksum manifest that must accompany it, and why an in-app apply is possible or not.
/// </summary>
internal sealed record LauncherUpdateSelection(
    LauncherUpdateTarget Target,
    ReleaseFile? Package,
    ReleaseFile? ChecksumManifest,
    LauncherUpdateInAppAvailability Availability)
{
    /// <summary>
    /// The browser hand-off selection, tagged with the release-side reason this host cannot
    /// apply the offered release itself. Reasons that belong to the host (a missing helper)
    /// are not the selector's to answer and are resolved by the self-update service.
    /// </summary>
    public static LauncherUpdateSelection External(LauncherUpdateInAppAvailability reason) =>
        new(LauncherUpdateTarget.ExternalDownload, Package: null, ChecksumManifest: null, reason);
}
