using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.Controls;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;

namespace Cafe.Launcher.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class UninstallDialogViewModelTests
{
    private static UninstallDialogViewModel Create(Func<Task>? confirm = null, Func<Task>? logs = null)
    {
        TestLocalizationHelper.Initialize();
        return new UninstallDialogViewModel(new LocalizationService(), confirm ?? (() => Task.CompletedTask), logs ?? (() => Task.CompletedTask));
    }

    [Fact]
    public void Open_WhenAnOldMeasurementReturns_OnlyUsesTheCurrentConfirmation()
    {
        var vm = Create();
        var first = vm.Open(new LauncherStatusSnapshot());
        vm.CloseCommand.Execute(null);
        var second = vm.Open(new LauncherStatusSnapshot());
        vm.ApplyFootprint(first, new UninstallFootprint(999, 999));
        var pending = vm.TotalSizeText;
        vm.ApplyFootprint(second, new UninstallFootprint(2048, 1024));
        Assert.NotEqual(pending, vm.TotalSizeText);
        Assert.Equal(FileSizeFormatter.FormatParts(2048).Value, vm.TotalSizeText);
        vm.IsCompatibilitySelected = true;
        Assert.Equal(FileSizeFormatter.FormatParts(3072).Value, vm.TotalSizeText);
    }

    [Fact]
    public void BeginExecution_WhenSizeIsPending_FreezesConfirmationAndBlocksClose()
    {
        var vm = Create();
        var version = vm.Open(new LauncherStatusSnapshot());
        var pending = vm.TotalSizeText;
        Assert.True(vm.BeginExecution());
        Assert.False(vm.BeginExecution());
        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.False(vm.CloseCommand.CanExecute(null));
        vm.CloseCommand.Execute(null);
        vm.ApplyFootprint(version, new UninstallFootprint(100, 10));
        Assert.True(vm.IsVisible);
        Assert.True(vm.IsExecuting);
        Assert.Equal(pending, vm.TotalSizeText);
    }

    [Fact]
    public void ApplyProgress_WhenCallbacksArriveOutOfOrder_DoesNotRegressPercentageOrReplaceTheResult()
    {
        var vm = Create();
        vm.Open(new LauncherStatusSnapshot());
        vm.BeginExecution();
        Assert.True(vm.IsIndeterminate);
        vm.ApplyProgress(new GameOperationProgress { Stage = GameOperationStage.Uninstalling, Progress = 70 });
        vm.ApplyProgress(new GameOperationProgress { Stage = GameOperationStage.Uninstalling, Progress = 30 });
        Assert.Equal(70, vm.ProgressValue);
        vm.ApplyProgress(new GameOperationProgress { Stage = GameOperationStage.UninstallCleanup, Progress = 95 });
        vm.ApplyProgress(new GameOperationProgress { Stage = GameOperationStage.UninstallScanning, Progress = 0 });
        Assert.Equal(UninstallDialogState.Cleanup, vm.State);
        Assert.Equal(95, vm.ProgressValue);
        vm.Complete(new GameOperationResult { Success = true });
        vm.ApplyProgress(new GameOperationProgress { Stage = GameOperationStage.Uninstalling, Progress = 80 });
        Assert.True(vm.IsResult);
        Assert.False(vm.IsExecuting);
        Assert.True(vm.CloseCommand.CanExecute(null));
    }

    [Fact]
    public async Task Complete_WhenThereAreManyLeftovers_ShowsAllPathsAndClosesBeforeOpeningLogs()
    {
        UninstallDialogViewModel? vm = null;
        var logsOpened = false;
        vm = Create(logs: () =>
        {
            Assert.False(vm!.IsVisible);
            logsOpened = true;
            return Task.CompletedTask;
        });
        vm.Open(new LauncherStatusSnapshot());
        vm.BeginExecution();
        var leftovers = Enumerable.Range(0, 20).Select(index => $"C:\\Game\\{index}.bin").ToArray();
        vm.Complete(new GameOperationResult
        {
            Success = true, AffectedFileCount = 30, AffectedBytes = 2048,
            UninstallDetails = new UninstallResultDetails(leftovers, "E:\\CustomPrefix")
        });
        Assert.True(vm.IsVisible);
        Assert.True(vm.HasMetrics);
        Assert.True(vm.HasLeftovers);
        Assert.Equal(DialogSurfaceStatus.Warning, vm.Status);
        Assert.Equal(20, vm.Leftovers.Count);
        Assert.True(vm.HasKeptPrefix);
        Assert.Equal("E:\\CustomPrefix", vm.KeptPrefixPath);
        await vm.ViewLogsCommand.ExecuteAsync(null);
        Assert.True(logsOpened);
    }

    [Fact]
    public void Complete_WhenTheWorkflowFails_PreservesItsReasonWithoutInventingDeletionMetrics()
    {
        var vm = Create();
        vm.Open(new LauncherStatusSnapshot());
        vm.BeginExecution();
        vm.Complete(new GameOperationResult { Message = "access denied" });
        Assert.True(vm.IsVisible);
        Assert.True(vm.IsFailed);
        Assert.False(vm.HasMetrics);
        Assert.Equal("access denied", vm.ResultMessage);
        Assert.Equal(DialogSurfaceStatus.Danger, vm.Status);
        Assert.True(vm.HasLogAction);
    }

    [Fact]
    public void Open_WhenReopened_ResetsTheCompatibilitySelectionAndPreviousResult()
    {
        var vm = Create();
        vm.Open(new LauncherStatusSnapshot());
        vm.IsCompatibilitySelected = true;
        vm.BeginExecution();
        vm.Complete(new GameOperationResult { Success = true });
        vm.CloseCommand.Execute(null);
        vm.Open(new LauncherStatusSnapshot());
        Assert.True(vm.IsConfirmation);
        Assert.False(vm.IsCompatibilitySelected);
        Assert.False(vm.HasMetrics);
        Assert.False(vm.HasLeftovers);
    }
}
