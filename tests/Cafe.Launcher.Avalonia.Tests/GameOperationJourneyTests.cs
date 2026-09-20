using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Testing;
using Xunit;

namespace Cafe.Launcher.Avalonia.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class GameOperationJourneyTests
{
    static GameOperationJourneyTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public async Task StartGameAsync_WhenHostBusy_SkipsLaunch()
    {
        var context = CreateContext();
        context.Host.IsBusyForce = true;

        await context.Journey.StartGameAsync(CreateSnapshot());

        Assert.Equal(0, context.Executor.LaunchCallCount);
        Assert.Equal(0, context.Host.SetBusyCallCount);
    }

    [Fact]
    public async Task StartGameAsync_WhenLaunchSucceeds_ReportsCheckResultMinimizesAndClearsBusy()
    {
        var context = CreateContext();
        context.Executor.LaunchResult = new GameLaunchResult
        {
            Success = true,
            Message = "launched",
            Validation = new ManifestValidationResult { Message = "validation ok" }
        };
        var notifications = context.SubscribeToasts();

        await context.Journey.StartGameAsync(CreateSnapshot());

        Assert.Equal(1, context.Executor.LaunchCallCount);
        Assert.Equal("validation ok", context.Host.LastLaunchCheckResult);
        Assert.True(context.Host.MinimizeRequested);
        Assert.False(context.Host.IsBusy);
        Assert.Equal(2, context.Host.SetBusyCallCount);
        Assert.Contains(notifications, toast => toast.Severity == ToastSeverity.Success);
    }

