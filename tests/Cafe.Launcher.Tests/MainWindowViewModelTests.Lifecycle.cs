using Cafe.Launcher.UI.Features.Settings;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Tests;

public partial class MainWindowViewModelTests
{
    [Fact]
    public async Task Dispose_UnsubscribesSettingsEditorNotifications()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        var viewModel = await CreateViewModelAsync(coreService);
        viewModel.Settings.Editor.ApplySnapshot(new LauncherSettings());
        var notificationCount = 0;
        viewModel.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.IsSettingsDirty))
            {
                notificationCount++;
            }
        };

        viewModel.Dispose();
        // 表现图归容器所有：编辑器订阅随容器释放退订，而不是随窗口 ViewModel 单独释放。
        contexts[^1].Dispose();
        viewModel.Settings.Editor.Current.Language = LauncherLanguages.Japanese;

        Assert.Equal(0, notificationCount);
    }

    [Fact]
    public async Task Dispose_UnhooksLifecycleCoordinationDelegates()
    {
        var viewModel = await CreateViewModelAsync(new CountingCoreService(CreateSnapshot()));

        Assert.NotNull(viewModel.Settings.Appearance.GetBackgroundBitmapAsync);
        Assert.NotNull(viewModel.Settings.PreviewAppearanceAsync);
        Assert.NotNull(viewModel.Settings.SavedSettingsFollowThrough);
        Assert.NotNull(viewModel.RemoteContent.OpenExternalUrlRequested);

        viewModel.Dispose();

        Assert.Null(viewModel.Settings.Appearance.GetBackgroundBitmapAsync);
        Assert.Null(viewModel.Settings.PreviewAppearanceAsync);
        Assert.Null(viewModel.Settings.SavedSettingsFollowThrough);
        Assert.Null(viewModel.RemoteContent.OpenExternalUrlRequested);
    }

    [Fact]
    public async Task Constructor_DoesNotLoadLauncherState()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);

        Assert.Equal(0, coreService.LoadCount);
    }

    [Fact]
    public async Task InitializeAsync_WhenCalledTwice_LoadsLauncherStateOnce()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);

        await viewModel.InitializeAsync();
        await viewModel.InitializeAsync();

        Assert.Equal(1, coreService.LoadCount);
    }

    [Fact]
    public async Task HandleOperationsRefreshRequestedAsync_ConsumesSkipPersistedResumeOnce()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        var backend = new StubGameOperationExecutor();
        using var viewModel = await CreateViewModelAsync(coreService, gameOperationsBackend: backend);

        await viewModel.HandleOperationsRefreshRequestedAsync(GameOperationsRefreshMode.Normal);
        Assert.Equal(1, backend.ResumeCallCount);

        await viewModel.HandleOperationsRefreshRequestedAsync(GameOperationsRefreshMode.SkipPersistedResume);
        Assert.Equal(1, backend.ResumeCallCount);

        await viewModel.HandleOperationsRefreshRequestedAsync(GameOperationsRefreshMode.Normal);
        Assert.Equal(2, backend.ResumeCallCount);
        Assert.Equal(3, coreService.LoadCount);
    }

    [Fact]
    public async Task ShellSetLoading_UsesPureLoadingValuesForStatusDetails()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);

        viewModel.Shell.SetLoading();

        Assert.Equal(viewModel.Shell.ExecutableNameText, viewModel.Shell.LaunchCheckValueText);
        Assert.DoesNotContain(':', viewModel.Shell.ExecutableNameText);
        Assert.DoesNotContain('：', viewModel.Shell.ExecutableNameText);
    }

    [Fact]
    public async Task ShellSetLaunchCheckResult_UpdatesPureStatusDetailValue()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);

        viewModel.Shell.SetLaunchCheckResult("manifest verified");

        Assert.Equal("manifest verified", viewModel.Shell.LaunchCheckValueText);
        Assert.DoesNotContain("Launch check:", viewModel.Shell.LaunchCheckValueText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("启动校验：", viewModel.Shell.LaunchCheckValueText, StringComparison.Ordinal);
        Assert.DoesNotContain("起動チェック：", viewModel.Shell.LaunchCheckValueText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_WhenSnapshotLoads_PopulatesPureStatusDetailValues()
    {
        var snapshot = CreateSnapshot();
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService);

        await viewModel.InitializeAsync();

        Assert.Equal("BlueArchive.exe", viewModel.Shell.ExecutableNameText);
        Assert.Equal(
            viewModel.Settings.Options.ResolveLaunchCheckDisplayName(snapshot.Settings.LaunchCheckMode),
            viewModel.Shell.LaunchCheckValueText);
        Assert.DoesNotContain("Executable", viewModel.Shell.ExecutableNameText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("启动程序", viewModel.Shell.ExecutableNameText, StringComparison.Ordinal);
        Assert.DoesNotContain("実行ファイル", viewModel.Shell.ExecutableNameText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAsync_WhileCoreLoadIsPending_ShowsRemoteContentLoadingState()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.ShowRemoteContentCard = true;
        snapshot.Remote.OperationsResource = CreateOperationsResource();
        var coreService = new BlockingSecondLoadCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService);
        await viewModel.InitializeAsync();

        var refreshTask = viewModel.RefreshCommand.ExecuteAsync(null);
        await coreService.SecondLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            Assert.True(viewModel.IsBusy);
            Assert.True(viewModel.RemoteContent.IsLoading);
            Assert.True(viewModel.RemoteContent.IsPanelVisible);
        }
        finally
        {
            coreService.ReleaseSecondLoad.TrySetResult();
            await refreshTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.RemoteContent.IsLoading);
        Assert.True(viewModel.RemoteContent.IsPanelVisible);
    }

    [Fact]
    public async Task PrepareForShutdownAsync_WhileRefreshIsPending_CancelsAndDrainsRefresh()
    {
        var coreService = new BlockingSecondLoadCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);
        await viewModel.InitializeAsync();

        Task refreshTask = viewModel.RefreshCommand.ExecuteAsync(null);
        await coreService.SecondLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await viewModel.PrepareForShutdownAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await refreshTask.WaitAsync(TimeSpan.FromSeconds(2));
        await viewModel.HandleOperationsRefreshRequestedAsync(
            GameOperationsRefreshMode.SkipPersistedResume);

        Assert.Equal(2, coreService.LoadCount);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.RemoteContent.IsLoading);
    }

    [Theory]
    [InlineData("install")]
    [InlineData("repair")]
    [InlineData("resume")]
    public async Task PrepareForShutdownAsync_WhileGameOperationIsPending_WaitsForItsCleanup(string kind)
    {
        var snapshot = CreateSnapshot();
        snapshot.RuntimeState = kind == "install" ? LauncherRuntimeState.NotInstalled : LauncherRuntimeState.Ready;
        var backend = new StubGameOperationExecutor();
        using var viewModel = await CreateViewModelAsync(new CountingCoreService(snapshot), gameOperationsBackend: backend);
        await viewModel.InitializeAsync();
        var completion = new TaskCompletionSource<GameOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeCompletion = new TaskCompletionSource<GameOperationResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.InstallCompletion = completion;
        backend.RepairCompletion = completion;
        backend.ResumeCompletion = resumeCompletion;
        var initialCalls = backend.InstallCallCount + backend.RepairCallCount + backend.ResumeCallCount;
        var operation = kind switch
        {
            "install" => viewModel.Operations.InstallOrUpdateCommand.ExecuteAsync(null),
            "repair" => viewModel.Operations.RepairAsync(),
            _ => viewModel.Operations.ResumePersistedDownloadAsync(CancellationToken.None)
        };
        try
        {
            Assert.Equal(initialCalls + 1, backend.InstallCallCount + backend.RepairCallCount + backend.ResumeCallCount);
            Assert.False(operation.IsCompleted);
            var shutdown = viewModel.PrepareForShutdownAsync();
            Assert.False(shutdown.IsCompleted);
            Assert.Equal(DownloadStopReason.ApplicationExit, backend.LastStopReason);
            completion.TrySetResult(new GameOperationResult { ErrorCode = GameOperationErrorCode.Stopped });
            resumeCompletion.TrySetResult(null);
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5));

            var calls = backend.InstallCallCount + backend.RepairCallCount + backend.ResumeCallCount;
            viewModel.Shell.IsBusy = false;
            await viewModel.Operations.InstallOrUpdateCommand.ExecuteAsync(null);
            await viewModel.Operations.RepairAsync();
            await viewModel.Operations.ResumePersistedDownloadAsync(CancellationToken.None);
            Assert.Equal(calls, backend.InstallCallCount + backend.RepairCallCount + backend.ResumeCallCount);
        }
        finally
        {
            completion.TrySetResult(new GameOperationResult());
            resumeCompletion.TrySetResult(null);
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task RefreshAsync_WhenRequestsOverlap_SerializesLoadsAndKeepsNewestSnapshot()
    {
        var initial = CreateSnapshot();
        initial.RuntimeState = LauncherRuntimeState.NotInstalled;
        var older = CreateSnapshot();
        older.RuntimeState = LauncherRuntimeState.Corrupted;
        var newest = CreateSnapshot();
        newest.RuntimeState = LauncherRuntimeState.Ready;
        var coreService = new SequencedBlockingCoreService(initial, older, newest);
        using var viewModel = await CreateViewModelAsync(coreService);
        await viewModel.InitializeAsync();

        var olderRefresh = viewModel.HandleOperationsRefreshRequestedAsync(
            GameOperationsRefreshMode.SkipPersistedResume);
        await coreService.SecondLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var newestRefresh = viewModel.HandleOperationsRefreshRequestedAsync(
            GameOperationsRefreshMode.SkipPersistedResume);

        coreService.ReleaseSecondLoad.TrySetResult();
        await Task.WhenAll(olderRefresh, newestRefresh).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, coreService.MaximumConcurrency);
        Assert.Equal(3, coreService.LoadCount);
        Assert.True(viewModel.Operations.IsControlPanelVisible);
        Assert.False(viewModel.Operations.IsInstallPanelVisible);
    }

    [Fact]
    public async Task InitializeAsync_WhenCoreLoadFails_DoesNotShowNetworkLoadedToast()
    {
        var coreService = new ThrowingCoreService();
        var successToasts = new List<string>();
        var toastService = new ToastService();
        using var viewModel = await CreateViewModelAsync(coreService, toastService: toastService);
        toastService.ToastRaised += notification =>
        {
            if (notification.Severity == ToastSeverity.Success)
            {
                successToasts.Add(notification.Message);
            }
        };

        await viewModel.InitializeAsync();

        Assert.DoesNotContain(viewModel.Shell.I18n["statusNetworkLoaded"], successToasts);
        Assert.False(viewModel.RemoteContent.IsLoading);
    }

    [Fact]
    public async Task InitializeAsync_WhenStartupUpdateCheckEnabledAndUpdateAvailable_ShowsToastThatOpensDialogWithoutRechecking()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.EnableStartupUpdateCheck = true;
        snapshot.Settings.UpdateChannel = UpdateChannels.Stable;
        snapshot.Settings.Language = LauncherLanguages.English;
        var coreService = new CountingCoreService(snapshot);
        var toasts = new List<ToastNotification>();
        var toastService = new ToastService();
        toastService.ToastRaised += toasts.Add;
        var releaseJson = """
            [
                {
                    "version": "99.0.0",
                    "files": [
                        {
                            "name": "installer.exe",
                            "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v99.0.0/installer.exe",
                            "size": 123456
                        }
                    ],
                    "releaseDate": "2026-01-01"
                }
            ]
            """;
        var transportCalls = 0;
        var transport = new StubRemoteHttpTransport(uri =>
        {
            transportCalls++;
            return uri == new Uri(
                new Uri(LauncherProfiles.Cafe.LauncherApiBaseUrl),
                LauncherProfiles.Cafe.GetLauncherReleaseNotesPath("99.0.0"))
                ? """{"releaseNotes":"## 99.0.0 notes"}"""
                : releaseJson;
        });
        var updateSvc = new LauncherUpdateService(
            LauncherProfiles.Cafe,
            transport,
            currentVersionOverride: "1.0.0");
        using var viewModel = await CreateViewModelAsync(
            coreService,
            toastService: toastService,
            launcherUpdateService: updateSvc);

        await viewModel.InitializeAsync();
        await viewModel.PendingStartupUpdateCheck.WaitAsync(TimeSpan.FromSeconds(2));

        ToastNotification updateToast = Assert.Single(toasts, t => t.Message.Contains("99.0.0"));
        var action = Assert.IsType<ToastAction>(updateToast.PrimaryAction);
        Assert.Equal("View Update", action.Label);
        var callsAfterStartup = transportCalls;

        var outcome = await action.ExecuteAsync(CancellationToken.None);

        // 动作复用启动检查的结果：开对话框不再发起第二次版本检查，
        // 否则「有更新」的提示可能被一次失败或「已最新」当场推翻。
        // 唯一新增的请求是按已选版本补充更新说明。
        Assert.True(outcome.IsSuccess);
        Assert.Equal("99.0.0", viewModel.Dialogs.UpdateAvailableVersion);
        Assert.True(viewModel.Dialogs.IsUpdateAvailableVisible);
        Assert.Equal(callsAfterStartup + 1, transportCalls);
        Assert.Equal(
            new Uri(
                new Uri(LauncherProfiles.Cafe.LauncherApiBaseUrl),
                LauncherProfiles.Cafe.GetLauncherReleaseNotesPath("99.0.0")),
            transport.RequestedUris[^1]);
    }

    [Fact]
    public async Task InitializeAsync_WhenStartupUpdateCheckDisabled_DoesNotShowToast()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.EnableStartupUpdateCheck = false;
        snapshot.Settings.UpdateChannel = UpdateChannels.Stable;
        var coreService = new CountingCoreService(snapshot);
        var toasts = new List<string>();
        var toastService = new ToastService();
        toastService.ToastRaised += notification => toasts.Add(notification.Message);
        using var viewModel = await CreateViewModelAsync(
            coreService,
            toastService: toastService);

        await viewModel.InitializeAsync();
        await viewModel.PendingStartupUpdateCheck.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(toasts, t => t.Contains("available"));
    }

    [Fact]
    public async Task InitializeAsync_WhenStartupUpdateCheckEnabledButNoUpdate_DoesNotShowToast()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.EnableStartupUpdateCheck = true;
        snapshot.Settings.UpdateChannel = UpdateChannels.Stable;
        var coreService = new CountingCoreService(snapshot);
        var toasts = new List<string>();
        var toastService = new ToastService();
        toastService.ToastRaised += notification => toasts.Add(notification.Message);
        // The default stub transport answers with an empty body (no releases found = no update).
        using var viewModel = await CreateViewModelAsync(
            coreService,
            toastService: toastService);

        await viewModel.InitializeAsync();
        await viewModel.PendingStartupUpdateCheck.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(toasts, t => t.Contains("available"));
    }

    private sealed class SequencedBlockingCoreService(
        LauncherStatusSnapshot initial,
        LauncherStatusSnapshot older,
        LauncherStatusSnapshot newest) : ILauncherCoreService
    {
        private int loadCount;
        private int currentConcurrency;
        private int maximumConcurrency;

        public int LoadCount => Volatile.Read(ref loadCount);
        public int MaximumConcurrency => Volatile.Read(ref maximumConcurrency);

        public TaskCompletionSource SecondLoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSecondLoad { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<LauncherStatusSnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            var invocation = Interlocked.Increment(ref loadCount);
            var concurrency = Interlocked.Increment(ref currentConcurrency);
            UpdateMaximum(concurrency);
            try
            {
                if (invocation == 2)
                {
                    SecondLoadStarted.TrySetResult();
                    await ReleaseSecondLoad.Task.WaitAsync(cancellationToken);
                }

                return invocation switch
                {
                    1 => initial,
                    2 => older,
                    _ => newest
                };
            }
            finally
            {
                Interlocked.Decrement(ref currentConcurrency);
            }
        }

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref maximumConcurrency);
                if (current >= value
                    || Interlocked.CompareExchange(ref maximumConcurrency, value, current) == current)
                {
                    return;
                }
            }
        }
    }
}
