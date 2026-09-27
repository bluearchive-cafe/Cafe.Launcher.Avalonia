using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// 游戏操作结果工厂（D5）：原先挂在 <see cref="DownloadSession"/> 上的静态
/// <c>Failed</c> 原样搬来，让有状态的会话类型不再承载无状态的构造面。
/// </summary>
internal static class GameOperationOutcomes
{
    /// <summary>Creates a failed <see cref="GameOperationResult"/> with the given details.</summary>
    internal static GameOperationResult Failed(
        string message,
        GameOperationErrorCode errorCode,
        int affectedFileCount = 0,
        int failedFileCount = 0)
    {
        return new GameOperationResult
        {
            Success = false,
            Message = message,
            ErrorCode = errorCode,
            AffectedFileCount = affectedFileCount,
            FailedFileCount = failedFileCount
        };
    }
}
