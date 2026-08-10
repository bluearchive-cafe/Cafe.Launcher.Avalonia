using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.Diagnostics;`nusing Cafe.Launcher.Avalonia.Models;
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
        shell.I18n.Apply(localizer);
        var diagnostics = new LocalDiagnostics();
        var dialogs = new DialogsViewModel(
            localizer,
            new NoticeStateService(Path.Combine(tempDir, "notices.json")),
            new SetupWizardViewModel(
                localizer,
                new GameInstallationPath(),
                new LocalInstallationStateStore(),
                diagnostics));
        var backend = new TestBackend { IsRunning = true };
        var errorHandling = new ErrorHandlingService(localizer, diagnostics, toastService);
        var operations = new GameOperationsViewModel(
            backend,
            backend,
            backend,
            localizer,
            toastService,
            diagnostics,
            shell,
            dialogs,
            _ => Task.CompletedTask,
            errorHandling);
        var logger = new UnifiedLogger(Path.Combine(tempDir, "logs"));
        var viewModel = new DebugViewModel(
            toastService,
            logger,
            errorHandling,
            new LauncherSettingsService(tempDir),
            operations,
            shell);
        return new TestContext(viewModel, operations, logger, toastService, localizer);
    }

    private sealed record TestContext(
        DebugViewModel ViewModel,
        GameOperationsViewModel Operations,
        UnifiedLogger Logger,
        ToastService ToastService,
        LocalizationService Localizer) : IDisposable
    {
        public void Dispose()
        {
            Logger.Dispose();
        }
    }

    private sealed class TestBackend :
        IGameLaunchWorkflow,
        IGameInstallationWorkflow,
        IGameUninstallWorkflow
    {
        public bool IsRunning { get; set; }
        public bool IsPaused { get; private set; }
        public event Action? IsRunningChanged { add { } remove { } }

        public Task<GameLaunchResult> StartGameAsync(LauncherStatusSnapshot snapshot) =>
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
            IsRunning = false;
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
