using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.Update;
using Cafe.Launcher.Avalonia.ViewModels;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class DialogsViewModelTests : IDisposable
{
    /// <summary>
    /// 本类各静态装配辅助方法共用的临时父目录：xUnit 为每个测试方法新建实例，故它本身
    /// 就是每测试一个；每次装配再取一个子目录，保持「每个上下文一个独立数据根」的原语义。
    /// </summary>
    private readonly TestDirectory tempDir = TestDirectory.Create();

    public void Dispose() => tempDir.Dispose();

    /// <summary>Upper bound for a wait on an event the test itself gates, so a broken command fails instead of hanging.</summary>
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(5);

    static DialogsViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public void DownloadConfirmations_WhenLanguageIsApplied_PreserveDistinctConsequences()
    {
        var viewModel = CreateViewModel();
        var localizer = new LocalizationService();
        viewModel.ShowStopConfirm();
        viewModel.ShowDownloadRunningCloseConfirm();

        Assert.Equal(localizer.T(LocalizationKeys.StopDownloadMessage), viewModel.StopConfirm.Message);
        Assert.Equal(localizer.T(LocalizationKeys.CloseDownloadMessage), viewModel.DownloadRunningCloseConfirm.Message);
        viewModel.RefreshLocalizedText();
        Assert.Equal(localizer.T(LocalizationKeys.StopDownloadMessage), viewModel.StopConfirm.Message);
        Assert.Equal(localizer.T(LocalizationKeys.CloseDownloadMessage), viewModel.DownloadRunningCloseConfirm.Message);
        Assert.NotEqual(viewModel.StopConfirm.Message, viewModel.DownloadRunningCloseConfirm.Message);
    }

    [Fact]
    public void RefreshLocalizedText_AlsoRefreshesTheHostedSetupWizard()
    {
        // D10：向导不再自订阅语言事件，它的宿主 DialogsViewModel 负责把刷新传下去。
        var viewModel = CreateViewModel();
        var notified = false;
        viewModel.SetupWizard.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SetupWizardViewModel.GamePathPresentation))
            {
                notified = true;
            }
        };

        viewModel.RefreshLocalizedText();

        Assert.True(notified);
    }

    [Fact]
    public void ShowUpdateAvailable_WithoutSelfUpdate_EnablesBrowserHandOffImmediately()
    {
        var localizer = new LocalizationService();
        var viewModel = CreateViewModel();
        var files = CreateFiles();

        viewModel.ShowUpdateAvailable(
            "1.2.0", files, LauncherUpdateInAppAvailability.PlatformUnsupported);

        Assert.True(viewModel.IsUpdateAvailableVisible);
        Assert.Equal("1.2.0", viewModel.UpdateAvailableVersion);
        Assert.False(viewModel.UpdateSupportsInAppApply);
        Assert.True(viewModel.CanConfirmUpdate);
        // 主动作文案是「前往发布页」而不是「下载」：浏览器交接分支的措辞即出口语义。
        Assert.Equal(
            localizer.T(LocalizationKeys.LauncherUpdateOpenReleasePage),
            viewModel.UpdatePrimaryActionText);
    }

    [Fact]
    public void ShowUpdateAvailable_WithReleaseNotes_ExposesMarkdownPreview()
    {
        var viewModel = CreateViewModel();

        viewModel.ShowUpdateAvailable(
            "1.2.0",
            CreateFiles(),
            LauncherUpdateInAppAvailability.Available,
            releaseNotes: "## Highlights\n\n- Faster updates");

        Assert.True(viewModel.HasUpdateReleaseNotes);
        Assert.Equal("## Highlights\n\n- Faster updates", viewModel.UpdateReleaseNotes);
    }

    [Fact]
    public void ConfirmUpdateAvailable_WithoutSelfUpdate_OpensReleasePageAndCloses()
    {
        var viewModel = CreateViewModel();
        string? requestedUrl = null;
        viewModel.ConfirmUpdateAvailableRequested += url => requestedUrl = url;
        viewModel.ShowUpdateAvailable(
            "1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.PackageUnverifiable);

        viewModel.ConfirmUpdateAvailableCommand.Execute(null);

        Assert.False(viewModel.IsUpdateAvailableVisible);
        Assert.Equal(LauncherConstants.GitHubReleasesPageUrl, requestedUrl);
    }

    [Fact]
    public void ShowUpdateAvailable_WhenSelfUpdateSupported_EnablesInAppFlow()
    {
        var viewModel = CreateViewModel();

        viewModel.ShowUpdateAvailable("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);

        Assert.True(viewModel.UpdateSupportsInAppApply);
        Assert.True(viewModel.CanConfirmUpdate);
        Assert.False(viewModel.IsUpdateApplying);
    }

    [Theory]
    [InlineData(
        LauncherUpdateInAppAvailability.PlatformUnsupported,
        LocalizationKeys.LauncherUpdateInAppUnavailablePlatform)]
    [InlineData(
        LauncherUpdateInAppAvailability.HelperMissing,
        LocalizationKeys.LauncherUpdateInAppUnavailableHelper)]
    [InlineData(
        LauncherUpdateInAppAvailability.PackageUnverifiable,
        LocalizationKeys.LauncherUpdateInAppUnavailablePackage)]
    public void ShowUpdateAvailable_WhenInAppApplyIsUnavailable_NamesTheCauseTheVerdictReported(
        LauncherUpdateInAppAvailability availability,
        string expectedKey)
    {
        var localizer = new LocalizationService();
        var viewModel = CreateViewModel();

        viewModel.ShowUpdateAvailable("1.2.0", CreateFiles(), availability);

        // 「前往发布页」必须说清是哪一种「不行」：平台、这份安装，还是这一版发布。
        Assert.True(viewModel.IsUpdateInAppUnavailable);
        Assert.Equal(localizer.T(expectedKey), viewModel.UpdateInAppUnavailableNoticeText);
        Assert.NotEmpty(viewModel.UpdateInAppUnavailableNoticeText);
    }

    [Fact]
    public void ShowUpdateAvailable_ForEveryUnavailableCause_StaysThreeDistinctExplanations()
    {
        var viewModel = CreateViewModel();
        var notices = new List<string>();

        foreach (var availability in new[]
                 {
                     LauncherUpdateInAppAvailability.PlatformUnsupported,
                     LauncherUpdateInAppAvailability.HelperMissing,
                     LauncherUpdateInAppAvailability.PackageUnverifiable
                 })
        {
            viewModel.ShowUpdateAvailable("1.2.0", CreateFiles(), availability);
            notices.Add(viewModel.UpdateInAppUnavailableNoticeText);
        }

        // 三种原因必须说三件事：压回一句话就是又回到「此设备无法…」那种笼统归因。
        Assert.Equal(3, notices.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ShowUpdateAvailable_WhenAvailable_ShowsNoHandOffNotice()
    {
        var viewModel = CreateViewModel();

        viewModel.ShowUpdateAvailable("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);

        Assert.False(viewModel.IsUpdateInAppUnavailable);
        Assert.Empty(viewModel.UpdateInAppUnavailableNoticeText);
    }

    [Fact]
    public void MarkUpdateFailed_AfterAnAttempt_ReportsNoCapabilityVerdict()
    {
        var viewModel = CreateViewModel();
        viewModel.ShowUpdateAvailable("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);
        viewModel.BeginUpdateApply();

        viewModel.MarkUpdateFailed();

        // 一次失败不是能力判定：失败缘由由 toast 说明，对话框不能改口归因给平台或安装。
        Assert.False(viewModel.UpdateSupportsInAppApply);
        Assert.False(viewModel.IsUpdateInAppUnavailable);
        Assert.Empty(viewModel.UpdateInAppUnavailableNoticeText);
    }

    [Fact]
    public void RefreshLocalizedText_WhileTheHandOffNoticeIsVisible_RecomputesItsText()
    {
        var viewModel = CreateViewModel();
        viewModel.ShowUpdateAvailable(
            "1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.HelperMissing);
        var notified = false;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DialogsViewModel.UpdateInAppUnavailableNoticeText))
            {
                notified = true;
            }
        };

        viewModel.RefreshLocalizedText();

        Assert.True(notified, "切换语言后说明行未通告重算。");
    }

    [Fact]
    public void ConfirmUpdateAvailable_WhenSelfUpdateSupported_RaisesStart()
    {
        var viewModel = CreateViewModel();
        var raised = 0;
        viewModel.SelfUpdateStartRequested += (_, _) => raised++;
        viewModel.ShowUpdateAvailable("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);

        viewModel.ConfirmUpdateAvailableCommand.Execute(null);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void UpdateApplyStates_ToggleConfirmAndStatusText()
    {
        var viewModel = CreateViewModel();
        viewModel.ShowUpdateAvailable("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);

        viewModel.BeginUpdateApply();

        Assert.True(viewModel.IsUpdateApplying);
        Assert.True(viewModel.IsUpdateDownloading);
        Assert.False(viewModel.CanConfirmUpdate);

        viewModel.ReportUpdateProgress(0.5, 5 * 1024 * 1024);
        Assert.Equal(50d, viewModel.UpdateProgress);
        Assert.Equal("5MB/s", viewModel.UpdateDownloadSpeedText);

        viewModel.MarkUpdateReady();

        Assert.False(viewModel.IsUpdateDownloading);
        Assert.True(viewModel.IsUpdateReadyToRestart);
        Assert.True(viewModel.CanConfirmUpdate);
        Assert.Equal(100d, viewModel.UpdateProgress);
        Assert.Empty(viewModel.UpdateDownloadSpeedText);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.UpdateStatusText));
    }

    [Fact]
    public void ConfirmUpdateAvailable_WhenReady_RaisesApply()
    {
        var viewModel = CreateViewModel();
        var applied = 0;
        viewModel.ApplyUpdateRequested += () => applied++;
        viewModel.ShowUpdateAvailable("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);
        viewModel.BeginUpdateApply();
        viewModel.MarkUpdateReady();

        viewModel.ConfirmUpdateAvailableCommand.Execute(null);

        Assert.Equal(1, applied);
    }

    [Fact]
    public void MarkUpdateFailed_ChangesPrimaryActionToReleasePage()
    {
        var localizer = new LocalizationService();
        var viewModel = CreateViewModel();
        string? requestedUrl = null;
        viewModel.ConfirmUpdateAvailableRequested += url => requestedUrl = url;
        viewModel.ShowUpdateAvailable("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);
        viewModel.BeginUpdateApply();

        viewModel.MarkUpdateFailed();

        Assert.False(viewModel.IsUpdateApplying);
        Assert.False(viewModel.UpdateSupportsInAppApply);
        Assert.Equal(
            localizer.T(LocalizationKeys.LauncherUpdateOpenReleasePage),
            viewModel.UpdatePrimaryActionText);

        viewModel.ConfirmUpdateAvailableCommand.Execute(null);

        Assert.Equal(LauncherConstants.GitHubReleasesPageUrl, requestedUrl);
        Assert.False(viewModel.IsUpdateAvailableVisible);
    }

    [Fact]
    public void CancelUpdateAvailable_WhileApplying_RaisesCancelAndResets()
    {
        var viewModel = CreateViewModel();
        var cancelled = 0;
        viewModel.CancelUpdateRequested += () => cancelled++;
        viewModel.ShowUpdateAvailable("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);
        viewModel.BeginUpdateApply();

        viewModel.CancelUpdateAvailableCommand.Execute(null);

        Assert.Equal(1, cancelled);
        Assert.False(viewModel.IsUpdateAvailableVisible);
        Assert.False(viewModel.IsUpdateApplying);
    }

    [Fact]
    public void ShowUpdateAvailable_WhenReopened_InAppConfirmCarriesLatestFiles()
    {
        var viewModel = CreateViewModel();
        viewModel.ShowUpdateAvailable("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);
        viewModel.CancelUpdateAvailableCommand.Execute(null);

        Assert.False(viewModel.IsUpdateAvailableVisible);

        var secondFiles = new[]
        {
            new ReleaseFile
            {
                Name = "Cafe.Launcher_v1.3.0.zip",
                Url = "https://example.com/Cafe.Launcher_v1.3.0.zip",
                Size = 7000000
            }
        };
        IReadOnlyList<ReleaseFile>? requestedFiles = null;
        viewModel.SelfUpdateStartRequested += (_, files) => requestedFiles = files;
        viewModel.ShowUpdateAvailable("1.3.0", secondFiles, LauncherUpdateInAppAvailability.Available);

        viewModel.ConfirmUpdateAvailableCommand.Execute(null);

        // 文件清单不再作为可选项暴露，但应用内更新确认时必须携带最新一轮的文件。
        Assert.Equal(secondFiles, requestedFiles);
    }

    [Fact]
    public async Task ConfirmationCommands_WhenExecuted_RaiseConfirmedAndCloseDialogs()
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
    public async Task CancelCommands_WhenExecuted_CloseEveryConfirmationDialog()
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
        var stateService = new NoticeStateService(tempDir.DataRoot);
        var viewModel = new DialogsViewModel(
            new LocalizationService(),
            stateService,
            new SetupWizardViewModel(new LocalizationService(), new GameInstallationPath(), new LocalInstallationStateStore(), new LocalDiagnostics(), new StubFilePickerService()),
            new LocalDiagnostics(),
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
        viewModel.ShowUpdateAvailable(
            "1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.PlatformUnsupported);

        viewModel.RefreshLocalizedText();

        Assert.NotEmpty(viewModel.StopConfirm.Message);
        Assert.NotEqual(viewModel.StopConfirm.Message, viewModel.DownloadRunningCloseConfirm.Message);
        Assert.Contains("1.2.0", viewModel.UpdateAvailableText, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowStopConfirm_WhenInvoked_LocalizesDistinctStopMessages()
    {
        var viewModel = CreateViewModel();

        viewModel.ShowStopConfirm();
        viewModel.ShowDownloadRunningCloseConfirm();

        Assert.True(viewModel.StopConfirm.IsVisible);
        Assert.True(viewModel.DownloadRunningCloseConfirm.IsVisible);
        Assert.NotEmpty(viewModel.StopConfirm.Message);
        Assert.NotEqual(viewModel.StopConfirm.Message, viewModel.DownloadRunningCloseConfirm.Message);
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

    private string NextDataRoot() => tempDir.Sub(Guid.NewGuid().ToString("N"));

    private DialogsViewModel CreateViewModel()
    {
        return new DialogsViewModel(
            new LocalizationService(),
            new NoticeStateService(TestDataRoot.ForDirectory(NextDataRoot())),
            new SetupWizardViewModel(new LocalizationService(), new GameInstallationPath(), new LocalInstallationStateStore(), new LocalDiagnostics(), new StubFilePickerService()),
            new LocalDiagnostics(),
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
