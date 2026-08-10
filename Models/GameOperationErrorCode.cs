namespace Cafe.Launcher.Avalonia.Models;

/// <summary>Identifies a presentation-independent game operation failure.</summary>
public enum GameOperationErrorCode
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
