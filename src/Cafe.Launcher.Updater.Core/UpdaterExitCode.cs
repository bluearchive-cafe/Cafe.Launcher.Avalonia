namespace Cafe.Launcher.Updater;

/// <summary>
/// Stable process exit codes for the launcher's private updater protocol.
/// </summary>
public enum UpdaterExitCode
{
    Success = 0,
    UnexpectedFailure = 1,
    PackageIntegrityFailure = 2,
    InstallerStartFailure = 3,
    InstallerFailure = 4,
    LauncherStartFailure = 5,
    PackageExtractionFailure = 6,
    BackupMoveFailure = 7,
    InstallMoveFailure = 8,
    PreviousVersionRestored = 9,
    PackageLayoutInvalid = 10,
    ParentExitTimeout = 11,
    Usage = 64
}
