using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Models;

namespace Cafe.Launcher.Tests;

public sealed partial class GameOperationsViewModelTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ConfirmInstall_CreatesShortcutOnlyWhenSelectedAndSuccessful(bool selected, bool success)
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.NotInstalled,
            LocalGame = new LocalInstallationState { GamePath = "target" } });
        context.ViewModel.CommitInstallPathAsync = _ => Task.FromResult(true);
        context.Backend.InstallResult = new GameOperationResult { Success = success };
        context.ViewModel.RefreshRequested += _ =>
        {
            context.ViewModel.ApplySnapshot(ReadySnapshot("target"));
            return Task.CompletedTask;
        };
        await context.ViewModel.OpenInstallPreparationCommand.ExecuteAsync(null);
        context.ViewModel.CreateInstallDesktopShortcut = selected;
        await context.ViewModel.ConfirmInstallCommand.ExecuteAsync(null);
        Assert.Equal(selected && success, context.ShortcutService.LastSnapshot is not null);
        Assert.Equal(success, context.ViewModel.CanLaunchTaskResult);
        if (selected && success) Assert.Equal("target", context.ShortcutService.LastSnapshot!.LocalGame.GamePath);
    }

    [Fact]
    public async Task InstallPreparation_FormatsSeparateSizesAndDiscardsShortcutDraft()
    {
        var context = CreateContext();
        context.Backend.InstallDownloadBytes = 1610612736;
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.NotInstalled,
            Remote = new LauncherRemoteState { GameConfig = new GameConfigResponse { DecompressionSize = "2 GB" } } });
        await context.ViewModel.OpenInstallPreparationCommand.ExecuteAsync(null);
        Assert.Equal(context.Localizer.F(LocalizationKeys.TaskInstallDownloadSize, $"{1.5:0.00} GB"), context.ViewModel.InstallDownloadSizeText);
        Assert.Equal(context.Localizer.F(LocalizationKeys.TaskInstallRequiredSize, $"{2:0.00} GB"), context.ViewModel.InstallRequiredSizeText);
        context.ViewModel.CreateInstallDesktopShortcut = false;
        context.ViewModel.CloseTaskCommand.Execute(null);
        await context.ViewModel.OpenInstallPreparationCommand.ExecuteAsync(null);
        Assert.True(context.ViewModel.CreateInstallDesktopShortcut);
        Assert.Null(context.ShortcutService.LastSnapshot);
    }

    [Fact]
    public async Task InstallPreparation_ClosedPreviewCannotOverwriteNewPreparation()
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.NotInstalled });
        var pending = new TaskCompletionSource<long?>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Backend.InstallSizeCompletion = pending;
        var opening = context.ViewModel.OpenInstallPreparationCommand.ExecuteAsync(null);
        context.ViewModel.CloseTaskCommand.Execute(null);
        pending.SetResult(1073741824);
        await opening;
        Assert.False(context.ViewModel.IsTaskDialogVisible);
        Assert.Equal(context.Localizer.F(LocalizationKeys.TaskInstallDownloadSize, "—"), context.ViewModel.InstallDownloadSizeText);
        context.Backend.InstallSizeCompletion = null;
        await context.ViewModel.OpenInstallPreparationCommand.ExecuteAsync(null);
        Assert.Equal(context.Localizer.F(LocalizationKeys.TaskInstallRequiredSize, "—"), context.ViewModel.InstallRequiredSizeText);
    }

    [Fact]
    public async Task Task_WhenHidden_CompletesWithoutOpeningAndOffersResultOnce()
    {
        var context = CreateContext();
        var notices = SubscribeToasts(context);
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.NotInstalled });
        var completion = new TaskCompletionSource<GameOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Backend.InstallCompletion = completion;
        var running = context.ViewModel.InstallOrUpdateCommand.ExecuteAsync(null);
        Assert.True(context.ViewModel.IsTaskCardVisible);
        Assert.False(context.ViewModel.IsTaskDialogVisible);
        context.ViewModel.SetTaskSurfaceVisibility(false, false);
        context.ViewModel.CloseTaskCommand.Execute(null);
        Assert.True(context.ViewModel.IsTaskExecuting);
        Assert.Equal(0, context.Backend.StopCallCount);
        completion.SetResult(new GameOperationResult { Success = true, Message = "installed" });
        await running;
        Assert.False(context.ViewModel.IsTaskDialogVisible);
        Assert.True(context.ViewModel.HasTaskResult);
        Assert.Empty(notices);
        context.ViewModel.SetTaskSurfaceVisibility(true, true);
        var notice = Assert.Single(notices);
        Assert.Equal(context.Localizer.T(LocalizationKeys.TaskViewResult), notice.PrimaryAction!.Label);
        await notice.PrimaryAction.ExecuteAsync(CancellationToken.None);
        Assert.True(context.ViewModel.IsTaskCardVisible);
        Assert.Equal("installed", context.ViewModel.TaskMessage);
        Assert.Equal(0, context.Backend.LaunchCallCount);
    }

    [Fact]
    public async Task Task_StopWaitsForCleanupAndKeepsNewOperationsBlocked()
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(ReadySnapshot());
        var completion = new TaskCompletionSource<GameOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Backend.RepairCompletion = completion;
        var running = context.ViewModel.RepairAsync();
        context.ViewModel.RequestStopCommand.Execute(null);
        Assert.True(context.Dialogs.StopConfirm.IsVisible);
        await context.Dialogs.StopConfirm.ConfirmCommand.ExecuteAsync(null);
        Assert.True(context.ViewModel.IsTaskStopping);
        Assert.True(context.ViewModel.IsTaskExecuting);
        Assert.False(context.ViewModel.CanStopTask);
        Assert.False(context.ViewModel.CanRetryTask);
        Assert.False(context.ViewModel.CanManageGame);
        context.ViewModel.CloseTaskCommand.Execute(null);
        context.Shell.IsBusy = false; // Refresh/stop flags cannot release the task's own execution gate.
        await context.ViewModel.StartGameCommand.ExecuteAsync(null);
        Assert.Equal(0, context.Backend.LaunchCallCount);
        completion.SetResult(new GameOperationResult { ErrorCode = GameOperationErrorCode.Stopped, Message = "stopped" });
        await running;
        Assert.False(context.ViewModel.IsTaskExecuting);
        Assert.False(context.ViewModel.IsTaskStopping);
        Assert.True(context.ViewModel.CanManageGame);
        Assert.False(context.ViewModel.IsTaskFailed);
    }

    [Fact]
    public async Task InstallPreparation_CancelKeepsSavedPathAndPreviousResult()
    {
        var context = CreateContext();
        var snapshot = new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.NotInstalled,
            LocalGame = new LocalInstallationState { GamePath = "original" } };
        context.ViewModel.ApplySnapshot(snapshot);
        context.Backend.InstallResult = new GameOperationResult { Message = "old failure", ErrorCode = GameOperationErrorCode.Network };
        await context.ViewModel.InstallOrUpdateCommand.ExecuteAsync(null);
        var writes = 0;
        context.ViewModel.CommitInstallPathAsync = _ => { writes++; return Task.FromResult(true); };
        context.ViewModel.PickInstallPathAsync = _ => Task.FromResult<string?>("draft");
        context.ViewModel.OpenInstallPreparationCommand.Execute(null);
        await context.ViewModel.ChooseInstallPathCommand.ExecuteAsync(null);
        Assert.Equal("draft", context.ViewModel.InstallPathDraft);
        context.ViewModel.CloseTaskCommand.Execute(null);
        Assert.Equal(0, writes);
        Assert.Equal("original", snapshot.LocalGame.GamePath);
        Assert.Equal("old failure", context.ViewModel.TaskMessage);
        context.ViewModel.OpenInstallPreparationCommand.Execute(null);
        Assert.Equal("original", context.ViewModel.InstallPathDraft);
    }

    [Fact]
    public async Task ConfirmInstall_CommitsDraftBeforeUsingRefreshedSnapshot()
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.NotInstalled,
            LocalGame = new LocalInstallationState { GamePath = "old" } });
        context.ViewModel.CommitInstallPathAsync = path =>
        {
            Assert.Equal(0, context.Backend.InstallCallCount);
            context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.NotInstalled,
                LocalGame = new LocalInstallationState { GamePath = path } });
            return Task.FromResult(true);
        };
        await context.ViewModel.PrimaryActionCommand.ExecuteAsync(null);
        Assert.True(context.ViewModel.IsInstallPreparing);
        Assert.Equal(0, context.Backend.InstallCallCount);
        context.ViewModel.InstallPathDraft = "new";
        await context.ViewModel.ConfirmInstallCommand.ExecuteAsync(null);
        Assert.Equal("new", context.Backend.LastInstallSnapshot!.LocalGame.GamePath);
        Assert.False(context.ViewModel.IsInstallPreparing);
        Assert.True(context.ViewModel.HasTaskResult);
    }

    [Fact]
    public async Task IdentifyInstalledGame_PicksFolderBeforeCommittingWithoutStartingDownload()
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.NotInstalled,
            LocalGame = new LocalInstallationState { GamePath = "draft" } });
        var picks = 0;
        context.ViewModel.PickInstallPathAsync = path =>
        {
            Assert.Equal("draft", path);
            picks++;
            return Task.FromResult<string?>("existing");
        };
        var commits = 0;
        context.ViewModel.CommitInstallPathAsync = path =>
        {
            Assert.Equal(1, picks);
            Assert.Equal("existing", path);
            commits++;
            return Task.FromResult(true);
        };
        await context.ViewModel.PrimaryActionCommand.ExecuteAsync(null);
        await context.ViewModel.IdentifyInstalledGameCommand.ExecuteAsync(null);
        Assert.Equal(1, picks);
        Assert.Equal(1, commits);
        Assert.Equal(0, context.Backend.InstallCallCount);
        Assert.Null(context.ShortcutService.LastSnapshot);
        Assert.False(context.ViewModel.IsTaskDialogVisible);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task IdentifyInstalledGame_CancelLeavesDraftAndPreparationUnchanged(string? picked)
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.NotInstalled,
            LocalGame = new LocalInstallationState { GamePath = "original" } });
        context.ViewModel.PickInstallPathAsync = _ => Task.FromResult(picked);
        var commits = 0;
        context.ViewModel.CommitInstallPathAsync = _ => { commits++; return Task.FromResult(true); };
        await context.ViewModel.OpenInstallPreparationCommand.ExecuteAsync(null);
        context.ViewModel.InstallPathDraft = "draft";
        await context.ViewModel.IdentifyInstalledGameCommand.ExecuteAsync(null);
        Assert.Equal(0, commits);
        Assert.Equal("draft", context.ViewModel.InstallPathDraft);
        Assert.True(context.ViewModel.IsTaskDialogVisible);
        Assert.True(context.ViewModel.IsInstallPreparing);
        Assert.False(context.ViewModel.IsInstallPathBusy);
        Assert.Equal(0, context.Backend.InstallCallCount);
        Assert.Null(context.ShortcutService.LastSnapshot);
    }

    [Fact]
    public async Task IdentifyInstalledGame_EmptyDraftStillOpensPickerAndBlocksOtherActionsWhilePicking()
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.NotInstalled });
        var selection = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var picks = 0;
        context.ViewModel.PickInstallPathAsync = _ => { picks++; return selection.Task; };
        context.ViewModel.CommitInstallPathAsync = _ => Task.FromResult(true);
        await context.ViewModel.OpenInstallPreparationCommand.ExecuteAsync(null);
        var locating = context.ViewModel.IdentifyInstalledGameCommand.ExecuteAsync(null);
        Assert.Equal(1, picks);
        Assert.True(context.ViewModel.IsInstallPathBusy);
        Assert.False(context.ViewModel.CanChooseInstallPath);
        Assert.False(context.ViewModel.CanStartInstall);
        Assert.False(context.ViewModel.CloseTaskCommand.CanExecute(null));
        selection.SetResult("existing");
        await locating;
        Assert.False(context.ViewModel.IsInstallPathBusy);
        Assert.False(context.ViewModel.IsTaskDialogVisible);
    }

    [Theory]
    [InlineData(LauncherRuntimeState.Ready)]
    [InlineData(LauncherRuntimeState.Corrupted)]
    public async Task RetryTask_RechecksAdmissionAndRetainsOldResultWhenRejected(LauncherRuntimeState state)
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.NotInstalled });
        context.Backend.InstallResult = new GameOperationResult { ErrorCode = GameOperationErrorCode.Network, Message = "offline" };
        await context.ViewModel.InstallOrUpdateCommand.ExecuteAsync(null);
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = state });
        await context.ViewModel.RetryTaskCommand.ExecuteAsync(null);
        Assert.Equal(1, context.Backend.InstallCallCount);
        Assert.Equal("offline", context.ViewModel.TaskMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryTask_UsesCurrentPreferencesButNeverRedirectsTarget(bool changePath)
    {
        var context = CreateContext();
        var original = new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.NotInstalled,
            LocalGame = new LocalInstallationState { GamePath = "original" } };
        context.ViewModel.ApplySnapshot(original);
        context.Backend.InstallResult = new GameOperationResult { ErrorCode = GameOperationErrorCode.Network };
        await context.ViewModel.InstallOrUpdateCommand.ExecuteAsync(null);
        var latest = new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.NotInstalled,
            LocalGame = new LocalInstallationState { GamePath = changePath ? "different" : "original" } };
        context.ViewModel.ApplySnapshot(latest);
        await context.ViewModel.RetryTaskCommand.ExecuteAsync(null);
        Assert.Equal(changePath ? 1 : 2, context.Backend.InstallCallCount);
        Assert.Same(changePath ? original : latest, context.Backend.LastInstallSnapshot);
    }

    [Fact]
    public async Task RetryTask_WhenOnlyStateRefreshFails_KeepsPreviousResult()
    {
        var context = CreateContext();
        var notices = SubscribeToasts(context);
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.NotInstalled });
        context.Backend.InstallResult = new GameOperationResult { ErrorCode = GameOperationErrorCode.Network, Message = "original failure" };
        await context.ViewModel.InstallOrUpdateCommand.ExecuteAsync(null);
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.RemoteUnavailable });
        context.ViewModel.RefreshRequested += _ => throw new IOException("refresh failed");

        await context.ViewModel.RetryTaskCommand.ExecuteAsync(null);

        Assert.Equal(1, context.Backend.InstallCallCount);
        Assert.Equal("original failure", context.ViewModel.TaskMessage);
        Assert.False(context.ViewModel.IsTaskExecuting);
        Assert.Single(notices);
    }

    [Theory]
    [InlineData(GameOperationKind.Repair, LauncherRuntimeState.Ready, LocalizationKeys.TaskRepairFailed)]
    [InlineData(GameOperationKind.Download, LauncherRuntimeState.UpdateAvailable, LocalizationKeys.TaskUpdateFailed)]
    public async Task AutomaticResume_WhenPreparationFails_RetryKeepsOperationKind(
        GameOperationKind kind, LauncherRuntimeState runtimeState, LocalizationKeys failureTitle)
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot
        {
            RuntimeState = runtimeState,
            LocalGame = new LocalInstallationState { GamePath = "target" }
        });
        context.Backend.ResumeProgress = new GameOperationProgress
        {
            OperationKind = kind, Stage = GameOperationStage.Idle, Progress = -1
        };
        context.Backend.ResumeResult = new GameOperationResult
        {
            ErrorCode = GameOperationErrorCode.System, Message = "Directory preparation failed."
        };

        await context.ViewModel.ResumePersistedDownloadAsync(CancellationToken.None);

        Assert.Equal(context.Localizer.T(failureTitle), context.ViewModel.TaskTitle);
        Assert.True(context.ViewModel.HasTaskResult);
        Assert.True(context.ViewModel.CanRetryTask);
        Assert.False(context.ViewModel.IsTaskExecuting);
        await context.ViewModel.RetryTaskCommand.ExecuteAsync(null);
        Assert.Equal(kind == GameOperationKind.Repair ? 1 : 0, context.Backend.RepairCallCount);
        Assert.Equal(kind == GameOperationKind.Download ? 1 : 0, context.Backend.InstallCallCount);
    }

    [Fact]
    public async Task AutomaticResume_ShowsOnlyPanelProgressAndDoesNotReopenOnCompletion()
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(ReadySnapshot());
        context.Backend.ResumeProgress = new GameOperationProgress { OperationKind = GameOperationKind.Repair,
            Stage = GameOperationStage.RepairCheck, Progress = 25 };
        var completion = new TaskCompletionSource<GameOperationResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Backend.ResumeCompletion = completion;
        var running = context.ViewModel.ResumePersistedDownloadAsync(CancellationToken.None);
        Assert.True(context.ViewModel.IsTaskExecuting);
        Assert.False(context.ViewModel.IsTaskDialogVisible);
        Assert.Equal(25, context.ViewModel.ProgressValue);
        Assert.True(context.ViewModel.IsTaskCardVisible);
        completion.SetResult(new GameOperationResult { Success = true });
        await running;
        Assert.True(context.ViewModel.HasTaskResult);
        Assert.False(context.ViewModel.IsTaskDialogVisible);
    }

    [Fact]
    public async Task TaskResult_OldNotificationCannotOpenANewerResult()
    {
        var context = CreateContext();
        var notices = SubscribeToasts(context);
        context.ViewModel.SetTaskSurfaceVisibility(true, false);
        context.ViewModel.ApplySnapshot(ReadySnapshot());
        context.Backend.ResumeResult = new GameOperationResult { ErrorCode = GameOperationErrorCode.Network, Message = "old" };
        await context.ViewModel.ResumePersistedDownloadAsync(CancellationToken.None);
        var oldAction = Assert.Single(notices).PrimaryAction!;
        context.Backend.ResumeResult = new GameOperationResult { Success = true, Message = "new" };
        await context.ViewModel.ResumePersistedDownloadAsync(CancellationToken.None);
        var reveals = 0;
        context.ViewModel.TaskSurfaceRequested += () => reveals++;
        await oldAction.ExecuteAsync(CancellationToken.None);
        Assert.False(context.ViewModel.IsTaskDialogVisible);
        Assert.Equal(0, reveals);
        Assert.Equal("new", context.ViewModel.TaskMessage);
    }
}
