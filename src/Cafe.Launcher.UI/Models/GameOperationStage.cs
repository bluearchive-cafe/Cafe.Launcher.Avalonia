namespace Cafe.Launcher.UI.Models;

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
    /// <summary>扫描整棵卸载目标，条目总数尚未确定。</summary>
    UninstallScanning,
    Uninstalling,
    /// <summary>目录树处理结束，正在清理续传记录与快捷方式。</summary>
    UninstallCleanup,
}
