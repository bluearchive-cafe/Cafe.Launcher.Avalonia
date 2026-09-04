using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

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
        viewModel.Settings.Editor.Current.Language = LauncherLanguages.Japanese;

        Assert.Equal(0, notificationCount);
    }

    [Fact]
    public async Task Dispose_UnhooksLifecycleCoordinationDelegates()
    {
        var viewModel = await CreateViewModelAsync(new CountingCoreService(CreateSnapshot()));

        Assert.NotNull(viewModel.Settings.Appearance.GetBackgroundBitmap);
        Assert.NotNull(viewModel.Settings.PreviewAppearanceAsync);
        Assert.NotNull(viewModel.Settings.ApplyLanguageAndTheme);
        Assert.NotNull(viewModel.RemoteContent.OpenExternalUrlRequested);

        viewModel.Dispose();

        Assert.Null(viewModel.Settings.Appearance.GetBackgroundBitmap);
        Assert.Null(viewModel.Settings.PreviewAppearanceAsync);
        Assert.Null(viewModel.Settings.ApplyLanguageAndTheme);
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
            await refreshTask;
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
    public async Task InitializeAsync_WhenStartupUpdateCheckEnabledAndUpdateAvailable_ShowsToast()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.EnableStartupUpdateCheck = true;
        snapshot.Settings.UpdateChannel = UpdateChannels.Stable;
        var coreService = new CountingCoreService(snapshot);
        var toasts = new List<string>();
        var toastService = new ToastService();
        toastService.ToastRaised += notification => toasts.Add(notification.Message);
        var releaseJson = """
            [
                {
                    "version": "99.0.0",
                    "files": [
                        {
                            "name": "installer.exe",
                            "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v99.0.0/installer.exe",
                            "size": 123456
                        }
                    ],
                    "releaseDate": "2026-01-01"
                }
            ]
            """;
        var updateSvc = new LauncherUpdateService(
            new LauncherUpdateHandler(releaseJson),
            currentVersionOverride: "1.0.0");
        using var viewModel = await CreateViewModelAsync(
            coreService,
            toastService: toastService,
            launcherUpdateService: updateSvc);

        await viewModel.InitializeAsync();
        await viewModel.PendingStartupUpdateCheck.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Contains(toasts, t => t.Contains("99.0.0"));
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
        // LauncherUpdateHandler returns 404 by default (no releases found = no update)
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
