using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class DialogsViewModelTests
{
    /// <summary>Upper bound for a wait on an event the test itself gates, so a broken command fails instead of hanging.</summary>
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(5);

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
    public async Task ConfirmationCommands_RaiseConfirmedAndCloseDialogs()
    {
        var viewModel = CreateViewModel();
        var repair = false;
        var uninstall = false;
        var stop = false;
        var switchSource = false;
        var closeAfterStop = false;
        viewModel.RepairConfirm.Confirmed += () =>
        {
            repair = true;
            return Task.CompletedTask;
        };
        viewModel.UninstallConfirm.Confirmed += () =>
        {
            uninstall = true;
            return Task.CompletedTask;
        };
        viewModel.StopConfirm.Confirmed += () =>
        {
            stop = true;
            return Task.CompletedTask;
        };
        viewModel.ResourcePanelSourceConfirm.Confirmed += () =>
        {
            switchSource = true;
            return Task.CompletedTask;
        };
        viewModel.DownloadRunningCloseConfirm.Confirmed += () =>
        {
            closeAfterStop = true;
            return Task.CompletedTask;
        };

        viewModel.RepairConfirm.Show("repair");
        await viewModel.RepairConfirm.ConfirmCommand.ExecuteAsync(null);
        viewModel.UninstallConfirm.Show("uninstall");
        await viewModel.UninstallConfirm.ConfirmCommand.ExecuteAsync(null);
        viewModel.ShowStopConfirm();
        await viewModel.StopConfirm.ConfirmCommand.ExecuteAsync(null);
        viewModel.ResourcePanelSourceConfirm.Show("switch");
        await viewModel.ResourcePanelSourceConfirm.ConfirmCommand.ExecuteAsync(null);
        viewModel.ShowDownloadRunningCloseConfirm();
        await viewModel.DownloadRunningCloseConfirm.ConfirmCommand.ExecuteAsync(null);

        Assert.True(repair);
        Assert.True(uninstall);
        Assert.True(stop);
        Assert.True(switchSource);
        Assert.True(closeAfterStop);
        Assert.False(viewModel.RepairConfirm.IsVisible);
        Assert.False(viewModel.UninstallConfirm.IsVisible);
        Assert.False(viewModel.StopConfirm.IsVisible);
        Assert.False(viewModel.ResourcePanelSourceConfirm.IsVisible);
        Assert.False(viewModel.DownloadRunningCloseConfirm.IsVisible);
    }

    [Fact]
    public async Task RepairConfirm_WithMultipleAsyncSubscribers_AwaitsEverySubscriber()
    {
        var viewModel = CreateViewModel();
        var firstSubscriberInvoked = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSubscriberRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSubscriberInvoked = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.RepairConfirm.Confirmed += async () =>
        {
            firstSubscriberInvoked.SetResult();
            await firstSubscriberRelease.Task;
        };
        viewModel.RepairConfirm.Confirmed += () =>
        {
            secondSubscriberInvoked.SetResult();
            return Task.CompletedTask;
        };
        viewModel.RepairConfirm.Show("repair");

        var confirmTask = viewModel.RepairConfirm.ConfirmCommand.ExecuteAsync(null);
        // The gate flags are raised only by the command under test, so an unbounded await would hang
        // the runner instead of failing — the project-wide xUnit1051 suppression is justified by
        // every gate wait carrying a bound like this one.
        await firstSubscriberInvoked.Task.WaitAsync(GateTimeout);

        Assert.False(confirmTask.IsCompleted);
        Assert.False(secondSubscriberInvoked.Task.IsCompleted);
        firstSubscriberRelease.SetResult();
        await secondSubscriberInvoked.Task.WaitAsync(GateTimeout);
        await confirmTask.WaitAsync(GateTimeout);
    }

    [Fact]
    public async Task CancelCommands_CloseEveryConfirmationDialog()
    {
        var viewModel = CreateViewModel();
        var requested = false;
        viewModel.RepairConfirm.Confirmed += () =>
        {
            requested = true;
            return Task.CompletedTask;
        };
        viewModel.RepairConfirm.Show("repair");
        viewModel.UninstallConfirm.Show("uninstall");
        viewModel.ShowStopConfirm();
        viewModel.ResourcePanelSourceConfirm.Show("source");
        viewModel.ShowDownloadRunningCloseConfirm();

        viewModel.RepairConfirm.CancelCommand.Execute(null);
        viewModel.UninstallConfirm.CancelCommand.Execute(null);
        viewModel.StopConfirm.CancelCommand.Execute(null);
        viewModel.ResourcePanelSourceConfirm.CancelCommand.Execute(null);
        viewModel.DownloadRunningCloseConfirm.CancelCommand.Execute(null);

        Assert.False(requested);
        Assert.False(viewModel.RepairConfirm.IsVisible);
        Assert.False(viewModel.UninstallConfirm.IsVisible);
        Assert.False(viewModel.StopConfirm.IsVisible);
        Assert.False(viewModel.ResourcePanelSourceConfirm.IsVisible);
        Assert.False(viewModel.DownloadRunningCloseConfirm.IsVisible);
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
            new SetupWizardViewModel(new LocalizationService(), new GameInstallationPath(), new LocalInstallationStateStore(), new LocalDiagnostics(), new StubFilePickerService()),
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

        Assert.NotEmpty(viewModel.StopConfirm.Message);
        Assert.Equal(viewModel.StopConfirm.Message, viewModel.DownloadRunningCloseConfirm.Message);
        Assert.Contains("1.2.0", viewModel.UpdateAvailableText, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowStopConfirm_LocalizesSharedStopMessage()
    {
        var viewModel = CreateViewModel();

        viewModel.ShowStopConfirm();
        viewModel.ShowDownloadRunningCloseConfirm();

        Assert.True(viewModel.StopConfirm.IsVisible);
        Assert.True(viewModel.DownloadRunningCloseConfirm.IsVisible);
        Assert.NotEmpty(viewModel.StopConfirm.Message);
        Assert.Equal(viewModel.StopConfirm.Message, viewModel.DownloadRunningCloseConfirm.Message);
    }

    [Fact]
    public void SettingsResetConfirm_ShowAndCancel_ToggleVisibilityWithoutRequest()
    {
        var viewModel = CreateViewModel();
        var requested = false;
        viewModel.SettingsResetConfirm.Confirmed += () =>
        {
            requested = true;
            return Task.CompletedTask;
        };

        viewModel.SettingsResetConfirm.Show();
        Assert.True(viewModel.SettingsResetConfirm.IsVisible);

        viewModel.SettingsResetConfirm.CancelCommand.Execute(null);

        Assert.False(viewModel.SettingsResetConfirm.IsVisible);
        Assert.False(requested);
    }

    [Fact]
    public async Task SettingsResetConfirm_WhenConfirmed_RaisesRequestOnceAndCloses()
    {
        var viewModel = CreateViewModel();
        var requestCount = 0;
        viewModel.SettingsResetConfirm.Confirmed += () =>
        {
            requestCount++;
            return Task.CompletedTask;
        };
        viewModel.SettingsResetConfirm.Show();

        await viewModel.SettingsResetConfirm.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(1, requestCount);
        Assert.False(viewModel.SettingsResetConfirm.IsVisible);
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
            new SetupWizardViewModel(new LocalizationService(), new GameInstallationPath(), new LocalInstallationStateStore(), new LocalDiagnostics(), new StubFilePickerService()),
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
