using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// 游戏操作进度快照工厂（D5）：原先挂在 <see cref="DownloadSession"/> 上的静态
/// <c>CreateProgress</c> 原样搬来，供会话与 diff 计算共用。
/// </summary>
internal static class GameOperationProgressFactory
{
    /// <summary>Builds a progress snapshot for a phase boundary of an operation.</summary>
    internal static GameOperationProgress CreateProgress(
        GameOperationKind kind,
        GameOperationStage stage,
        int value)
    {
        return new GameOperationProgress
        {
            OperationKind = kind,
            Stage = stage,
            Progress = value,
            IsRunning = true,
            CanStop = kind is GameOperationKind.Download or GameOperationKind.Repair,
            CanPause = false
        };
    }
}
