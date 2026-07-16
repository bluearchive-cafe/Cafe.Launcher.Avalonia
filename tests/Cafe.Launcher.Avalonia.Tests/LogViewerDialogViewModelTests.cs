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
        await viewModel.PendingFilterTask;

        Assert.Empty(viewModel.FilteredEntries);
        Assert.True(viewModel.IsEmpty);
    }

    [Fact]
    public async Task SeverityFilter_AfterInitialLoad_DoesNotReadLogFileAgain()
    {
        await logger.LogAsync(LogEntrySeverity.Info, "Launcher started");
        await logger.LogAsync(LogEntrySeverity.Error, "Launcher failed");
        logger.Dispose(); // release Serilog file handle before reading
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

        entriesLoaded.SetResult([]);
        await openTask;

        Assert.True(viewModel.IsVisible);
        Assert.False(viewModel.HasFilteredEntries);
    }

    [Fact]
    public async Task CloseCommand_HidesDialog()
    {
        await logger.LogAsync(LogEntrySeverity.Info, "Launcher started");
        var viewModel = CreateViewModel();
        await viewModel.OpenCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsVisible);

        viewModel.CloseCommand.Execute(null);

        Assert.False(viewModel.IsVisible);
    }

    [Fact]
    public async Task OpenCommand_WhenTextLogIsOpenForWriting_ShowsEntry()
    {
        await File.WriteAllLinesAsync(
            logger.LogFilePath,
            [
                "2026-06-22T00:27:57.4750472+08:00 [ERR] Launcher failed",
                "text failure"
            ]);
        using var activeWriter = new FileStream(
            logger.LogFilePath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite);
        var viewModel = CreateViewModel();

        await viewModel.OpenCommand.ExecuteAsync(null);

        var entry = Assert.Single(viewModel.FilteredEntries);
        Assert.Equal("Launcher failed", entry.Title);
        Assert.Equal("text failure", entry.Details);
        Assert.Equal(LogEntrySeverity.Error, entry.Severity);
    }

    [Fact]
    public async Task OpenCommand_WithInfoEntries_ShowsThemIncludingSessionBoundaries()
    {
        await File.WriteAllLinesAsync(
            logger.LogFilePath,
            [
                "2026-06-22T00:27:57.4750472+08:00 [INF] Session started",
                "Version: 1.0.0  CommitSha: abc1234",
                "2026-06-22T00:28:00.0000000+08:00 [INF] Wallpaper applied",
                "2026-06-22T00:29:00.0000000+08:00 [INF] Session ended"
            ]);
        var viewModel = CreateViewModel();

        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.SetFilterInfoCommand.Execute(null);

        Assert.Equal(3, viewModel.FilteredEntries.Count);
        Assert.All(viewModel.FilteredEntries, e => Assert.Equal(LogEntrySeverity.Info, e.Severity));
        Assert.Contains(viewModel.FilteredEntries, e => e.Title == "Session started");
        Assert.Contains(viewModel.FilteredEntries, e => e.Title == "Wallpaper applied");
        Assert.Contains(viewModel.FilteredEntries, e => e.Title == "Session ended");
    }

    [Fact]
    public async Task OpenCommand_WithSixHundredEntries_LoadsLatestFiveHundred()
    {
        await File.WriteAllLinesAsync(
            logger.LogFilePath,
            Enumerable.Range(0, 600).Select(index =>
                $"2026-06-22T00:27:57.4750472+08:00 [INF] Entry {index:D3}"));
        var viewModel = CreateViewModel();

        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.Equal(500, viewModel.FilteredEntries.Count);
        Assert.Equal("Entry 100", viewModel.FilteredEntries[0].Title);
        Assert.Equal("Entry 599", viewModel.FilteredEntries[^1].Title);
        Assert.True(viewModel.HasEarlierEntries);
    }

    [Fact]
    public async Task LoadEarlierCommand_AfterInitialPage_LoadsPreviousFiveHundred()
    {
        await File.WriteAllLinesAsync(
            logger.LogFilePath,
            Enumerable.Range(0, 600).Select(index =>
                $"2026-06-22T00:27:57.4750472+08:00 [INF] Entry {index:D3}"));
        var viewModel = CreateViewModel();
        await viewModel.OpenCommand.ExecuteAsync(null);

        await viewModel.LoadEarlierCommand.ExecuteAsync(null);

        Assert.Equal(600, viewModel.FilteredEntries.Count);
        Assert.Equal("Entry 000", viewModel.FilteredEntries[0].Title);
        Assert.False(viewModel.HasEarlierEntries);
    }

    [Fact]
    public async Task FilterText_WhenChangedAgainBeforeDebounce_CompletesLatestSearchOnly()
    {
        await File.WriteAllLinesAsync(
            logger.LogFilePath,
            [
                "2026-06-22T00:27:57.4750472+08:00 [INF] First entry",
                "2026-06-22T00:28:57.4750472+08:00 [INF] Second entry"
            ]);
        var viewModel = CreateViewModel();
        await viewModel.OpenCommand.ExecuteAsync(null);

        viewModel.FilterText = "First";
        viewModel.FilterText = "Second";
        await viewModel.PendingFilterTask;

        var entry = Assert.Single(viewModel.FilteredEntries);
        Assert.Equal("Second entry", entry.Title);
    }

    [Fact]
    public async Task OpenCommand_AfterLogFileGrows_IncludesNewEntry()
    {
        await File.WriteAllLinesAsync(
            logger.LogFilePath,
            ["2026-06-22T00:27:57.4750472+08:00 [INF] Existing entry"]);
        var viewModel = CreateViewModel();
        await viewModel.OpenCommand.ExecuteAsync(null);
        await File.AppendAllLinesAsync(
            logger.LogFilePath,
            ["2026-06-22T00:28:57.4750472+08:00 [INF] Appended entry"]);

        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.FilteredEntries.Count);
        Assert.Equal("Appended entry", viewModel.FilteredEntries[^1].Title);
    }

    [Fact]
    public void SetFilterAll_ResetsSeverityFilter()
    {
        var viewModel = CreateViewModel();
        viewModel.SetFilterErrorCommand.Execute(null);

        viewModel.SetFilterAllCommand.Execute(null);

        Assert.Null(viewModel.SeverityFilter);
        Assert.True(viewModel.IsFilterAllActive);
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
        logger.Dispose(); // release Serilog file handle before reading
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
