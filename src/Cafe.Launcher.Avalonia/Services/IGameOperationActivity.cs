using System.ComponentModel;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// 诊断面板所需的"游戏操作活动"窄视图：下载运行/暂停状态、可暂停判定，
/// 以及暂停/恢复与停止控制。由 GameOperationsViewModel 实现；DebugViewModel
/// 只依赖本抽象而非 GameOperations 本体，避免 Diagnostics 与 GameOperations
/// 两个 Feature 产生横向耦合。放在共享 Services 层使两个 Feature 都只向下依赖。
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

    /// <summary>Stops the active download workflow.</summary>
    void StopOperation();
}
