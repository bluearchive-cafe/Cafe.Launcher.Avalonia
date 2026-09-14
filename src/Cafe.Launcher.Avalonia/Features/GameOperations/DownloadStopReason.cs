namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// 下载停止的语义原因：决定持久化检查点的去留。它是本功能内的策略词表——
/// 域外的调用方表达意图（<c>GameOperationStopIntent</c>），翻译发生在
/// <c>GameOperationsViewModel</c>，见 ADR-026。
/// </summary>
public enum DownloadStopReason
{
    /// <summary>
    /// 用户显式停止，含「关闭窗口并停止」手势：丢弃持久化检查点与临时进度，不自动续传。
    /// </summary>
    UserRequested,

    /// <summary>
    /// 进程生命周期退出、且会话未被用户显式停止（关机、会话结束、托盘退出）：
    /// 保留检查点，下次启动可续传。
    /// </summary>
    ApplicationExit
}
