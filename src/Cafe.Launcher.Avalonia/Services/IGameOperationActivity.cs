using System.ComponentModel;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// "游戏操作活动"窄视图：下载运行/暂停状态、可暂停判定，以及暂停/恢复与停止控制。
/// 由 GameOperationsViewModel 实现；消费者是窗口 chrome（停止手势）与诊断面板，
/// 两者都只依赖本抽象而非 GameOperations 本体，避免窗口层／Diagnostics 与
/// GameOperations 产生横向耦合。放在共享 Services 层使各 Feature 都只向下依赖。
/// </summary>
public interface IGameOperationActivity
{
    /// <summary>Raised when IsDownloadRunning, IsPaused, or CanPauseOperation changes.</summary>
    event PropertyChangedEventHandler? ActivityPropertyChanged;

    /// <summary>Gets whether a download or repair workflow is currently running.</summary>
    bool IsDownloadRunning { get; }

    /// <summary>Gets whether the active download workflow is paused.</summary>
    bool IsPaused { get; }

    /// <summary>Gets whether the active download can be paused right now.</summary>
    bool CanPauseOperation { get; }

    /// <summary>Toggles pause/resume for the active download.</summary>
    void PauseResume();

    /// <summary>
    /// 用户按下停止：域自行决定是否先确认（有活动下载时弹确认框，否则直接停止）。
    /// </summary>
    void RequestStop();

    /// <summary>
    /// 按给定意图立即停止活动工作流——「关窗并停止」与生命周期退出走这里。
    /// 调用方只表达意图，检查点去留由域翻译。
    /// </summary>
    void StopOperation(GameOperationStopIntent intent);
}
