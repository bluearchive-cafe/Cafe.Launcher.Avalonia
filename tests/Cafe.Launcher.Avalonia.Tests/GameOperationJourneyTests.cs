using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
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
    public async Task StartGameAsync_WhenExecutorThrows_HandlesErrorAndClearsBusy()
    {
        var context = CreateContext();
        context.Executor.LaunchException = new InvalidOperationException("boom");

        await context.Journey.StartGameAsync(CreateSnapshot());

        Assert.Single(context.ErrorHandling.Handled);
        Assert.False(context.Host.IsBusy);
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
            && toast.PrimaryAction is null);
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
    public async Task ConfirmUninstallAsync_WhenNotReady_SkipsUninstall()
    {
        var context = CreateContext();

        await context.Journey.ConfirmUninstallAsync(CreateSnapshot(LauncherRuntimeState.NotInstalled));

        Assert.Equal(0, context.Executor.UninstallCallCount);
        Assert.Equal(0, context.Host.SetBusyCallCount);
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
        Assert.True(context.Executor.LastStopClearPersistedState);
        Assert.Contains(notifications, toast => toast.Severity == ToastSeverity.Warning);
    }

    private static LauncherStatusSnapshot CreateSnapshot(
        LauncherRuntimeState runtimeState = LauncherRuntimeState.Ready)
    {
        return new LauncherStatusSnapshot
        {
            RuntimeState = runtimeState,
            LocalGame = new LocalInstallationState(),
            Remote = new LauncherRemoteState()
        };
    }

    private static JourneyTestContext CreateContext()
    {
        var executor = new RecordingOperationExecutor();
        var host = new RecordingJourneyHost();
        var errorHandling = new RecordingErrorHandlingService();
        var toastService = new ToastService();
        var journey = new GameOperationJourney(
            executor,
            new TestGameShortcutService(),
            new LocalizationService(),
            toastService,
            new LocalDiagnostics(),
            errorHandling,
            _ => Task.CompletedTask,
            host);
        return new JourneyTestContext(journey, executor, host, errorHandling, toastService);
    }

    private sealed record JourneyTestContext(
        GameOperationJourney Journey,
        RecordingOperationExecutor Executor,
        RecordingJourneyHost Host,
        RecordingErrorHandlingService ErrorHandling,
        ToastService ToastService)
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
    }

    private sealed class RecordingOperationExecutor : IGameOperationExecutor
    {
        public bool IsDownloadRunning { get; set; }

        public bool IsPaused { get; set; }

#pragma warning disable CS0067
        public event Action? IsRunningChanged;
#pragma warning restore CS0067

        public GameLaunchResult LaunchResult { get; set; } = new() { Success = true, Message = "launched" };

        public Exception? LaunchException { get; set; }

        public int LaunchCallCount { get; private set; }

        public GameOperationResult InstallResult { get; set; } = new() { Success = true, Message = "done" };

        public int InstallCallCount { get; private set; }

        public GameOperationResult RepairResult { get; set; } = new() { Success = true, Message = "repaired" };

        public int RepairCallCount { get; private set; }

        public GameOperationResult UninstallResult { get; set; } = new() { Success = true, Message = "uninstalled" };

        public int UninstallCallCount { get; private set; }

        public GameOperationResult ValidateUninstallResult { get; set; } = new() { Success = true, Message = "ok" };

        public GameOperationResult? ResumeResult { get; set; }

        public int ResumeCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public bool? LastStopClearPersistedState { get; private set; }

        public Task<GameLaunchResult> LaunchAsync(
            LauncherStatusSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            LaunchCallCount++;
            if (LaunchException is not null)
            {
                throw LaunchException;
            }

            return Task.FromResult(LaunchResult);
        }

        public Task<GameOperationResult> InstallOrUpdateAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress,
            CancellationToken cancellationToken = default)
        {
            InstallCallCount++;
            return Task.FromResult(InstallResult);
        }

        public Task<GameOperationResult> RepairAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress)
        {
            RepairCallCount++;
            return Task.FromResult(RepairResult);
        }

        public Task<GameOperationResult?> ResumePersistedAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress,
            CancellationToken cancellationToken)
        {
            ResumeCallCount++;
            return Task.FromResult(ResumeResult);
        }

        public Task<GameOperationResult> ValidateUninstallAsync(string gamePath) =>
            Task.FromResult(ValidateUninstallResult);

        public Task<GameOperationResult> UninstallAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress)
        {
            UninstallCallCount++;
            return Task.FromResult(UninstallResult);
        }

        public void Stop(bool clearPersistedState)
        {
            StopCallCount++;
            LastStopClearPersistedState = clearPersistedState;
        }

        public void Pause()
        {
        }

        public void Resume()
        {
        }
    }

    private sealed class RecordingErrorHandlingService : IErrorHandlingService
    {
        public List<(string Context, Exception Exception)> Handled { get; } = [];

        public Task HandleErrorAsync(string context, Exception exception, ErrorHandlingOptions? options = null)
        {
            Handled.Add((context, exception));
            return Task.CompletedTask;
        }

        public Task HandleCriticalErrorAsync(string context, Exception exception) => Task.CompletedTask;

        public event Action<CriticalErrorInfo>? CriticalErrorRequested
        {
            add { }
            remove { }
        }
    }
}
