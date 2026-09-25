namespace Cafe.Launcher.Avalonia.Services.Update;

/// <summary>
/// Hands a verified, staged package to the detached helper. The caller shuts the
/// launcher down once this returns true; a false result means the update could not
/// be started and the current version stays in place.
/// </summary>
public interface IWindowsLauncherUpdateApplier
{
    /// <summary>
    /// Whether this installation ships the detached helper the apply step runs. Only where
    /// this is true does an in-app update path exist at all: without the helper a verified
    /// package could be downloaded but never applied, so the dialog offers the release page
    /// instead of starting a download that is guaranteed to end in a fallback.
    /// </summary>
    bool IsAvailable { get; }

    bool TryStartApply(LauncherSelfUpdatePreparation preparation);

    /// <summary>
    /// Best-effort removal of the helper copies previous updates left under the temp
    /// root. Only the GUID-named directories this class creates are eligible; a helper
    /// that is somehow still running keeps its directory through the Windows file lock
    /// and simply loses it on a later launch.
    /// </summary>
    void CleanupAbandonedHelperDirectories();
}
