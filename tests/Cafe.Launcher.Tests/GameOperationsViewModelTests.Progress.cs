using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Models;

namespace Cafe.Launcher.Tests;

public sealed partial class GameOperationsViewModelTests
{
    [Theory]
    [InlineData(LauncherRuntimeState.NotInstalled, GameOperationKind.Download, LocalizationKeys.Downloading)]
    [InlineData(LauncherRuntimeState.UpdateAvailable, GameOperationKind.Download, LocalizationKeys.TaskUpdating)]
    [InlineData(LauncherRuntimeState.Ready, GameOperationKind.Repair, LocalizationKeys.Repairing)]
    public void FloatingTask_UsesTaskSpecificActionWithoutOpeningAModal(LauncherRuntimeState runtime, GameOperationKind kind, LocalizationKeys label)
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = runtime });
        context.ViewModel.ApplyProgress(new GameOperationProgress { OperationKind = kind, Stage = GameOperationStage.Downloading, Progress = 42 });
        Assert.True(context.ViewModel.IsTaskCardVisible);
        Assert.False(context.ViewModel.IsTaskDialogVisible);
        var reveals = 0;
        context.ViewModel.TaskSurfaceRequested += () => reveals++;
        Assert.Equal(context.Localizer.T(label), context.ViewModel.PrimaryActionText);
        context.ViewModel.PrimaryActionCommand.Execute(null);
        Assert.Equal(1, reveals);
        Assert.False(context.ViewModel.IsTaskDialogVisible);
    }

    [Fact]
    public void FileReadProgress_ResetsStallEvenWhenIntegerPercentageIsUnchanged()
    {
        var context = CreateContext();
        var first = new GameOperationProgress { OperationKind = GameOperationKind.Repair, Stage = GameOperationStage.FileCheck,
            Progress = 42, ActivitySequence = 1, TotalFileCount = 10,
            ActiveFiles = [new("large.bundle", 100, 10000, true)] };
        context.ViewModel.ApplyProgress(first);
        context.ViewModel.CheckTaskActivity(TimeSpan.FromSeconds(31));
        Assert.True(context.ViewModel.IsTaskStalled);
        context.ViewModel.ApplyProgress(first);
        Assert.True(context.ViewModel.IsTaskStalled);
        context.ViewModel.ApplyProgress(new GameOperationProgress { OperationKind = first.OperationKind, Stage = first.Stage,
            Progress = 42, ActivitySequence = 2, TotalFileCount = 10,
            ActiveFiles = [new("large.bundle", 200, 10000, true)] });
        Assert.False(context.ViewModel.IsTaskStalled);
        Assert.Equal(42, context.ViewModel.ProgressValue);
        Assert.Equal(context.Localizer.F(LocalizationKeys.TaskProcessedFiles, 0, 10), context.ViewModel.CompactTaskSummary);
    }

    [Fact]
    public void PausedTask_DoesNotBecomeStalledOrShowDownloadEstimates()
    {
        var context = CreateContext();
        context.ViewModel.ApplyProgress(new GameOperationProgress { OperationKind = GameOperationKind.Download,
            Stage = GameOperationStage.Paused, IsPaused = true, DownloadedSize = 20, TotalSize = 100 });
        context.ViewModel.CheckTaskActivity(TimeSpan.FromMinutes(5));
        Assert.False(context.ViewModel.IsTaskStalled);
        Assert.Empty(context.ViewModel.CompactTaskSpeed);
        Assert.Equal(context.Localizer.T(LocalizationKeys.TaskDownloadPaused), context.ViewModel.PrimaryActionText);
        Assert.Equal("20B / 100B", context.ViewModel.CompactTaskSummary);
    }

    [Fact]
    public void StaleDownloadSpeed_StaysClearedDuringFileReadsUntilMoreBytesTransfer()
    {
        var context = CreateContext();
        var progress = new GameOperationProgress { OperationKind = GameOperationKind.Download,
            Stage = GameOperationStage.Downloading, DownloadedSize = 100, TotalSize = 1000,
            BytesPerSecond = 100, ActivitySequence = 1 };
        context.ViewModel.ApplyProgress(progress);
        Assert.NotEmpty(context.ViewModel.ProgressSpeed);
        context.ViewModel.CheckTaskActivity(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
        Assert.Equal("0B/s", context.ViewModel.ProgressSpeed);
        // Other files can keep reading while one transfer is blocked. Their snapshots retain
        // the last measured rate but must not revive a stale download estimate.
        context.ViewModel.ApplyProgress(new GameOperationProgress { OperationKind = progress.OperationKind,
            Stage = progress.Stage, DownloadedSize = 100, TotalSize = 1000, BytesPerSecond = 100, ActivitySequence = 2 });
        Assert.Empty(context.ViewModel.ProgressSpeed);
        Assert.Equal(context.Localizer.T(LocalizationKeys.TaskEstimating), context.ViewModel.ProgressEstimated);
        context.ViewModel.ApplyProgress(new GameOperationProgress { OperationKind = progress.OperationKind,
            Stage = progress.Stage, DownloadedSize = 200, TotalSize = 1000, BytesPerSecond = 100 });
        Assert.NotEmpty(context.ViewModel.ProgressSpeed);
    }

    [Fact]
    public void FloatingTask_UsesFileCountsAndMarksPostDownloadChecksIndeterminate()
    {
        var context = CreateContext();
        context.ViewModel.ApplyProgress(new GameOperationProgress { OperationKind = GameOperationKind.Download,
            Stage = GameOperationStage.Downloading, Progress = 100, IsCheckingDownload = true, TotalFileCount = 5, ProcessedFileCount = 2,
            ActiveFiles = Enumerable.Range(0, 5).Select(i => new GameOperationFileProgress(i + ".bundle", 1, 10, true)).ToArray() });
        Assert.Equal(context.Localizer.F(LocalizationKeys.TaskProcessedFiles, 2, 5), context.ViewModel.CompactTaskSummary);
        Assert.True(context.ViewModel.IsProgressIndeterminate);
        Assert.False(context.ViewModel.ShowTaskPercent);
        Assert.Empty(context.ViewModel.CompactTaskSpeed);
        Assert.Equal(context.Localizer.T(LocalizationKeys.TaskChecking), context.ViewModel.PrimaryActionText);
        Assert.Equal(context.Localizer.T(LocalizationKeys.VerifyingDownloadedFiles), context.ViewModel.ProgressDetail);
        Assert.Equal("Magnify", context.ViewModel.PrimaryActionIcon);
    }

    [Theory]
    [InlineData(LauncherRuntimeState.NotInstalled, GameOperationKind.Download)]
    [InlineData(LauncherRuntimeState.UpdateAvailable, GameOperationKind.Download)]
    [InlineData(LauncherRuntimeState.Ready, GameOperationKind.Repair)]
    public void FloatingTask_DownloadShowsTransferredAndTotalSizeWithoutOpeningDetails(LauncherRuntimeState runtime, GameOperationKind kind)
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = runtime });
        context.ViewModel.ApplyProgress(new GameOperationProgress { OperationKind = kind,
            Stage = GameOperationStage.Downloading, DownloadedSize = 10 * 1024 * 1024, TotalSize = 20 * 1024 * 1024,
            BytesPerSecond = 2 * 1024 * 1024, Progress = 50 });
        Assert.Equal("10MB / 20MB", context.ViewModel.CompactTaskSummary);
        Assert.Equal("2MB/s", context.ViewModel.CompactTaskSpeed);
        context.ViewModel.ApplyProgress(new GameOperationProgress { OperationKind = kind,
            Stage = GameOperationStage.Paused, IsPaused = true, DownloadedSize = 10 * 1024 * 1024, TotalSize = 20 * 1024 * 1024,
            BytesPerSecond = 2 * 1024 * 1024, Progress = 50 });
        Assert.Equal("10MB / 20MB", context.ViewModel.CompactTaskSummary);
        Assert.Empty(context.ViewModel.CompactTaskSpeed);
    }

    [Theory]
    [InlineData(true, true, 0)]
    [InlineData(true, false, 1)]
    [InlineData(false, false, 0)]
    public void TaskResult_OnlyNotifiesWhenObscuredAndQueuesWhileHidden(bool windowVisible, bool unobscured, int immediate)
    {
        var context = CreateContext();
        var notices = SubscribeToasts(context);
        context.ViewModel.SetTaskSurfaceVisibility(windowVisible, unobscured);
        context.ViewModel.ApplyProgress(new GameOperationProgress { OperationKind = GameOperationKind.Download });
        ((IGameOperationJourneyHost)context.ViewModel).Present(new GameOperationPresentation.Completed(new GameOperationResult { Success = true }));
        Assert.Equal(immediate, notices.Count);
        context.ViewModel.SetTaskSurfaceVisibility(true, true);
        context.ViewModel.SetTaskSurfaceVisibility(true, true);
        Assert.Equal(windowVisible ? immediate : 1, notices.Count);
        Assert.True(context.ViewModel.HasTaskResult);
        Assert.False(context.ViewModel.IsTaskDialogVisible);
    }

    [Fact]
    public void ClosedResult_DoesNotDeliverAnOldQueuedNotificationAfterRestore()
    {
        var context = CreateContext();
        var notices = SubscribeToasts(context);
        context.ViewModel.SetTaskSurfaceVisibility(false, false);
        context.ViewModel.ApplyProgress(new GameOperationProgress { OperationKind = GameOperationKind.Download });
        ((IGameOperationJourneyHost)context.ViewModel).Present(new GameOperationPresentation.Completed(new GameOperationResult { Success = true }));
        context.ViewModel.CloseTaskCommand.Execute(null);
        context.ViewModel.SetTaskSurfaceVisibility(true, true);
        Assert.Empty(notices);
        Assert.False(context.ViewModel.IsTaskCardVisible);
    }
}
