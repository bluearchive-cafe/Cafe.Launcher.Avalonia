using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LogViewerDialogViewModelTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly UnifiedLogger logger;

    static LogViewerDialogViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    public LogViewerDialogViewModelTests()
    {
        Directory.CreateDirectory(tempDir);
        logger = new UnifiedLogger(tempDir);
    }

    [Fact]
    public async Task FilterText_WhenNoEntryMatches_ExposesEmptyState()
    {
        await logger.LogAsync(LogEntrySeverity.Info, "Launcher started");
        var viewModel = CreateViewModel();
        viewModel.LoadEntries();

        viewModel.FilterText = "text-that-does-not-exist";

        Assert.Empty(viewModel.FilteredEntries);
        Assert.True(viewModel.IsEmpty);
    }

    [Fact]
    public async Task SeverityFilter_AfterInitialLoad_DoesNotReadLogFileAgain()
    {
        await logger.LogAsync(LogEntrySeverity.Info, "Launcher started");
        await logger.LogAsync(LogEntrySeverity.Error, "Launcher failed");
        var viewModel = CreateViewModel();
        viewModel.LoadEntries();
        File.Delete(logger.LogFilePath);

        viewModel.SetFilterErrorCommand.Execute(null);

        var entry = Assert.Single(viewModel.FilteredEntries);
        Assert.Equal(LogEntrySeverity.Error, entry.Severity);
    }

    [Fact]
    public async Task OpenCommand_ShowsDialogBeforeEntryLoadingCompletes()
    {
        var entriesLoaded = new TaskCompletionSource<IReadOnlyList<LogEntryDisplay>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new LogViewerDialogViewModel(
            logger,
            null,
            null,
            null,
            null,
            _ => entriesLoaded.Task);

        var openTask = viewModel.OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsVisible);
        Assert.False(openTask.IsCompleted);

        entriesLoaded.SetResult([]);
        await openTask;
    }

    [Fact]
    public async Task ExportCommand_WhenDirectorySelected_ShowsPathAndOpensDirectory()
    {
        await logger.LogAsync(LogEntrySeverity.Info, "Export test");
        var toastService = new ToastService();
        ToastNotification? toast = null;
        toastService.ToastRaised += notification => toast = notification;
        var localizer = new LocalizationService();
        var viewModel = new LogViewerDialogViewModel(
            logger,
            new LogExportService(logger),
            toastService,
            localizer,
            new LocalDiagnostics(logger));
        var exportDirectory = Path.Combine(tempDir, "selected");
        string? openedDirectory = null;
        viewModel.PickExportDirectoryAsync = _ => Task.FromResult<string?>(exportDirectory);
        viewModel.OpenExportDirectory = path => openedDirectory = path;

        await viewModel.ExportCommand.ExecuteAsync(null);

        var zipPath = Assert.Single(Directory.GetFiles(exportDirectory, "*.zip"));
        Assert.Equal(exportDirectory, openedDirectory);
        Assert.NotNull(toast);
        Assert.Equal(ToastSeverity.Success, toast.Severity);
        Assert.Contains(zipPath, toast.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportCommand_WhenPickerIsCancelled_DoesNotExportOrShowToast()
    {
        var toastService = new ToastService();
        ToastNotification? toast = null;
        toastService.ToastRaised += notification => toast = notification;
        var viewModel = new LogViewerDialogViewModel(
            logger,
            new LogExportService(logger),
            toastService,
            new LocalizationService(),
            new LocalDiagnostics(logger));
        viewModel.PickExportDirectoryAsync = _ => Task.FromResult<string?>(null);

        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.Null(toast);
    }

    [Fact]
    public async Task ExportCommand_WhenExportFails_ShowsErrorAndWritesDiagnostic()
    {
        var toastService = new ToastService();
        ToastNotification? toast = null;
        toastService.ToastRaised += notification => toast = notification;
        var diagnostics = new LocalDiagnostics(logger);
        var viewModel = new LogViewerDialogViewModel(
            logger,
            new LogExportService(logger),
            toastService,
            new LocalizationService(),
            diagnostics);
        viewModel.PickExportDirectoryAsync = _ => Task.FromResult<string?>("\0");

        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.NotNull(toast);
        Assert.Equal(ToastSeverity.Error, toast.Severity);
        Assert.Contains("Log export failed.", File.ReadAllText(logger.LogFilePath), StringComparison.Ordinal);
    }

    private LogViewerDialogViewModel CreateViewModel() =>
        new(logger, null, null, null, null, null);

    public void Dispose()
    {
        logger.Dispose();
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
