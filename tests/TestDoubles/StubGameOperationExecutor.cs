using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Testing;

/// <summary>
/// <see cref="IGameOperationExecutor"/>（internal）的共享测试替身，由两个测试工程通过
/// csproj Link 共用。记录每个操作的调用次数与 Stop 参数；操作结果、抛出异常、完成门
/// （TaskCompletionSource）均可配置。默认构造与各 ViewModel 测试原有 fake 一致：所有
/// 操作返回未成功的默认结果；<see cref="Succeeding"/> 与旅程测试原
/// RecordingOperationExecutor 一致：所有操作默认成功。<see cref="IsDownloadRunning"/>
/// 仅在值真正变化时触发 <see cref="IsRunningChanged"/>（与原 Debug/GameOperations 两个
/// ViewModel 测试的 fake 一致）。
/// </summary>
internal sealed class StubGameOperationExecutor : IGameOperationExecutor
{
    private bool isDownloadRunning;

    /// <inheritdoc />
    public event Action? IsRunningChanged;

    /// <summary>仅读时表示当前下载运行状态；写时值变化会触发 <see cref="IsRunningChanged"/>。</summary>
    public bool IsDownloadRunning
    {
        get => isDownloadRunning;
        set
        {
            if (isDownloadRunning == value)
            {
                return;
            }

            isDownloadRunning = value;
            IsRunningChanged?.Invoke();
        }
    }

    /// <inheritdoc />
    public bool IsPaused { get; set; }

    public int LaunchCallCount { get; private set; }

    public int InstallCallCount { get; private set; }

    public int RepairCallCount { get; private set; }

    public int ValidateUninstallCallCount { get; private set; }

    public int UninstallCallCount { get; private set; }

    public int ResumeCallCount { get; private set; }

    public int StopCallCount { get; private set; }

    /// <summary>Gets 最近一次 Stop 收到的 clearPersistedState 参数；尚未调用过 Stop 时为 null。</summary>
    public bool? LastStopClearPersistedState { get; private set; }

    /// <summary>Launch 的返回结果；Validation 有默认实例，无需额外初始化。</summary>
    public GameLaunchResult LaunchResult { get; set; } = new();

    /// <summary>设置后 Launch 同步抛出该异常，用于验证旅程的错误处理路径。</summary>
    public Exception? LaunchException { get; set; }

    public GameOperationResult InstallResult { get; set; } = new();

    /// <summary>设置后 InstallOrUpdate 同步抛出该异常（与原 GameOperationsViewModel fake 一致）。</summary>
    public Exception? InstallException { get; set; }

    /// <summary>设置后 InstallOrUpdate 返回其 Task，用于把操作挂起在下载阶段。</summary>
    public TaskCompletionSource<GameOperationResult>? InstallCompletion { get; set; }

    public GameOperationResult RepairResult { get; set; } = new();

    public GameOperationResult ValidateUninstallResult { get; set; } = new();

    public GameOperationResult UninstallResult { get; set; } = new();

    /// <summary>设置后 Uninstall 返回其 Task，用于把操作挂起在卸载阶段。</summary>
    public TaskCompletionSource<GameOperationResult>? UninstallCompletion { get; set; }

    /// <summary>Resume 的返回结果；null 表示无可恢复的持久化工作。</summary>
    public GameOperationResult? ResumeResult { get; set; }

    /// <summary>创建所有操作默认成功（并带典型成功消息）的替身，供直接走成功路径的旅程测试使用。</summary>
    public static StubGameOperationExecutor Succeeding() => new()
    {
        LaunchResult = new GameLaunchResult { Success = true, Message = "launched" },
        InstallResult = new GameOperationResult { Success = true, Message = "done" },
        RepairResult = new GameOperationResult { Success = true, Message = "repaired" },
        ValidateUninstallResult = new GameOperationResult { Success = true, Message = "ok" },
        UninstallResult = new GameOperationResult { Success = true, Message = "uninstalled" }
    };

    /// <inheritdoc />
    public Task<GameLaunchResult> LaunchAsync(
        LauncherStatusSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        LaunchCallCount++;
        if (LaunchException is not null)
        {
            throw LaunchException;
        }

        return Task.FromResult(LaunchResult);
    }

    /// <inheritdoc />
    public Task<GameOperationResult> InstallOrUpdateAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        InstallCallCount++;
        if (InstallException is not null)
        {
            throw InstallException;
        }

        return InstallCompletion?.Task ?? Task.FromResult(InstallResult);
    }

    /// <inheritdoc />
    public Task<GameOperationResult> RepairAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress)
    {
        RepairCallCount++;
        return Task.FromResult(RepairResult);
    }

    /// <inheritdoc />
    public Task<GameOperationResult?> ResumePersistedAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        ResumeCallCount++;
        return Task.FromResult(ResumeResult);
    }

    /// <inheritdoc />
    public Task<GameOperationResult> ValidateUninstallAsync(string gamePath)
    {
        ValidateUninstallCallCount++;
        return Task.FromResult(ValidateUninstallResult);
    }

    /// <inheritdoc />
    public Task<GameOperationResult> UninstallAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress)
    {
        UninstallCallCount++;
        return UninstallCompletion?.Task ?? Task.FromResult(UninstallResult);
    }

    /// <inheritdoc />
    public void Stop(bool clearPersistedState)
    {
        StopCallCount++;
        LastStopClearPersistedState = clearPersistedState;
        IsDownloadRunning = false;
    }

    /// <inheritdoc />
    public void Pause() => IsPaused = true;

    /// <inheritdoc />
    public void Resume() => IsPaused = false;
}
