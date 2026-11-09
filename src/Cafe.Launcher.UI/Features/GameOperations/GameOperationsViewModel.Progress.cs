using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Threading;
using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.UI.Features.GameOperations;

internal partial class GameOperationsViewModel
{
    private bool taskIsInstall;
    private bool isTaskWindowPresenting = true;
    private bool isTaskSurfaceUnobscured = true;
    private bool pendingTaskNotification;
    private DispatcherTimer? taskActivityTimer;
    private long lastTaskActivity;
    private long lastTransferActivity;
    private GameOperationProgress? taskProgress;
    private double smoothedDownloadRate;
    [ObservableProperty] private bool isTaskStalled;

    public bool IsTaskCardVisible => HasTask && !IsInstallPreparing;
    public bool ShowTaskPercent => IsTaskExecuting && !IsProgressIndeterminate;
    public bool ShowTaskProgress => IsTaskExecuting;
    public string TaskActionText => localizer.T(IsTaskStopping ? LocalizationKeys.TaskStopping
        : IsPaused ? LocalizationKeys.TaskDownloadPaused
        : IsTaskStalled ? LocalizationKeys.TaskWaitingProgress
        : displayedProgressStage switch
        {
            GameOperationStage.UpdateCheck or GameOperationStage.RepairCheck => LocalizationKeys.TaskChecking,
            GameOperationStage.Downloading when taskProgress?.IsCheckingDownload == true => LocalizationKeys.TaskChecking,
            GameOperationStage.FileCheck => LocalizationKeys.TaskVerifying,
            GameOperationStage.VerificationRetry => LocalizationKeys.TaskRetrying,
            GameOperationStage.Idle or GameOperationStage.DiskCheck or GameOperationStage.RepairConfirmation => LocalizationKeys.Preparing,
            GameOperationStage.ApplyingFiles or GameOperationStage.DownloadCompleted or GameOperationStage.RepairCompleted =>
                taskKind == GameOperationKind.Repair ? LocalizationKeys.Repairing : taskIsInstall ? LocalizationKeys.TaskInstalling : LocalizationKeys.TaskUpdating,
            _ => taskKind == GameOperationKind.Repair ? LocalizationKeys.Repairing
                : taskIsInstall ? LocalizationKeys.Downloading : LocalizationKeys.TaskUpdating
        });
    public string CompactTaskTitle => HasTaskResult ? TaskTitle
        : IsTaskStopping ? localizer.T(LocalizationKeys.TaskStopping)
        : IsPaused ? localizer.T(LocalizationKeys.TaskDownloadPaused)
        : IsTaskStalled ? localizer.T(LocalizationKeys.TaskWaitingProgress)
        : localizer.T(displayedProgressStage switch
        {
            GameOperationStage.Downloading => taskProgress?.IsCheckingDownload == true ? LocalizationKeys.TaskVerifying : LocalizationKeys.Downloading,
            GameOperationStage.UpdateCheck or GameOperationStage.RepairCheck => LocalizationKeys.TaskChecking,
            GameOperationStage.FileCheck => LocalizationKeys.TaskVerifying,
            GameOperationStage.VerificationRetry => LocalizationKeys.TaskRetrying,
            GameOperationStage.ApplyingFiles or GameOperationStage.DownloadCompleted or GameOperationStage.RepairCompleted => LocalizationKeys.TaskFinishing,
            _ => LocalizationKeys.Preparing
        });
    public string CompactTaskSummary => HasTaskResult ? TaskMessage
        : IsTaskStopping ? localizer.T(LocalizationKeys.TaskFinishing)
        : IsTaskStalled ? localizer.T(LocalizationKeys.TaskNoProgress)
        : IsPaused || taskProgress is { Stage: GameOperationStage.Downloading, IsCheckingDownload: false } ? ProgressSize
        : taskProgress is { TotalFileCount: > 0 } progress
            ? localizer.F(LocalizationKeys.TaskProcessedFiles, progress.ProcessedFileCount, progress.TotalFileCount)
        : ProgressDetail;
    public string CompactTaskSpeed => !IsTaskExecuting || IsPaused || IsTaskStopping || IsTaskStalled
        || taskProgress?.Stage != GameOperationStage.Downloading || taskProgress.IsCheckingDownload ? "" : ProgressSpeed;

