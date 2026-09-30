using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.UI.Services.GameRuntime;
using Cafe.Launcher.Testing;
using Xunit;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services.GameRuntime;

namespace Cafe.Launcher.Tests;

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
    public async Task StartGameAsync_WhenLaunchSucceeds_HandsSessionToMonitor()
    {
        // 启动报告的「成功」只覆盖 spawn 那一刻；会话看护从旅程这里接管此后的事实（ADR-035）。
        var context = CreateContext();
        context.Executor.LaunchResult = new GameLaunchResult
        {
            Success = true,
            Message = "launched",
            RunnerId = "umu",
            KnownExeNames = ["BlueArchive"],
            Validation = new ManifestValidationResult { Message = "validation ok" }
        };

        await context.Journey.StartGameAsync(CreateSnapshot());

        Assert.Equal(1, context.SessionMonitor.BeginSessionCallCount);
        Assert.Equal(("umu", (IReadOnlyList<string>)["BlueArchive"]), context.SessionMonitor.LastBeginSession);
    }

    [Fact]
    public async Task StartGameAsync_WhenLaunchFails_MonitorIsNotToldAboutASession()
    {
        var context = CreateContext();
        context.Executor.LaunchResult = new GameLaunchResult
        {
            Success = false,
            Message = "no runner",
            Validation = new ManifestValidationResult { Message = "no runner" }
        };

        await context.Journey.StartGameAsync(CreateSnapshot());

        Assert.Equal(0, context.SessionMonitor.BeginSessionCallCount);
    }

    [Fact]
    public void NotifySessionStartFailed_RestoresWindowAndReportsTheExitCode()
    {
        // 运行器提前退出时窗口多半在托盘里：必须先恢复窗口，错误才送达得到（ADR-035）。
        var context = CreateContext();
        var notifications = context.SubscribeToasts();
        var exitCode = unchecked((int)0xC0000135);
        var exit = new GameLaunchExitInfo(exitCode, TimeSpan.FromSeconds(2.4), DateTimeOffset.Now, "wine");

        context.Journey.NotifySessionStartFailed(exit);

        Assert.True(context.Host.ShowRequested);
        Assert.Contains(notifications, toast =>
            toast.Severity == ToastSeverity.Error
            && string.Equals(
                toast.Message,
                context.Localizer.F(LocalizationKeys.GameSessionStartFailed, exitCode),
                StringComparison.Ordinal));
    }

    [Fact]
    public void NotifySessionStartFailed_WhenExitInfoMissing_ReportsAnUnknownExitCode()
    {
        // 退出信息缺失（Register→订阅竞态的极端档）也必须报出来，退出码按未知 -1 呈现。
        var context = CreateContext();
        var notifications = context.SubscribeToasts();

        context.Journey.NotifySessionStartFailed(null);

        Assert.True(context.Host.ShowRequested);
        Assert.Contains(notifications, toast =>
            toast.Severity == ToastSeverity.Error
            && string.Equals(
                toast.Message,
                context.Localizer.F(LocalizationKeys.GameSessionStartFailed, -1),
                StringComparison.Ordinal));
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
    public async Task InstallOrUpdateAsync_WhenNotInstalledIntoAFolderThatHasContent_SaysOnlyTheManifestIsInstalled()
    {
        // 2026-09-29 反馈：卸载只删清单内的文件，重启安装时目录里还留着游戏自行下载的一大片
        // （实测清单 157 个文件 / 1.06 GiB，而官方声明的安装是 18.5 GB）。那条路径下「安装」
        // 只补回清单内的一小部分，用户看到的是「忙活一下就好了」——所以开始前就要说明。
        using var temp = TestDirectory.Create();
        var gamePath = Path.Combine(temp.Path, "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        await File.WriteAllTextAsync(Path.Combine(gamePath, "leftover.bin"), "x");
        var context = CreateContext();
        var notifications = context.SubscribeToasts();

        await context.Journey.InstallOrUpdateAsync(
            CreateSnapshot(LauncherRuntimeState.NotInstalled, gamePath: gamePath));

        Assert.Equal(1, context.Executor.InstallCallCount);
        var expectedNotice = context.Localizer.F(LocalizationKeys.InstallOverExistingContentNotice, gamePath);
        // 提示必须先出现：「安装完成」的成功 toast 也会升起（ShowOperationResult），
        // 若它先到，用户读到的顺序就反了。
        Assert.Same(notifications[0], Assert.Single(notifications, toast => toast.Message == expectedNotice));
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenNotInstalledIntoAnEmptyFolder_SaysNothingExtra()
    {
        // 空目录上的全新安装没有任何意外，多一句提示只是噪音。
        using var temp = TestDirectory.Create();
        var gamePath = Path.Combine(temp.Path, "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        var context = CreateContext();
        var notifications = context.SubscribeToasts();

        await context.Journey.InstallOrUpdateAsync(
            CreateSnapshot(LauncherRuntimeState.NotInstalled, gamePath: gamePath));

        Assert.Equal(1, context.Executor.InstallCallCount);
        Assert.DoesNotContain(
            notifications,
            toast => toast.Message
                == context.Localizer.F(LocalizationKeys.InstallOverExistingContentNotice, gamePath));
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenTheFolderDoesNotExistYet_SaysNothingExtra()
    {
        using var temp = TestDirectory.Create();
        var gamePath = Path.Combine(temp.Path, "not-created-yet");
        var context = CreateContext();
        var notifications = context.SubscribeToasts();

        await context.Journey.InstallOrUpdateAsync(
            CreateSnapshot(LauncherRuntimeState.NotInstalled, gamePath: gamePath));

        Assert.Equal(1, context.Executor.InstallCallCount);
        Assert.DoesNotContain(
            notifications,
            toast => toast.Message
                == context.Localizer.F(LocalizationKeys.InstallOverExistingContentNotice, gamePath));
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenUpdatingAnExistingInstall_OnlyNotesTheFolderForFreshInstalls()
    {
        // 更新路径上「目录里本来就有东西」是常态、也是用户预期，不必提示；提示留给
        // 启动器不认这份安装（NotInstalled）却看见目录非空的那一次。
        using var temp = TestDirectory.Create();
        var gamePath = Path.Combine(temp.Path, "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        await File.WriteAllTextAsync(Path.Combine(gamePath, "managed.bin"), "x");
        var context = CreateContext();
        var notifications = context.SubscribeToasts();

        await context.Journey.InstallOrUpdateAsync(
            CreateSnapshot(LauncherRuntimeState.UpdateAvailable, gamePath: gamePath));

        Assert.Equal(1, context.Executor.InstallCallCount);
        Assert.DoesNotContain(
            notifications,
            toast => toast.Message
                == context.Localizer.F(LocalizationKeys.InstallOverExistingContentNotice, gamePath));
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
        var context = CreateContext();
        var notifications = context.SubscribeToasts();
        var result = await context.Journey.ConfirmUninstallAsync(
            CreateSnapshot(LauncherRuntimeState.NotInstalled), UninstallScope.GameDirectory);
        Assert.False(result.Success);
        Assert.Equal(context.Localizer.T(LocalizationKeys.OperationUnavailableForCurrentState), result.Message);
        Assert.Equal(0, context.Executor.UninstallCallCount);
        Assert.Equal(0, context.Host.SetBusyCallCount);
        Assert.Empty(notifications);
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
    [InlineData(UninstallScope.GameDirectory)]
    [InlineData(UninstallScope.GameDirectoryAndManagedCompatibility)]
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
        var context = CreateContext();
        var notifications = context.SubscribeToasts();
        context.Executor.UninstallResult = new GameOperationResult { Success = true, Message = "uninstalled" };
        var result = await context.Journey.ConfirmUninstallAsync(CreateSnapshot(), UninstallScope.GameDirectory);
        Assert.Same(context.Executor.UninstallResult, result);
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task ConfirmUninstallAsync_WhenUninstallFails_ReportsTheFailureInsteadOfSilence()
    {
        var context = CreateContext();
        var notifications = context.SubscribeToasts();
        context.Executor.UninstallResult = new GameOperationResult { Success = false, Message = "game files are locked", ErrorCode = GameOperationErrorCode.System };
        var result = await context.Journey.ConfirmUninstallAsync(CreateSnapshot(), UninstallScope.GameDirectoryAndManagedCompatibility);
        Assert.False(result.Success);
        Assert.Equal("game files are locked", result.Message);
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task ConfirmUninstallAsync_WhenUninstallThrows_ReportsTheFailureInsteadOfSilence()
    {
        var context = CreateContext();
        context.Executor.UninstallException = new IOException("目录不是空的。");
        var result = await context.Journey.ConfirmUninstallAsync(CreateSnapshot(), UninstallScope.GameDirectoryAndManagedCompatibility);
        Assert.False(result.Success);
        Assert.Contains("目录不是空的", result.Message, StringComparison.Ordinal);
        Assert.Empty(context.ErrorHandling.Handled);
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

    [Fact]
    public async Task ConfirmUninstallAsync_WhenRefreshThrows_PreservesDeletionResultAndClearsBusy()
    {
        var context = CreateContext();
        context.Host.RefreshException = new IOException("refresh failed");
        context.Executor.UninstallResult = new GameOperationResult { Success = true, AffectedFileCount = 17, AffectedBytes = 2048 };
        var result = await context.Journey.ConfirmUninstallAsync(CreateSnapshot(), UninstallScope.GameDirectory);
        Assert.Same(context.Executor.UninstallResult, result);
        Assert.False(context.Host.IsBusy);
    }

    private static LauncherStatusSnapshot CreateSnapshot(
        LauncherRuntimeState runtimeState = LauncherRuntimeState.Ready,
        string afterLaunchBehavior = AfterLaunchBehaviors.Minimize,
        string gamePath = "")
    {
        return new LauncherStatusSnapshot
        {
            RuntimeState = runtimeState,
            LocalGame = new LocalInstallationState { GamePath = gamePath },
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
        var sessionMonitor = new FakeGameSessionMonitor();
        var journey = new GameOperationJourney(
            executor,
            new TestGameShortcutService(),
            sessionMonitor,
            localizer,
            toastService,
            new LocalDiagnostics(),
            errorHandling,
            _ => Task.CompletedTask,
            host);
        return new JourneyTestContext(journey, executor, host, errorHandling, toastService, localizer, sessionMonitor);
    }

    private sealed record JourneyTestContext(
        GameOperationJourney Journey,
        StubGameOperationExecutor Executor,
        RecordingJourneyHost Host,
        RecordingErrorHandlingService ErrorHandling,
        ToastService ToastService,
        LocalizationService Localizer,
        FakeGameSessionMonitor SessionMonitor)
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
        public Exception? RefreshException { get; set; }

        public bool MinimizeRequested { get; private set; }

        public bool ExitRequested { get; private set; }

        public bool ShowRequested { get; private set; }

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
            if (RefreshException is not null)
            {
                throw RefreshException;
            }
            return Task.FromResult(true);
        }

        public Task ShowLogViewerAsync() => Task.CompletedTask;

        public void RequestMinimize() => MinimizeRequested = true;

        public void RequestExit() => ExitRequested = true;

        public void RequestShow() => ShowRequested = true;
    }
}
