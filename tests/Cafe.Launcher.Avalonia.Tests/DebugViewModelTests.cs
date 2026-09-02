using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.Diagnostics;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class DebugViewModelTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    static DebugViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public void TogglePauseResume_WhenOperationBecomesPaused_ReportsPaused()
    {
        using var context = CreateContext();
        context.Operations.ApplyProgress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Download,
            Stage = GameOperationStage.Downloading,
            CanPause = true
        });

        context.ViewModel.TogglePauseResumeCommand.Execute(null);

        Assert.Equal("Download paused.", context.ViewModel.LastActionResult);
    }

    [Fact]
    public void TogglePauseResume_WhenLanguageIsJapanese_ReportsLocalizedResult()
    {
        using var context = CreateContext(LauncherLanguages.Japanese);
        context.Operations.ApplyProgress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Download,
            Stage = GameOperationStage.Downloading,
            CanPause = true
        });

        context.ViewModel.TogglePauseResumeCommand.Execute(null);

        Assert.Equal("ダウンロードを一時停止しました。", context.ViewModel.LastActionResult);
    }

    [Fact]
    public void Dispose_WhenOperationPauseStateChanges_DoesNotUpdateDebugState()
    {
        using var context = CreateContext();
        context.Operations.ApplyProgress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Download,
            Stage = GameOperationStage.Downloading,
            CanPause = true
        });
        context.ViewModel.Dispose();

        context.Operations.PauseResumeCommand.Execute(null);

        Assert.False(context.ViewModel.IsDownloadPaused);
    }

    [Fact]
    public async Task InstallationRunningChanged_WhenJourneyForwardsEvent_UpdatesDebugState()
    {
        using var context = CreateContext();
        await context.ViewModel.OpenCommand.ExecuteAsync(null);

        Assert.True(context.ViewModel.IsDownloadRunning);

        context.Backend.IsDownloadRunning = false;

        Assert.False(context.ViewModel.IsDownloadRunning);
        Assert.Equal(context.Localizer.T("debugIdle"), context.ViewModel.DownloadStatusText);
    }

    [Fact]
    public async Task TestActionToastCommand_RaisesPrimarySuccessAndSecondaryFailureActions()
    {
        using var context = CreateContext();
        ToastNotification? raised = null;
        context.ToastService.ToastRaised += toast => raised = toast;

        context.ViewModel.TestActionToastCommand.Execute(null);

        Assert.NotNull(raised);
        Assert.Equal(context.Localizer.T("debugActionToastTitle"), raised.Title);
        Assert.Equal(context.Localizer.T("debugSimulateSuccess"), raised.PrimaryAction!.Label);
        Assert.Equal(context.Localizer.T("debugSimulateFailure"), raised.SecondaryAction!.Label);
        Assert.True((await raised.PrimaryAction.ExecuteAsync(CancellationToken.None)).IsSuccess);
        var failure = await raised.SecondaryAction.ExecuteAsync(CancellationToken.None);
        Assert.False(failure.IsSuccess);
        Assert.Equal(context.Localizer.T("debugActionFailureTitle"), failure.Title);
        Assert.Equal(context.Localizer.T("debugActionFailureMessage"), failure.Message);
    }

    [Fact]
    public async Task ResetSettingsCommand_RequiresConfirmationBeforeResetting()
    {
        using var context = CreateContext();
        var confirmationCount = 0;
        var resetCount = 0;
        context.ViewModel.ResetSettingsConfirmationRequested += () => confirmationCount++;
        context.ViewModel.ResetSettingsRequested += () =>
        {
            resetCount++;
            return Task.CompletedTask;
        };

        context.ViewModel.ResetSettingsCommand.Execute(null);

        Assert.Equal(1, confirmationCount);
        Assert.Equal(0, resetCount);

        await context.ViewModel.ConfirmResetSettingsAsync();

        Assert.Equal(1, resetCount);
    }

    [Fact]
    public async Task RefreshStateCommand_AwaitsSubscribersStrictlyInRegistrationOrder()
    {
        using var context = CreateContext();
        var sequence = new List<string>();
        var firstSubscriberRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.ViewModel.RefreshRequested += async () =>
        {
            sequence.Add("first-start");
            await firstSubscriberRelease.Task;
            sequence.Add("first-end");
        };
        context.ViewModel.RefreshRequested += () =>
        {
            sequence.Add("second");
            return Task.CompletedTask;
        };

        var commandTask = context.ViewModel.RefreshStateCommand.ExecuteAsync(null);
        await Task.Yield();

        Assert.Equal(["first-start"], sequence);
        firstSubscriberRelease.SetResult();
        await commandTask;
        Assert.Equal(["first-start", "first-end", "second"], sequence);
    }

    [Fact]
    public async Task ConfirmResetSettingsAsync_AwaitsSubscribersStrictlyInRegistrationOrder()
    {
        using var context = CreateContext();
        var sequence = new List<string>();
        var firstSubscriberRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.ViewModel.ResetSettingsRequested += async () =>
        {
            sequence.Add("first-start");
            await firstSubscriberRelease.Task;
            sequence.Add("first-end");
        };
        context.ViewModel.ResetSettingsRequested += () =>
        {
            sequence.Add("second");
            return Task.CompletedTask;
        };

        var resetTask = context.ViewModel.ConfirmResetSettingsAsync();
        await Task.Yield();

        Assert.Equal(["first-start"], sequence);
        firstSubscriberRelease.SetResult();
        await resetTask;
        Assert.Equal(["first-start", "first-end", "second"], sequence);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private TestContext CreateContext(string language = LauncherLanguages.English)
    {
        Directory.CreateDirectory(tempDir);
        var localizer = new LocalizationService();
        localizer.SetLanguage(language);
        var toastService = new ToastService();
        var shell = new ShellViewModel(localizer);
        var diagnostics = new LocalDiagnostics();
        var dialogs = new DialogsViewModel(
            localizer,
            new NoticeStateService(Path.Combine(tempDir, "notices.json")),
            new SetupWizardViewModel(
                localizer,
                new GameInstallationPath(),
                new LocalInstallationStateStore(),
                diagnostics, new StubFilePickerService()));
        var backend = new TestBackend { IsDownloadRunning = true };
        var errorHandling = new ErrorHandlingService(localizer, diagnostics, toastService);
        var operations = new GameOperationsViewModel(
            backend,
            new TestGameShortcutService(),
            localizer,
            toastService,
            diagnostics,
            shell,
            dialogs,
            errorHandling,
            _ => Task.CompletedTask);
        var logger = new UnifiedLogger(Path.Combine(tempDir, "logs"));
        var viewModel = new DebugViewModel(
            toastService,
            logger,
            errorHandling,
            new LauncherSettingsService(tempDir),
            operations,
            shell,
            new StubFilePickerService());
        return new TestContext(viewModel, operations, backend, logger, toastService, localizer);
    }

    private sealed record TestContext(
        DebugViewModel ViewModel,
        GameOperationsViewModel Operations,
        TestBackend Backend,
        UnifiedLogger Logger,
        ToastService ToastService,
        LocalizationService Localizer) : IDisposable
    {
        public void Dispose()
        {
            Logger.Dispose();
        }
    }

    private sealed class TestBackend : IGameOperationExecutor
    {
        private bool isRunning;
        public bool IsDownloadRunning
        {
            get => isRunning;
            set
            {
                if (isRunning == value)
                {
                    return;
                }

                isRunning = value;
                IsRunningChanged?.Invoke();
            }
        }
        public bool IsPaused { get; private set; }
        public event Action? IsRunningChanged;

        public Task<GameLaunchResult> LaunchAsync(
            LauncherStatusSnapshot snapshot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GameLaunchResult { Validation = new ManifestValidationResult() });

        public Task<GameOperationResult> InstallOrUpdateAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GameOperationResult());

        public Task<GameOperationResult> RepairAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress) =>
            Task.FromResult(new GameOperationResult());

        public Task<GameOperationResult?> ResumePersistedAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress,
            CancellationToken cancellationToken) =>
            Task.FromResult<GameOperationResult?>(null);

        public void Stop(bool clearPersistedState)
        {
            IsDownloadRunning = false;
        }

        public void Pause()
        {
            IsPaused = true;
        }

        public void Resume()
        {
            IsPaused = false;
        }

        public Task<GameOperationResult> ValidateUninstallAsync(string gamePath) =>
            Task.FromResult(new GameOperationResult());

        public Task<GameOperationResult> UninstallAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress) =>
            Task.FromResult(new GameOperationResult());
    }
}
