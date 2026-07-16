using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class DialogsViewModelTests
{
    static DialogsViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public void ShowUpdateAvailable_ListsFilesWithoutSelectingOne()
    {
        var viewModel = CreateViewModel();
        var files = CreateFiles();

        viewModel.ShowUpdateAvailable("1.2.0", files);

        Assert.True(viewModel.IsUpdateAvailableVisible);
        Assert.Equal("1.2.0", viewModel.UpdateAvailableVersion);
        Assert.Equal(files, viewModel.UpdateAvailableFiles);
        Assert.Null(viewModel.SelectedUpdateFile);
        Assert.False(viewModel.HasSelectedUpdateFile);
    }

    [Fact]
    public void ConfirmUpdateAvailable_WithoutSelection_DoesNotCloseOrRequestDownload()
    {
        var viewModel = CreateViewModel();
        string? requestedUrl = null;
        viewModel.ConfirmUpdateAvailableRequested += url => requestedUrl = url;
        viewModel.ShowUpdateAvailable("1.2.0", CreateFiles());

        viewModel.ConfirmUpdateAvailableCommand.Execute(null);

        Assert.True(viewModel.IsUpdateAvailableVisible);
        Assert.Null(requestedUrl);
    }

    [Fact]
    public void ConfirmUpdateAvailable_WithSelection_RequestsSelectedFileUrl()
    {
        var viewModel = CreateViewModel();
        var files = CreateFiles();
        string? requestedUrl = null;
        viewModel.ConfirmUpdateAvailableRequested += url => requestedUrl = url;
        viewModel.ShowUpdateAvailable("1.2.0", files);
        viewModel.SelectedUpdateFile = files[1];

        viewModel.ConfirmUpdateAvailableCommand.Execute(null);

        Assert.False(viewModel.IsUpdateAvailableVisible);
        Assert.Equal(files[1].Url, requestedUrl);
    }

    [Fact]
    public void ShowUpdateAvailable_WhenReopened_ClearsPreviousSelection()
    {
        var viewModel = CreateViewModel();
        var firstFiles = CreateFiles();
        viewModel.ShowUpdateAvailable("1.2.0", firstFiles);
        viewModel.SelectedUpdateFile = firstFiles[0];
        viewModel.CancelUpdateAvailableCommand.Execute(null);

        Assert.False(viewModel.IsUpdateAvailableVisible);
        Assert.Empty(viewModel.UpdateAvailableFiles);
        Assert.Null(viewModel.SelectedUpdateFile);

        var secondFiles = new[]
        {
            new ReleaseFile
            {
                Name = "Cafe.Launcher_v1.3.0.zip",
                Url = "https://example.com/Cafe.Launcher_v1.3.0.zip",
                Size = 7000000
            }
        };
        viewModel.ShowUpdateAvailable("1.3.0", secondFiles);

        Assert.Equal(secondFiles, viewModel.UpdateAvailableFiles);
        Assert.Null(viewModel.SelectedUpdateFile);
        Assert.False(viewModel.HasSelectedUpdateFile);
    }

    [Fact]
    public void CrashRecoveryCommands_HideDialogAndRaiseEvents()
    {
        var viewModel = CreateViewModel();
        var continued = false;
        var viewedLog = false;
        viewModel.CrashRecoveryContinueRequested += () => continued = true;
        viewModel.CrashRecoveryViewLogRequested += () => viewedLog = true;

        viewModel.ShowCrashRecovery();
        viewModel.ContinueAfterCrashCommand.Execute(null);

        Assert.True(continued);
        Assert.False(viewModel.IsCrashRecoveryVisible);

        viewModel.ShowCrashRecovery();
        viewModel.ViewCrashLogCommand.Execute(null);

        Assert.True(viewedLog);
        Assert.False(viewModel.IsCrashRecoveryVisible);
    }

    [Fact]
    public async Task ResetSettingsAfterCrashCommand_RaisesAsyncEventAndHidesDialog()
    {
        var viewModel = CreateViewModel();
        var reset = false;
        viewModel.CrashRecoveryResetSettingsRequested += () =>
        {
            reset = true;
            return Task.CompletedTask;
        };
        viewModel.ShowCrashRecovery();

        await viewModel.ResetSettingsAfterCrashCommand.ExecuteAsync(null);

        Assert.True(reset);
        Assert.False(viewModel.IsCrashRecoveryVisible);
    }

    [Fact]
    public async Task ConfirmationCommands_RaiseConfiguredEventsAndCloseDialogs()
    {
        var viewModel = CreateViewModel();
        var repair = false;
        var uninstall = false;
        var stop = false;
        var switchSource = false;
        var closeAfterStop = false;
        viewModel.ConfirmRepairRequested += () =>
        {
            repair = true;
            return Task.CompletedTask;
        };
        viewModel.ConfirmUninstallRequested += () =>
        {
            uninstall = true;
            return Task.CompletedTask;
        };
        viewModel.ConfirmStopRequested += () => stop = true;
        viewModel.ConfirmResourcePanelSourceSwitchRequested += () => switchSource = true;
        viewModel.CloseAfterStoppingDownloadRequested += () => closeAfterStop = true;

        viewModel.ShowRepairConfirm("repair");
        await viewModel.ConfirmRepairCommand.ExecuteAsync(null);
        viewModel.ShowUninstallConfirm("uninstall");
        await viewModel.ConfirmUninstallCommand.ExecuteAsync(null);
        viewModel.ShowStopConfirm();
        viewModel.ConfirmStopCommand.Execute(null);
        viewModel.ShowResourcePanelSourceConfirm("switch");
        viewModel.ConfirmResourcePanelSourceSwitchCommand.Execute(null);
        viewModel.ShowDownloadRunningCloseConfirm();
        viewModel.ConfirmCloseWhileDownloadingCommand.Execute(null);

        Assert.True(repair);
        Assert.True(uninstall);
        Assert.True(stop);
        Assert.True(switchSource);
        Assert.True(closeAfterStop);
        Assert.False(viewModel.IsRepairConfirmVisible);
        Assert.False(viewModel.IsStopConfirmVisible);
        Assert.False(viewModel.IsResourcePanelSourceConfirmVisible);
        Assert.False(viewModel.IsDownloadRunningCloseConfirmVisible);
    }

    [Fact]
    public async Task ConfirmRepairCommand_WithMultipleAsyncSubscribers_AwaitsEverySubscriber()
    {
        var viewModel = CreateViewModel();
        var firstSubscriberInvoked = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSubscriberRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSubscriberInvoked = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.ConfirmRepairRequested += async () =>
        {
            firstSubscriberInvoked.SetResult();
            await firstSubscriberRelease.Task;
        };
        viewModel.ConfirmRepairRequested += () =>
        {
            secondSubscriberInvoked.SetResult();
            return Task.CompletedTask;
        };
        viewModel.ShowRepairConfirm("repair");

        var confirmTask = viewModel.ConfirmRepairCommand.ExecuteAsync(null);
        await firstSubscriberInvoked.Task;

        Assert.False(confirmTask.IsCompleted);
        Assert.False(secondSubscriberInvoked.Task.IsCompleted);
        firstSubscriberRelease.SetResult();
        await secondSubscriberInvoked.Task;
        await confirmTask;
    }

    [Fact]
    public void CancelCommands_CloseEveryConfirmationDialog()
    {
        var viewModel = CreateViewModel();
        viewModel.ShowRepairConfirm("repair");
        viewModel.ShowUninstallConfirm("uninstall");
        viewModel.ShowStopConfirm();
        viewModel.ShowResourcePanelSourceConfirm("source");
        viewModel.ShowDownloadRunningCloseConfirm();

        viewModel.CancelRepairCommand.Execute(null);
        viewModel.CancelUninstallCommand.Execute(null);
        viewModel.CancelStopCommand.Execute(null);
        viewModel.CancelResourcePanelSourceSwitchCommand.Execute(null);
        viewModel.CancelCloseWhileDownloadingCommand.Execute(null);

        Assert.False(viewModel.IsRepairConfirmVisible);
        Assert.False(viewModel.IsUninstallConfirmVisible);
        Assert.False(viewModel.IsStopConfirmVisible);
        Assert.False(viewModel.IsResourcePanelSourceConfirmVisible);
        Assert.False(viewModel.IsDownloadRunningCloseConfirmVisible);
    }

    [Fact]
    public async Task ShowNoticeDialogIfNeededAsync_WhenNoticeWasNotShown_ShowsAndPersistsNotice()
    {
        var statePath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"),
            "shown_notices.json");
        var stateService = new NoticeStateService(statePath);
        var viewModel = new DialogsViewModel(
            new LocalizationService(),
            stateService,
            new SetupWizardViewModel(new LocalizationService(), new GameInstallationPath(), new LocalInstallationStateStore()),
            action =>
            {
                action();
                return Task.CompletedTask;
            });
        var config = new BaseConfigResponse
        {
            NoticePopOpen = true,
            NoticeContent = "notice-content"
        };

        await viewModel.ShowNoticeDialogIfNeededAsync(config, CancellationToken.None);

        Assert.True(viewModel.IsNoticeDialogVisible);
        Assert.Equal("notice-content", viewModel.NoticeDialogContent);
        Assert.Single(await stateService.ReadShownNoticesAsync());

        viewModel.IsNoticeDialogVisible = false;
        await viewModel.ShowNoticeDialogIfNeededAsync(config, CancellationToken.None);

        Assert.False(viewModel.IsNoticeDialogVisible);
    }

    [Fact]
    public async Task DismissNotice_WhenExitIsConfigured_RequestsClose()
    {
        var viewModel = CreateViewModel();
        var closeRequested = false;
        viewModel.CloseRequested += () => closeRequested = true;
        await viewModel.ShowNoticeDialogIfNeededAsync(
            new BaseConfigResponse
            {
                NoticePopOpen = true,
                NoticeContent = $"exit-{Guid.NewGuid():N}",
                ExitLauncherOpen = true
            },
            CancellationToken.None);

        viewModel.DismissNoticeCommand.Execute(null);

        Assert.True(closeRequested);
        Assert.False(viewModel.IsNoticeDialogVisible);
    }

    [Fact]
    public void ApplyLanguage_RefreshesVisibleDialogText()
    {
        var viewModel = CreateViewModel();
        viewModel.ShowStopConfirm();
        viewModel.ShowDownloadRunningCloseConfirm();
        viewModel.ShowUpdateAvailable("1.2.0", CreateFiles());

        viewModel.ApplyLanguage();

        Assert.NotEmpty(viewModel.StopConfirmText);
        Assert.Equal(viewModel.StopConfirmText, viewModel.DownloadRunningCloseConfirmText);
        Assert.Contains("1.2.0", viewModel.UpdateAvailableText, StringComparison.Ordinal);
    }

    private static DialogsViewModel CreateViewModel()
    {
        var noticePath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"),
            "shown_notices.json");
        return new DialogsViewModel(
            new LocalizationService(),
            new NoticeStateService(noticePath),
            new SetupWizardViewModel(new LocalizationService(), new GameInstallationPath(), new LocalInstallationStateStore()),
            action =>
            {
                action();
                return Task.CompletedTask;
            });
    }

    private static ReleaseFile[] CreateFiles() =>
    [
        new()
        {
            Name = "Cafe.Launcher_v1.2.0.zip",
            Url = "https://example.com/Cafe.Launcher_v1.2.0.zip",
            Size = 5000000
        },
        new()
        {
            Name = "Cafe.Launcher_Setup_v1.2.0.exe",
            Url = "https://example.com/Cafe.Launcher_Setup_v1.2.0.exe",
            Size = 6000000
        }
    ];
}