    [Fact]
    public async Task StartGameAsync_WhenAfterLaunchBehaviorKeepsWindowOpen_TouchesNeitherWindowAction()
    {
        // "Keep the window open" is the one option that must not reach the host's window verbs
        // at all, otherwise the launcher would move or vanish despite the user asking it not to.
        var context = CreateContext();
        context.Executor.LaunchResult = new GameLaunchResult
        {
            Success = true,
            Message = "launched",
            Validation = new ManifestValidationResult { Message = "validation ok" }
        };
        var notifications = context.SubscribeToasts();

        await context.Journey.StartGameAsync(CreateSnapshot(afterLaunchBehavior: AfterLaunchBehaviors.KeepOpen));

        Assert.False(context.Host.MinimizeRequested);
        Assert.False(context.Host.ExitRequested);
        Assert.Contains(notifications, toast =>
            toast.Severity == ToastSeverity.Success
            && string.Equals(toast.Message, context.Localizer.T(LocalizationKeys.GameLaunched), StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartGameAsync_WhenAfterLaunchBehaviorExits_RequestsExitAndSaysSo()
    {
        var context = CreateContext();
        context.Executor.LaunchResult = new GameLaunchResult
        {
            Success = true,
            Message = "launched",
            Validation = new ManifestValidationResult { Message = "validation ok" }
        };
        var notifications = context.SubscribeToasts();

        await context.Journey.StartGameAsync(CreateSnapshot(afterLaunchBehavior: AfterLaunchBehaviors.Exit));

        Assert.True(context.Host.ExitRequested);
        Assert.False(context.Host.MinimizeRequested);
        // The window is about to go away, so the toast must not promise a tray icon the user
        // will not find.
        Assert.Contains(notifications, toast =>
            toast.Severity == ToastSeverity.Success
            && string.Equals(toast.Message, context.Localizer.T(LocalizationKeys.GameLaunchedExiting), StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartGameAsync_WhenAfterLaunchBehaviorIsUnrecognized_FallsBackToMinimize()
    {
        // Normalization rejects unknown codes on load, so this only covers a snapshot built in
        // code. Falling back to the shipped behavior beats leaving the window covering the game.
        var context = CreateContext();
        context.Executor.LaunchResult = new GameLaunchResult
        {
            Success = true,
            Message = "launched",
            Validation = new ManifestValidationResult { Message = "validation ok" }
        };

        await context.Journey.StartGameAsync(CreateSnapshot(afterLaunchBehavior: "somethingElse"));

        Assert.True(context.Host.MinimizeRequested);
        Assert.False(context.Host.ExitRequested);
    }

    [Fact]
    public async Task StartGameAsync_WhenExecutorThrows_HandlesErrorAndClearsBusy()
    {
        var context = CreateContext();
        context.Executor.LaunchException = new InvalidOperationException("boom");

        await context.Journey.StartGameAsync(CreateSnapshot());

        Assert.Single(context.ErrorHandling.Handled);
        Assert.False(context.Host.IsBusy);
    }

    [Fact]
    public async Task StartGameAsync_WhenLaunchVerificationFindsDamagedFiles_ShowsRepairConfirmation()
    {
        var context = CreateContext();
        context.Executor.LaunchResult = new GameLaunchResult
        {
            Success = false,
            Message = "manifest damaged",
            Validation = new ManifestValidationResult
            {
                Success = false,
                DamagedFileCount = 2,
                MissingFileCount = 1,
                SizeMismatchFileCount = 1,
                Message = "manifest damaged"
            }
        };
        var notifications = context.SubscribeToasts();

        await context.Journey.StartGameAsync(CreateSnapshot());

        // The damage prompt must use its own copy, not the settings-flow repair warning:
        // the dialog appears straight after a failed start, so it has to say why.
        Assert.Equal(
            context.Localizer.T(LocalizationKeys.LaunchDamageRepairPrompt),
            context.Host.RepairConfirmationShown);
        Assert.DoesNotContain(notifications, toast => toast.Severity == ToastSeverity.Warning);
        Assert.False(context.Host.MinimizeRequested);
        Assert.False(context.Host.IsBusy);
    }

    [Fact]
    public async Task StartGameAsync_WhenLaunchFailsWithoutDamagedFiles_ShowsWarningToast()
    {
        // GameLaunchService.Failed() shapes state and path failures as Success = false with
        // every counter left at zero. Those must stay a toast and never open the repair
        // prompt, so this guards the damage discriminator against future drift.
        var context = CreateContext();
        context.Executor.LaunchResult = new GameLaunchResult
        {
            Success = false,
            Message = "update available",
            Validation = new ManifestValidationResult
            {
                Success = false,
                Message = "update available"
            }
        };
        var notifications = context.SubscribeToasts();

        await context.Journey.StartGameAsync(CreateSnapshot());

        Assert.Null(context.Host.RepairConfirmationShown);
        Assert.Contains(notifications, toast =>
            toast.Severity == ToastSeverity.Warning && toast.Message == "update available");
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenReady_RequestsRefreshAndShowsUpToDateToast()
    {
        var context = CreateContext();
        context.Host.CurrentSnapshot = CreateSnapshot(LauncherRuntimeState.Ready);
        var notifications = context.SubscribeToasts();

        await context.Journey.CheckForUpdateAsync(CreateSnapshot(LauncherRuntimeState.Ready));

        Assert.Contains(GameOperationsRefreshMode.SkipPersistedResume, context.Host.RefreshRequests);
        Assert.Contains(notifications, toast => toast.Severity == ToastSeverity.Success);
        Assert.False(context.Host.IsBusy);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenCorrupted_ShowsRepairConfirmationWithoutInstall()
    {
        var context = CreateContext();

        await context.Journey.InstallOrUpdateAsync(CreateSnapshot(LauncherRuntimeState.Corrupted));

        Assert.Equal(0, context.Executor.InstallCallCount);
        Assert.NotNull(context.Host.RepairConfirmationShown);
        Assert.False(context.Host.IsBusy);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenReady_RunsNothingAndRestoresSnapshot()
    {
        var context = CreateContext();
        var snapshot = CreateSnapshot(LauncherRuntimeState.Ready);
        context.Host.CurrentSnapshot = null;

        await context.Journey.InstallOrUpdateAsync(snapshot);

        Assert.Equal(0, context.Executor.InstallCallCount);
        Assert.Empty(context.Host.RefreshRequests);
        Assert.Same(snapshot, context.Host.CurrentSnapshot);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenTerminalFailure_ShowsErrorToastWithoutRetry()
    {
        var context = CreateContext();
        context.Executor.InstallResult = new GameOperationResult
        {
            Success = false,
            Message = "path missing",
            ErrorCode = GameOperationErrorCode.PathMissing
        };
        var notifications = context.SubscribeToasts();

        await context.Journey.InstallOrUpdateAsync(CreateSnapshot(LauncherRuntimeState.UpdateAvailable));

        Assert.Contains(notifications, toast =>
            toast.Severity == ToastSeverity.Error
            && toast.Message == "path missing"
            && toast.PrimaryAction is null
            && toast.SecondaryAction is not null);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenRetryableFailure_ShowsRetryToastAndRetryRunsInstallAgain()
    {
        var context = CreateContext();
        context.Executor.InstallResult = new GameOperationResult
        {
            Success = false,
            Message = "network hiccup",
            ErrorCode = GameOperationErrorCode.Network
        };
        var notifications = context.SubscribeToasts();
        var snapshot = CreateSnapshot(LauncherRuntimeState.UpdateAvailable);

        await context.Journey.InstallOrUpdateAsync(snapshot);

        var retryToast = notifications.Single(toast => toast.PrimaryAction is not null);
        context.Executor.InstallResult = new GameOperationResult { Success = true, Message = "done" };

        var retryOutcome = await retryToast.PrimaryAction!.ExecuteAsync(CancellationToken.None);

        Assert.True(retryOutcome.IsSuccess);
        Assert.Equal(2, context.Executor.InstallCallCount);
    }

    [Fact]
    public async Task RepairAsync_WhenRepairSucceeds_RequestsNormalRefreshAndClearsBusy()
    {
        var context = CreateContext();

        await context.Journey.RepairAsync(CreateSnapshot(LauncherRuntimeState.Corrupted));

        Assert.Equal(1, context.Executor.RepairCallCount);
        Assert.Contains(GameOperationsRefreshMode.Normal, context.Host.RefreshRequests);
        Assert.False(context.Host.IsBusy);
    }

    [Fact]
    public async Task ConfirmUninstallAsync_WhenStateNoLongerAllowsUninstall_ReportsInsteadOfSilence()
    {
        // 用户已经点过确认：状态若在这之后变得不允许，不能什么都不做（ADR-027）。
        var context = CreateContext();
        var notifications = context.SubscribeToasts();

        await context.Journey.ConfirmUninstallAsync(
            CreateSnapshot(LauncherRuntimeState.NotInstalled),
            UninstallScope.ManifestFilesOnly);

        Assert.Equal(0, context.Executor.UninstallCallCount);
        Assert.Equal(0, context.Host.SetBusyCallCount);
        var notification = Assert.Single(notifications);
        Assert.Equal(ToastSeverity.Warning, notification.Severity);
        Assert.Equal(
            context.Localizer.T(LocalizationKeys.OperationUnavailableForCurrentState),
            notification.Message);
    }

    [Fact]
    public async Task ValidateUninstallAsync_WhenValidationFails_ReportsTheExecutorReason()
    {
        // 预检失败必须可见（ADR-029）：卸载按钮此刻可点，静默返回 null 就是「点了没反应」。
        // 报的是执行层给出的具体原因（路径缺失／受保护／目录名非法），不是笼统的「不可用」。
        var context = CreateContext();
        var notifications = context.SubscribeToasts();
        context.Executor.ValidateUninstallResult = new GameOperationResult
        {
            Success = false,
            Message = "game path is gone"
        };

        var validation = await context.Journey.ValidateUninstallAsync(CreateSnapshot());

        Assert.Null(validation);
        Assert.Equal(1, context.Executor.ValidateUninstallCallCount);
        var notification = Assert.Single(notifications);
        Assert.Equal(ToastSeverity.Warning, notification.Severity);
        Assert.Equal("game path is gone", notification.Message);
    }

    [Fact]
    public async Task ValidateUninstallAsync_WhenValidationSucceeds_ReturnsTheResultWithoutReporting()
    {
        // 反向守卫：成功路径不得因为「总是报一句」而多出一个 Toast。
        var context = CreateContext();
        var notifications = context.SubscribeToasts();
        context.Executor.ValidateUninstallResult = new GameOperationResult
        {
            Success = true,
            AffectedFileCount = 5
        };

        var validation = await context.Journey.ValidateUninstallAsync(CreateSnapshot());

        Assert.NotNull(validation);
        Assert.Equal(5, validation.AffectedFileCount);
        Assert.Empty(notifications);
    }

    [Theory]
    [InlineData(UninstallScope.ManifestFilesOnly)]
    [InlineData(UninstallScope.ThoroughCleanup)]
    public async Task ConfirmUninstallAsync_ForwardsTheRequestedScopeToTheExecutor(UninstallScope scope)
    {
        var context = CreateContext();

        await context.Journey.ConfirmUninstallAsync(CreateSnapshot(), scope);

        Assert.Equal(1, context.Executor.UninstallCallCount);
        Assert.Equal(scope, context.Executor.LastUninstallScope);
    }

    [Fact]
    public async Task ConfirmUninstallAsync_WhenUninstallSucceeds_ReportsTheResult()
    {
        // 从前这里把终态丢掉，成功也没有回声（ADR-030 顺带修）。
        var context = CreateContext();
        var notifications = context.SubscribeToasts();
        context.Executor.UninstallResult = new GameOperationResult { Success = true, Message = "uninstalled" };

        await context.Journey.ConfirmUninstallAsync(CreateSnapshot(), UninstallScope.ManifestFilesOnly);

        var notification = Assert.Single(notifications);
        Assert.Equal(ToastSeverity.Success, notification.Severity);
        Assert.Equal("uninstalled", notification.Message);
    }

    [Fact]
    public async Task ConfirmUninstallAsync_WhenUninstallFails_ReportsTheFailureInsteadOfSilence()
    {
        // 逐文件删除撞上占用/权限时用户必须看到发生了什么（ADR-030）。
        var context = CreateContext();
        var notifications = context.SubscribeToasts();
        context.Executor.UninstallResult = new GameOperationResult
        {
            Success = false,
            Message = "game files are locked",
            ErrorCode = GameOperationErrorCode.System
        };

        await context.Journey.ConfirmUninstallAsync(CreateSnapshot(), UninstallScope.ThoroughCleanup);

        var notification = Assert.Single(notifications);
        Assert.Equal(ToastSeverity.Error, notification.Severity);
        Assert.Equal("game files are locked", notification.Message);
    }

    [Fact]
    public async Task ConfirmUninstallAsync_WhenUninstallThrows_ReportsTheFailureInsteadOfSilence()
    {
        // 抛出路径同样要落到用户眼前（ADR-030）。实测：彻底清除在倒序删除阶段抛出时，
        // 从前只记日志——界面回到「未安装」、目录还留在盘上，而用户看不到任何原因。
        var context = CreateContext();
        context.Executor.UninstallException = new IOException("目录不是空的。");

        await context.Journey.ConfirmUninstallAsync(CreateSnapshot(), UninstallScope.ThoroughCleanup);

        var handled = Assert.Single(context.ErrorHandling.Handled);
        Assert.Equal("Game uninstall failed.", handled.Context);
        var options = Assert.Single(context.ErrorHandling.HandledOptions);
        Assert.NotNull(options);
        Assert.True(options!.ShowToast, "卸载抛出时不能只记日志，必须给用户可见结果。");
        Assert.Contains("目录不是空的", options.ToastMessage, StringComparison.Ordinal);
        Assert.False(context.Host.IsBusy);
    }

    [Fact]
    public async Task MeasureUninstallFootprintAsync_ReturnsTheExecutorMeasurement()
    {
        var context = CreateContext();
        context.Executor.MeasureFootprintResult = new UninstallFootprint(2048, 512);

        var footprint = await context.Journey.MeasureUninstallFootprintAsync(CreateSnapshot());

        Assert.Equal(1, context.Executor.MeasureFootprintCallCount);
        Assert.Equal(2048, footprint.InstallDirectoryBytes);
        Assert.Equal(512, footprint.PrefixBytes);
    }

    [Fact]
    public async Task ResumePersistedAsync_WhenHostBusy_SkipsResume()
    {
        var context = CreateContext();
        context.Host.IsBusyForce = true;

        await context.Journey.ResumePersistedAsync(CreateSnapshot(), CancellationToken.None);

        Assert.Equal(0, context.Executor.ResumeCallCount);
    }

    [Fact]
    public async Task ResumePersistedAsync_WhenResultNull_SkipsRefreshAndClearsBusy()
    {
        var context = CreateContext();
        context.Executor.ResumeResult = null;

        await context.Journey.ResumePersistedAsync(CreateSnapshot(), CancellationToken.None);

        Assert.Equal(1, context.Executor.ResumeCallCount);
        Assert.Empty(context.Host.RefreshRequests);
        Assert.False(context.Host.IsBusy);
    }

    [Fact]
    public void PerformStop_StopsExecutorAndShowsWarningToast()
    {
        var context = CreateContext();
        var notifications = context.SubscribeToasts();

        context.Journey.PerformStop();

        Assert.Equal(1, context.Executor.StopCallCount);
        Assert.Equal(DownloadStopReason.UserRequested, context.Executor.LastStopReason);
        Assert.Contains(notifications, toast => toast.Severity == ToastSeverity.Warning);
    }

    private static LauncherStatusSnapshot CreateSnapshot(
        LauncherRuntimeState runtimeState = LauncherRuntimeState.Ready,
        string afterLaunchBehavior = AfterLaunchBehaviors.Minimize)
    {
        return new LauncherStatusSnapshot
        {
            RuntimeState = runtimeState,
            LocalGame = new LocalInstallationState(),
            Remote = new LauncherRemoteState(),
            Settings = new LauncherSettings { AfterLaunchBehavior = afterLaunchBehavior }
        };
    }

    private static JourneyTestContext CreateContext()
    {
        // Succeeding 预设与原 RecordingOperationExecutor 一致：所有操作默认成功，
        // 让旅程测试直接走成功分支，只有显式改写结果的用例才关心失败路径。
        var executor = StubGameOperationExecutor.Succeeding();
        var host = new RecordingJourneyHost();
        var errorHandling = new RecordingErrorHandlingService();
        var toastService = new ToastService();
        var localizer = new LocalizationService();
        var journey = new GameOperationJourney(
            executor,
            new TestGameShortcutService(),
            localizer,
            toastService,
            new LocalDiagnostics(),
            errorHandling,
            _ => Task.CompletedTask,
            host);
        return new JourneyTestContext(journey, executor, host, errorHandling, toastService, localizer);
    }

    private sealed record JourneyTestContext(
        GameOperationJourney Journey,
        StubGameOperationExecutor Executor,
        RecordingJourneyHost Host,
        RecordingErrorHandlingService ErrorHandling,
        ToastService ToastService,
        LocalizationService Localizer)
    {
        public List<ToastNotification> SubscribeToasts()
        {
            var notifications = new List<ToastNotification>();
            ToastService.ToastRaised += notifications.Add;
            return notifications;
        }
    }

    private sealed class RecordingJourneyHost : IGameOperationJourneyHost
    {
        public bool IsBusyForce { get; set; }

        public bool IsBusy => IsBusyForce || busyState;

        private bool busyState;

        public LauncherStatusSnapshot? CurrentSnapshot { get; set; }

        public int SetBusyCallCount { get; private set; }

        public bool PrepareOperationCalled { get; private set; }

        public string? LastLaunchCheckResult { get; private set; }

        public string? RepairConfirmationShown { get; private set; }

        public List<GameOperationsRefreshMode> RefreshRequests { get; } = [];

        public bool MinimizeRequested { get; private set; }

        public bool ExitRequested { get; private set; }

        public void PrepareOperation() => PrepareOperationCalled = true;

        public void ApplyProgress(GameOperationProgress progress)
        {
        }

        public void ApplySnapshot(LauncherStatusSnapshot snapshot) => CurrentSnapshot = snapshot;

        public void SetBusy(bool busy)
        {
            busyState = busy;
            SetBusyCallCount++;
        }

        public void SetLaunchCheckResult(string message) => LastLaunchCheckResult = message;

        public void ShowRepairConfirmation(string message) => RepairConfirmationShown = message;

        public Task<bool> RefreshAsync(GameOperationsRefreshMode mode)
        {
            RefreshRequests.Add(mode);
            return Task.FromResult(true);
        }

        public Task ShowLogViewerAsync() => Task.CompletedTask;

        public void RequestMinimize() => MinimizeRequested = true;

        public void RequestExit() => ExitRequested = true;
    }
}
