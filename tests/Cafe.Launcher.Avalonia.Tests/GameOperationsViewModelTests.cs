using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class GameOperationsViewModelTests
{
    static GameOperationsViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public void LegacyDelegateProperties_AreRemoved()
    {
        var propertyNames = typeof(GameOperationsViewModel)
            .GetProperties(System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic)
            .Select(property => property.Name);

        Assert.DoesNotContain("GetSnapshot", propertyNames);
        Assert.DoesNotContain("RequestRefreshAsync", propertyNames);
        Assert.DoesNotContain("RequestRefreshAfterPersistedResumeAsync", propertyNames);
        Assert.DoesNotContain("ApplySnapshotAsync", propertyNames);
        Assert.DoesNotContain("MinimizeWindow", propertyNames);
    }

    [Fact]
    public async Task RefreshRequested_AwaitsSubscribersStrictlyInRegistrationOrder()
    {
        var context = CreateContext();
        var sequence = new List<string>();
        var firstSubscriberRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.RemoteUnavailable
        });
        context.ViewModel.RefreshRequested += async _ =>
        {
            sequence.Add("first-start");
            await firstSubscriberRelease.Task;
            sequence.Add("first-end");
        };
        context.ViewModel.RefreshRequested += _ =>
        {
            sequence.Add("second");
            return Task.CompletedTask;
        };

        var commandTask = context.ViewModel.InstallOrUpdateCommand.ExecuteAsync(null);
        await Task.Yield();

        Assert.Equal(["first-start"], sequence);
        firstSubscriberRelease.SetResult();
        await commandTask;
        Assert.Equal(["first-start", "first-end", "second"], sequence);
    }

    [Theory]
    [InlineData(LauncherRuntimeState.NotInstalled, true, false)]
    [InlineData(LauncherRuntimeState.Ready, false, true)]
    [InlineData(LauncherRuntimeState.Corrupted, true, false)]
    [InlineData(LauncherRuntimeState.RemoteUnavailable, true, false)]
    public void ApplySnapshot_MapsIdlePanelVisibility(
        LauncherRuntimeState state,
        bool installVisible,
        bool controlVisible)
    {
        var context = CreateContext();

        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = state });

        Assert.Equal(installVisible, context.ViewModel.IsInstallPanelVisible);
        Assert.Equal(controlVisible, context.ViewModel.IsControlPanelVisible);
        Assert.False(context.ViewModel.IsProgressPanelVisible);
    }

    [Fact]
    public async Task StartGameCommand_WhenLaunchSucceeds_MinimizesWindowAndShowsSuccess()
    {
        var context = CreateContext();
        var minimized = false;
        var notifications = new List<ToastNotification>();
        context.ToastService.ToastRaised += notifications.Add;
        context.Backend.LaunchResult = new GameLaunchResult
        {
            Success = true,
            Message = "started",
            Validation = new ManifestValidationResult
            {
                Success = true,
                Message = "validated"
            }
        };
        context.ViewModel.ApplySnapshot(ReadySnapshot());
        context.ViewModel.MinimizeRequested += () => minimized = true;

        await context.ViewModel.StartGameCommand.ExecuteAsync(null);

        Assert.True(minimized);
        Assert.False(context.Shell.IsBusy);
        Assert.Equal("validated", context.Shell.LaunchCheckValueText);
        Assert.Contains(notifications, item => item.Severity == ToastSeverity.Success);
    }

    [Fact]
    public async Task InstallOrUpdateCommand_WhenInstallationStateIsCorrupted_ShowsRepairDialog()
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.Corrupted
        });

        await context.ViewModel.InstallOrUpdateCommand.ExecuteAsync(null);

        Assert.True(context.Dialogs.IsRepairConfirmVisible);
        Assert.Equal(0, context.Backend.InstallInvocationCount);
        Assert.False(context.Shell.IsBusy);
    }

    [Fact]
    public async Task InstallOrUpdateCommand_WhenRemoteStateIsUnavailable_RefreshesWithoutDownloading()
    {
        var context = CreateContext();
        GameOperationsRefreshMode? refreshMode = null;
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.RemoteUnavailable
        });
        context.ViewModel.RefreshRequested += mode =>
        {
            refreshMode = mode;
            return Task.CompletedTask;
        };

        await context.ViewModel.InstallOrUpdateCommand.ExecuteAsync(null);

        Assert.Equal(GameOperationsRefreshMode.Normal, refreshMode);
        Assert.Equal(0, context.Backend.InstallInvocationCount);
    }

    [Fact]
    public async Task InstallOrUpdateCommand_WhenDownloadCompletes_RefreshesAndAppliesSnapshot()
    {
        var context = CreateContext();
        var snapshot = new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.NotInstalled
        };
        GameOperationsRefreshMode? refreshMode = null;
        context.ViewModel.ApplySnapshot(snapshot);
        context.Backend.InstallResult = new GameOperationResult
        {
            Success = true,
            Message = "installed"
        };
        context.ViewModel.RefreshRequested += mode =>
        {
            refreshMode = mode;
            return Task.CompletedTask;
        };

        await context.ViewModel.InstallOrUpdateCommand.ExecuteAsync(null);

        Assert.Equal(1, context.Backend.InstallInvocationCount);
        Assert.Equal(GameOperationsRefreshMode.SkipPersistedResume, refreshMode);
        Assert.False(context.ViewModel.IsProgressPanelVisible);
        Assert.Equal("installed", context.Shell.OperationNote);
        Assert.False(context.Shell.IsBusy);
    }

    [Fact]
    public async Task RequestRepairCommand_WhenStateAllowsRepair_ShowsConfirmation()
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(ReadySnapshot());

        await context.ViewModel.RequestRepairCommand.ExecuteAsync(null);

        Assert.True(context.Dialogs.IsRepairConfirmVisible);
    }

    [Fact]
    public async Task RepairAsync_WhenRepairCompletes_RefreshesState()
    {
        var context = CreateContext();
        var refreshCount = 0;
        context.ViewModel.ApplySnapshot(ReadySnapshot());
        context.Backend.RepairResult = new GameOperationResult
        {
            Success = true,
            Message = "repaired"
        };
        context.ViewModel.RefreshRequested += _ =>
        {
            refreshCount++;
            return Task.CompletedTask;
        };

        await context.ViewModel.RepairAsync();

        Assert.Equal(1, context.Backend.RepairInvocationCount);
        Assert.Equal(1, refreshCount);
        Assert.Equal("repaired", context.Shell.OperationNote);
    }

    [Fact]
    public void PauseResumeCommand_TogglesBackendAndPresentation()
    {
        var context = CreateContext();
        context.ViewModel.ApplyProgress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Download,
            Stage = GameOperationStage.Downloading,
            CanPause = true
        });

        context.ViewModel.PauseResumeCommand.Execute(null);

        Assert.True(context.Backend.IsPaused);
        Assert.True(context.ViewModel.IsPaused);
        Assert.Equal("Play", context.ViewModel.PauseResumeIcon);

        context.ViewModel.PauseResumeCommand.Execute(null);

        Assert.False(context.Backend.IsPaused);
        Assert.False(context.ViewModel.IsPaused);
        Assert.Equal("Pause", context.ViewModel.PauseResumeIcon);
    }

    [Fact]
    public void StopOperationCommand_WhenDownloadIsRunning_ShowsConfirmation()
    {
        var context = CreateContext();
        context.Backend.IsDownloadRunning = true;

        context.ViewModel.StopOperationCommand.Execute(null);

        Assert.True(context.Dialogs.IsStopConfirmVisible);
        Assert.Equal(0, context.Backend.StopInvocationCount);
    }

    [Fact]
    public async Task RequestUninstallCommand_WhenValidationSucceeds_ShowsConfirmation()
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(ReadySnapshot("C:\\Game"));
        context.Backend.ValidateUninstallResult = new GameOperationResult
        {
            Success = true,
            AffectedFileCount = 5
        };

        await context.ViewModel.RequestUninstallCommand.ExecuteAsync(null);

        Assert.True(context.Dialogs.IsUninstallConfirmVisible);
        Assert.Contains("C:\\Game", context.Dialogs.UninstallConfirmText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmUninstallAsync_WhenUninstallCompletes_RefreshesState()
    {
        var context = CreateContext();
        var refreshCount = 0;
        context.ViewModel.ApplySnapshot(ReadySnapshot("C:\\Game"));
        context.Backend.UninstallResult = new GameOperationResult
        {
            Success = true,
            Message = "uninstalled"
        };
        context.ViewModel.RefreshRequested += _ =>
        {
            refreshCount++;
            return Task.CompletedTask;
        };

        await context.ViewModel.ConfirmUninstallAsync();

        Assert.Equal(1, context.Backend.UninstallInvocationCount);
        Assert.Equal(1, refreshCount);
        Assert.False(context.Shell.IsBusy);
    }

    [Fact]
    public async Task ResumePersistedDownloadAsync_WhenResultExists_RefreshesAndResetsPauseCapability()
    {
        var context = CreateContext();
        var refreshCount = 0;
        context.ViewModel.ApplySnapshot(ReadySnapshot());
        context.Backend.ResumeResult = new GameOperationResult
        {
            Success = false,
            ErrorCode = GameOperationErrorCode.Stopped,
            Message = "stopped"
        };
        context.ViewModel.RefreshRequested += _ =>
        {
            refreshCount++;
            return Task.CompletedTask;
        };

        await context.ViewModel.ResumePersistedDownloadAsync(CancellationToken.None);

        Assert.Equal(1, context.Backend.ResumeInvocationCount);
        Assert.Equal(1, refreshCount);
        Assert.False(context.ViewModel.CanPauseOperation);
        Assert.False(context.Shell.IsBusy);
    }

    [Fact]
    public void ApplyProgress_MapsRepairConfirmationAndDownloadDetails()
    {
        var context = CreateContext();

        context.ViewModel.ApplyProgress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Repair,
            Stage = GameOperationStage.RepairConfirmation,
            Progress = -1,
            AffectedFileCount = 2,
            DownloadedSize = 1024
        });

        Assert.Equal(0, context.ViewModel.ProgressValue);
        Assert.Empty(context.ViewModel.ProgressSpeed);
        Assert.Contains("2", context.ViewModel.ProgressDetail, StringComparison.Ordinal);

        context.ViewModel.ApplyProgress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Download,
            Stage = GameOperationStage.Downloading,
            Progress = 50,
            DownloadedSize = 1024,
            TotalSize = 2048,
            BytesPerSecond = 1024 * 1024,
            EstimatedRemaining = TimeSpan.FromSeconds(1),
            CanPause = true
        });

        Assert.Equal(50, context.ViewModel.ProgressValue);
        Assert.Equal("1MB/S", context.ViewModel.ProgressSpeed);
        Assert.NotEmpty(context.ViewModel.ProgressSize);
        Assert.NotEmpty(context.ViewModel.ProgressEstimated);
        Assert.True(context.ViewModel.CanPauseOperation);
    }

    [Theory]
    [InlineData(GameOperationStage.DiskCheck, 10L, 20L, 0, 0, 0, "10B", "20B")]
    [InlineData(GameOperationStage.VerificationRetry, 0, null, 2, 1, 3, "2", "1/3")]
    [InlineData(GameOperationStage.VerificationFailed, 0, null, 2, 0, 0, "2", null)]
    public void ApplyProgress_MapsPreflightAndVerificationStagesAndClearsDownloadMetrics(
        GameOperationStage stage,
        long requiredBytes,
        long? availableBytes,
        int failedFileCount,
        int retryAttempt,
        int retryLimit,
        string expectedText,
        string? secondExpectedText)
    {
        var context = CreateContext();

        context.ViewModel.ApplyProgress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Download,
            Stage = stage,
            RequiredDiskBytes = requiredBytes,
            AvailableDiskBytes = availableBytes,
            FailedFileCount = failedFileCount,
            RetryAttempt = retryAttempt,
            RetryLimit = retryLimit,
            BytesPerSecond = 1,
            DownloadedSize = 10,
            TotalSize = 20,
            EstimatedRemaining = TimeSpan.FromSeconds(1)
        });

        Assert.Contains(expectedText, context.ViewModel.ProgressDetail, StringComparison.Ordinal);
        if (secondExpectedText is not null)
        {
            Assert.Contains(secondExpectedText, context.ViewModel.ProgressDetail, StringComparison.Ordinal);
        }
        Assert.Empty(context.ViewModel.ProgressSpeed);
        Assert.Empty(context.ViewModel.ProgressSize);
        Assert.Empty(context.ViewModel.ProgressEstimated);
    }

    [Theory]
    [MemberData(nameof(AllOperationStages))]
    public void ApplyProgress_ForEveryStage_ProducesLocalizedPresentation(
        GameOperationStage stage)
    {
        var context = CreateContext();

        context.ViewModel.ApplyProgress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Download,
            Stage = stage,
        });

        Assert.NotEmpty(context.ViewModel.ProgressTitle);
        Assert.NotEmpty(context.ViewModel.ProgressDetail);
    }

    public static TheoryData<GameOperationStage> AllOperationStages =>
        new(Enum.GetValues<GameOperationStage>());

    [Fact]
    public async Task StartGameCommand_WhenBusyOrStateMissing_DoesNotStartGame()
    {
        var context = CreateContext();
        context.Shell.IsBusy = true;
        context.ViewModel.ApplySnapshot(ReadySnapshot());

        await context.ViewModel.StartGameCommand.ExecuteAsync(null);

        Assert.Equal(0, context.Backend.LaunchInvocationCount);

        var missingStateContext = CreateContext();
        await missingStateContext.ViewModel.StartGameCommand.ExecuteAsync(null);

        Assert.Equal(0, missingStateContext.Backend.LaunchInvocationCount);
    }

    [Fact]
    public async Task StartGameCommand_WhenLaunchIsBlocked_ShowsWarning()
    {
        var context = CreateContext();
        var notifications = new List<ToastNotification>();
        context.ToastService.ToastRaised += notifications.Add;
        context.ViewModel.ApplySnapshot(ReadySnapshot());
        context.Backend.LaunchResult = new GameLaunchResult
        {
            Success = false,
            Message = "blocked",
            Validation = new ManifestValidationResult
            {
                Success = false,
                Message = "invalid"
            }
        };

        await context.ViewModel.StartGameCommand.ExecuteAsync(null);

        Assert.Equal("blocked", context.Shell.OperationNote);
        Assert.Contains(notifications, item => item.Severity == ToastSeverity.Warning);
    }

    [Fact]
    public async Task InstallOrUpdateCommand_WhenStateIsReady_ReturnsUnavailable()
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(ReadySnapshot());

        await context.ViewModel.InstallOrUpdateCommand.ExecuteAsync(null);

        Assert.Equal(0, context.Backend.InstallInvocationCount);
        Assert.NotEmpty(context.Shell.OperationNote);
    }

    [Fact]
    public async Task RequestRepairAndRepair_WhenStateIsInvalid_DoNotCallBackend()
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.NotInstalled
        });

        await context.ViewModel.RequestRepairCommand.ExecuteAsync(null);
        await context.ViewModel.RepairAsync();

        Assert.False(context.Dialogs.IsRepairConfirmVisible);
        Assert.Equal(0, context.Backend.RepairInvocationCount);
    }

    [Fact]
    public async Task RequestRepairCommand_WhenNotInstalled_ShowsWarningToast()
    {
        var context = CreateContext();
        var notifications = new List<ToastNotification>();
        context.ToastService.ToastRaised += notifications.Add;
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.NotInstalled
        });

        await context.ViewModel.RequestRepairCommand.ExecuteAsync(null);

        var notification = Assert.Single(notifications);
        Assert.Equal(ToastSeverity.Warning, notification.Severity);
        Assert.Equal(context.Shell.OperationNote, notification.Message);
        Assert.False(context.Dialogs.IsRepairConfirmVisible);
    }

    [Fact]
    public async Task RequestUninstallCommand_WhenNotInstalled_ShowsWarningToast()
    {
        var context = CreateContext();
        var notifications = new List<ToastNotification>();
        context.ToastService.ToastRaised += notifications.Add;
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.NotInstalled
        });

        await context.ViewModel.RequestUninstallCommand.ExecuteAsync(null);

        var notification = Assert.Single(notifications);
        Assert.Equal(ToastSeverity.Warning, notification.Severity);
        Assert.Equal(context.Shell.OperationNote, notification.Message);
        Assert.False(context.Dialogs.IsUninstallConfirmVisible);
    }

    [Fact]
    public void StopOperationCommand_WhenNoDownloadIsRunning_StopsImmediately()
    {
        var context = CreateContext();

        context.ViewModel.StopOperationCommand.Execute(null);

        Assert.Equal(1, context.Backend.StopInvocationCount);
        Assert.False(context.Dialogs.IsStopConfirmVisible);
    }

    [Fact]
    public void PauseResumeCommand_WhenProgressCannotPause_DoesNothing()
    {
        var context = CreateContext();

        context.ViewModel.PauseResumeCommand.Execute(null);

        Assert.False(context.Backend.IsPaused);
    }

    [Fact]
    public async Task RequestUninstallCommand_WhenValidationFails_DoesNotShowConfirmation()
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(ReadySnapshot("C:\\Game"));
        context.Backend.ValidateUninstallResult = new GameOperationResult
        {
            Success = false,
            Message = "cannot uninstall"
        };

        await context.ViewModel.RequestUninstallCommand.ExecuteAsync(null);

        Assert.False(context.Dialogs.IsUninstallConfirmVisible);
        Assert.Equal("cannot uninstall", context.Shell.OperationNote);
    }

    [Fact]
    public async Task ResumePersistedDownloadAsync_WhenNoResult_DoesNotRefresh()
    {
        var context = CreateContext();
        var refreshCount = 0;
        context.ViewModel.ApplySnapshot(ReadySnapshot());
        context.Backend.ResumeResult = null;
        context.ViewModel.RefreshRequested += _ =>
        {
            refreshCount++;
            return Task.CompletedTask;
        };

        await context.ViewModel.ResumePersistedDownloadAsync(CancellationToken.None);

        Assert.Equal(0, refreshCount);
        Assert.False(context.Shell.IsBusy);
    }

    [Fact]
    public async Task BackendExceptions_AreConvertedToOperationNotes()
    {
        var context = CreateContext();
        context.ViewModel.ApplySnapshot(new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.NotInstalled
        });
        context.Backend.InstallException = new InvalidOperationException("install failed");

        await context.ViewModel.InstallOrUpdateCommand.ExecuteAsync(null);

        Assert.Contains("install failed", context.Shell.OperationNote, StringComparison.Ordinal);
        Assert.False(context.Shell.IsBusy);
    }

    private static TestContext CreateContext()
    {
        var localizer = new LocalizationService();
        var toastService = new ToastService();
        var shell = new ShellViewModel(localizer);
        shell.IsBusy = false;
        var dialogs = new DialogsViewModel(localizer, new NoticeStateService(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "notices.json")),
            new SetupWizardViewModel(localizer, new GameInstallationPath()));
        var backend = new TestBackend();
        var viewModel = new GameOperationsViewModel(
            backend,
            backend,
            backend,
            localizer,
            toastService,
            new LocalDiagnostics(),
            shell,
            dialogs,
            _ => Task.CompletedTask);
        return new TestContext(viewModel, backend, shell, dialogs, toastService);
    }

    private static LauncherStatusSnapshot ReadySnapshot(string gamePath = "") =>
        new()
        {
            RuntimeState = LauncherRuntimeState.Ready,
            LocalGame = new LocalInstallationState { GamePath = gamePath }
        };

    private sealed record TestContext(
        GameOperationsViewModel ViewModel,
        TestBackend Backend,
        ShellViewModel Shell,
        DialogsViewModel Dialogs,
        ToastService ToastService);

    private sealed class TestBackend :
        IGameLaunchWorkflow,
        IGameInstallationWorkflow,
        IGameUninstallWorkflow
    {
        public bool IsDownloadRunning { get; set; }
        public bool IsRunning => IsDownloadRunning;
        public bool IsPaused { get; set; }
        public int InstallInvocationCount { get; private set; }
        public int LaunchInvocationCount { get; private set; }
        public int RepairInvocationCount { get; private set; }
        public int UninstallInvocationCount { get; private set; }
        public int ResumeInvocationCount { get; private set; }
        public int StopInvocationCount { get; private set; }
        public GameLaunchResult LaunchResult { get; set; } = new()
        {
            Validation = new ManifestValidationResult()
        };
        public GameOperationResult InstallResult { get; set; } = new();
        public GameOperationResult RepairResult { get; set; } = new();
        public GameOperationResult ValidateUninstallResult { get; set; } = new();
        public GameOperationResult UninstallResult { get; set; } = new();
        public GameOperationResult? ResumeResult { get; set; }
        public Exception? InstallException { get; set; }

        public Task<GameLaunchResult> StartGameAsync(LauncherStatusSnapshot snapshot)
        {
            LaunchInvocationCount++;
            return Task.FromResult(LaunchResult);
        }

        public Task<GameOperationResult> InstallOrUpdateAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress)
        {
            InstallInvocationCount++;
            if (InstallException is not null)
            {
                throw InstallException;
            }

            return Task.FromResult(InstallResult);
        }

        public Task<GameOperationResult> RepairAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress)
        {
            RepairInvocationCount++;
            return Task.FromResult(RepairResult);
        }

        public Task<GameOperationResult> ValidateUninstallAsync(string gamePath) =>
            Task.FromResult(ValidateUninstallResult);

        public Task<GameOperationResult> UninstallAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress)
        {
            UninstallInvocationCount++;
            return Task.FromResult(UninstallResult);
        }

        public Task<GameOperationResult?> ResumePersistedAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress,
            CancellationToken cancellationToken)
        {
            ResumeInvocationCount++;
            return Task.FromResult(ResumeResult);
        }

        public void Stop(bool clearPersistedState)
        {
            StopInvocationCount++;
            IsDownloadRunning = false;
        }

        public void Pause() => IsPaused = true;

        public void Resume() => IsPaused = false;
    }
}
