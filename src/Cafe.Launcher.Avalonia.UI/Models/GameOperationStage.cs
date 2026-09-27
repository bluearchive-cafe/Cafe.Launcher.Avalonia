namespace Cafe.Launcher.Avalonia.Models;

/// <summary>Identifies a stable stage in a game operation workflow.</summary>
public enum GameOperationStage
{
    Idle,
    RepairConfirmation,
    Paused,
    RepairCheck,
    UpdateCheck,
    FileCheck,
    DiskCheck,
    VerificationRetry,
    VerificationFailed,
    RepairCompleted,
    DownloadCompleted,
    Stopped,
    Downloading,
    Uninstalling,
}
