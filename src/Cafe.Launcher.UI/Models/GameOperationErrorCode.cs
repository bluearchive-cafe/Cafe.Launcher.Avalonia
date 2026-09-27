namespace Cafe.Launcher.UI.Models;

/// <summary>Identifies a presentation-independent game operation failure.</summary>
internal enum GameOperationErrorCode
{
    None,
    InvalidState,
    RemoteConfiguration,
    PathMissing,
    CdnConfiguration,
    GameRunning,
    InsufficientDiskSpace,
    Network,
    System,
    Stopped,
    Uninstall,
}
