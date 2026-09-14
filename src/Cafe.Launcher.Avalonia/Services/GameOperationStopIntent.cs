namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// 停止活动工作流的意图。调用方只表达「为什么停」，检查点去留由游戏操作域自己决定：
/// 那套策略词表留在域内，域外只认这两条意图（见 ADR-026）。
/// </summary>
public enum GameOperationStopIntent
{
    /// <summary>用户显式停止，含「关闭窗口并停止」手势：丢弃持久化检查点与临时进度。</summary>
    UserStop,

    /// <summary>
    /// 进程生命周期退出、且会话未被用户显式停止（关机、会话结束、托盘退出）：
    /// 保留检查点，下次启动可续传。注意「关窗并停止」属于 <see cref="UserStop"/>，
    /// 不是本值——那是用户的显式停止手势。
    /// </summary>
    ProcessExit
}