    /// <summary>Only the foreground window can display a result toast; hidden results wait for restoration.</summary>
    public void SetTaskSurfaceVisibility(bool windowPresenting, bool unobscured)
    {
        isTaskWindowPresenting = windowPresenting;
        isTaskSurfaceUnobscured = unobscured;
        if (!windowPresenting || !pendingTaskNotification || !HasTaskResult) return;
        pendingTaskNotification = false;
        ShowTaskResultNotification();
    }

    private void StartTaskActivity()
    {
        taskProgress = null;
        smoothedDownloadRate = 0;
        IsTaskStalled = false;
        lastTaskActivity = lastTransferActivity = Stopwatch.GetTimestamp();
        if (Application.Current is null) return;
        if (taskActivityTimer is null)
        {
            taskActivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            taskActivityTimer.Tick += OnTaskActivityTick;
        }
        taskActivityTimer.Start();
    }

    private void OnTaskActivityTick(object? sender, EventArgs e) => CheckTaskActivity(Stopwatch.GetElapsedTime(lastTaskActivity));

    private void ResetTaskPauseMeasurement()
    {
        lastTaskActivity = lastTransferActivity = Stopwatch.GetTimestamp();
        smoothedDownloadRate = 0;
        IsTaskStalled = false;
    }

    /// <summary>Checks elapsed time since measured work, never treating repainting as progress.</summary>
    internal void CheckTaskActivity(TimeSpan idleTime, TimeSpan? transferIdleTime = null)
    {
        if (!IsTaskExecuting || IsPaused || IsTaskStopping) return;
        IsTaskStalled = idleTime >= TimeSpan.FromSeconds(30);
        if (taskProgress?.Stage == GameOperationStage.Downloading && (transferIdleTime ?? Stopwatch.GetElapsedTime(lastTransferActivity)) >= TimeSpan.FromSeconds(3))
        {
            ProgressSpeed = $"{FileSizeFormatter.Format(0)}/s";
            ProgressEstimated = localizer.T(LocalizationKeys.TaskEstimating);
            smoothedDownloadRate = 0;
        }
        RefreshFloatingTaskPresentation();
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(PrimaryActionIcon));
    }

    private void ApplyFileActivity(GameOperationProgress progress)
    {
        var previous = taskProgress;
        if (previous is null || previous.Stage != progress.Stage || previous.IsPaused != progress.IsPaused
            || progress.ActivitySequence > previous.ActivitySequence || progress.ProcessedFileCount > previous.ProcessedFileCount
            || progress.DownloadedSize != previous.DownloadedSize || progress.Progress > previous.Progress)
        {
            lastTaskActivity = Stopwatch.GetTimestamp();
            IsTaskStalled = false;
        }
        if (progress.Stage == GameOperationStage.Downloading && !progress.IsPaused && !progress.IsCheckingDownload)
        {
            if (previous?.Stage != progress.Stage) smoothedDownloadRate = 0;
            if (progress.BytesPerSecond > 0 && (previous is null || previous.Stage != progress.Stage || previous.DownloadedSize != progress.DownloadedSize))
            {
                smoothedDownloadRate = smoothedDownloadRate <= 0 ? progress.BytesPerSecond : .25 * progress.BytesPerSecond + .75 * smoothedDownloadRate;
                lastTransferActivity = Stopwatch.GetTimestamp();
            }
            ProgressSpeed = smoothedDownloadRate > 0 ? $"{FileSizeFormatter.Format((long)smoothedDownloadRate)}/s" : "";
            ProgressEstimated = smoothedDownloadRate > 0
                ? localizer.F(LocalizationKeys.TaskDownloadRemaining, TimeSpan.FromSeconds(Math.Clamp((progress.TotalSize - progress.DownloadedSize) / smoothedDownloadRate, 0, TimeSpan.MaxValue.TotalSeconds - 1)).ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture))
                : localizer.T(LocalizationKeys.TaskEstimating);
        }
        else smoothedDownloadRate = 0;
        taskProgress = progress;
    }

    private void RefreshFloatingTaskPresentation()
    {
        foreach (var name in new[] { nameof(IsTaskCardVisible), nameof(ShowTaskPercent), nameof(ShowTaskProgress),
            nameof(CompactTaskTitle), nameof(CompactTaskSummary), nameof(CompactTaskSpeed), nameof(TaskActionText) }) OnPropertyChanged(name);
    }
}
