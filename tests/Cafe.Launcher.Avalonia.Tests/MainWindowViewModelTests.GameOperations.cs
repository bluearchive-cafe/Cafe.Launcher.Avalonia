using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public partial class MainWindowViewModelTests
{
    [Fact]
    public async Task InitializeAsync_WhenInstallationStateIsCorrupted_OffersRepairInsteadOfLaunch()
    {
        var snapshot = CreateSnapshot();
        snapshot.RuntimeState = LauncherRuntimeState.Corrupted;
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService);

        await viewModel.InitializeAsync();
        await viewModel.Operations.InstallOrUpdateCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.Shell.I18n["repair"], viewModel.Operations.InstallButtonText);
        Assert.True(viewModel.Operations.IsInstallPanelVisible);
        Assert.False(viewModel.Operations.IsControlPanelVisible);
        Assert.True(viewModel.Dialogs.IsRepairConfirmVisible);
    }

    [Fact]
    public async Task ConfirmRepairCommand_WhenShellIsWired_InvokesRepairOnce()
    {
        var snapshot = CreateSnapshot();
        snapshot.RuntimeState = LauncherRuntimeState.Corrupted;
        var backend = new StubGameOperationExecutor
        {
            RepairResult = new GameOperationResult
            {
                Success = true,
                Message = "repaired"
            }
        };
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(snapshot),
            gameOperationsBackend: backend);
        await viewModel.InitializeAsync();
        await viewModel.Operations.InstallOrUpdateCommand.ExecuteAsync(null);

        await viewModel.Dialogs.ConfirmRepairCommand.ExecuteAsync(null);

        Assert.Equal(1, backend.RepairCallCount);
    }

    [Fact]
    public async Task ConfirmUninstallCommand_WhenShellIsWired_InvokesUninstallOnce()
    {
        var snapshot = CreateSnapshot();
        snapshot.RuntimeState = LauncherRuntimeState.Ready;
        var backend = new StubGameOperationExecutor
        {
            ValidateUninstallResult = new GameOperationResult { Success = true },
            UninstallResult = new GameOperationResult { Success = true, Message = "uninstalled" }
        };
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(snapshot),
            gameOperationsBackend: backend);
        await viewModel.InitializeAsync();
        await viewModel.Operations.RequestUninstallCommand.ExecuteAsync(null);

        await viewModel.Dialogs.ConfirmUninstallCommand.ExecuteAsync(null);

        Assert.Equal(1, backend.UninstallCallCount);
    }

    [Fact]
    public async Task ConfirmStopCommand_WhenShellIsWired_InvokesStopOnce()
    {
        var backend = new StubGameOperationExecutor { IsDownloadRunning = true };
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(CreateSnapshot()),
            gameOperationsBackend: backend);
        viewModel.Dialogs.ShowStopConfirm();

        viewModel.Dialogs.ConfirmStopCommand.Execute(null);

        Assert.Equal(1, backend.StopCallCount);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenRemoteStateIsUnavailable_ReloadsState()
    {
        var snapshot = CreateSnapshot();
        snapshot.RuntimeState = LauncherRuntimeState.RemoteUnavailable;
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService);
        await viewModel.InitializeAsync();

        await viewModel.Operations.InstallOrUpdateCommand.ExecuteAsync(null);

        Assert.Equal(2, coreService.LoadCount);
        Assert.Equal(viewModel.Shell.I18n["refresh"], viewModel.Operations.InstallButtonText);
    }

    [Fact]
    public async Task ApplyProgress_WhenProgressCannotPause_HidesPauseResume()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);

        ApplyProgress(viewModel, new GameOperationProgress
        {
            OperationKind = GameOperationKind.Uninstall,
            Stage = GameOperationStage.Uninstalling,
            Progress = 50,
            CanPause = false
        });

        Assert.False(viewModel.Operations.CanPauseOperation);
    }

    [Fact]
    public async Task ApplyProgress_WhenProgressCanPause_ShowsPauseResume()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);

        ApplyProgress(viewModel, new GameOperationProgress
        {
            OperationKind = GameOperationKind.Download,
            Stage = GameOperationStage.Downloading,
            Progress = 50,
            CanPause = true
        });

        Assert.True(viewModel.Operations.CanPauseOperation);
    }

    [Fact]
    public async Task InstallFailureViewLogAction_OpensLogViewerUntilMainWindowIsDisposed()
    {
        var toastService = new ToastService();
        ToastNotification? raised = null;
        toastService.ToastRaised += toast => raised = toast;
        var backend = new StubGameOperationExecutor
        {
            InstallResult = new GameOperationResult
            {
                Success = false,
                Message = "offline"
            }
        };
        var viewModel = await CreateViewModelAsync(
            new CountingCoreService(CreateSnapshot()),
            toastService: toastService,
            gameOperationsBackend: backend);
        viewModel.Shell.IsBusy = false;
        viewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.NotInstalled
        });
        await viewModel.Operations.InstallOrUpdateCommand.ExecuteAsync(null);

        await raised!.SecondaryAction!.ExecuteAsync(CancellationToken.None);

        Assert.True(viewModel.LogViewer.IsVisible);
        viewModel.LogViewer.CloseCommand.Execute(null);
        viewModel.Dispose();
        await raised.SecondaryAction.ExecuteAsync(CancellationToken.None);
        Assert.False(viewModel.LogViewer.IsVisible);
    }

    private static void ApplyProgress(MainWindowViewModel viewModel, GameOperationProgress progress)
    {
        viewModel.Operations.ApplyProgress(progress);
    }
}
