using System.IO.Compression;
using System.Linq;
using System.Text;
using Cafe.Launcher.Avalonia.Features.Diagnostics;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class LogExportDialogViewModelTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly UnifiedLogger logger;

    static LogExportDialogViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    public LogExportDialogViewModelTests()
    {
        Directory.CreateDirectory(tempDir);
        var logDirectory = Path.Combine(tempDir, "logs");
        Directory.CreateDirectory(logDirectory);
        // Created before the logger opens it: the async sink decides on its own when to touch the
        // file, and the export refuses to run without it, so leaving that to the sink would race
        // the export whenever the machine is busy.
        File.WriteAllText(Path.Combine(logDirectory, "unified.log"), "");
        logger = new UnifiedLogger(logDirectory);
    }

    [Fact]
    public void OpenCommand_WhenOptionsWereChanged_ResetsOptionsToDefaults()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedRangeCode = nameof(LogExportRangePreset.Last7Days);
        viewModel.IncludeCrashReports = true;
        viewModel.IncludeUserData = true;

        viewModel.OpenCommand.Execute(null);

        Assert.True(viewModel.IsVisible);
        Assert.Equal(nameof(LogExportRangePreset.All), viewModel.SelectedRangeCode);
        Assert.DoesNotContain(
            viewModel.RangeOptions,
            option => option.Code.Equals("Custom", StringComparison.Ordinal));
        Assert.False(viewModel.IncludeCrashReports);
        Assert.False(viewModel.IncludeUserData);
        Assert.False(viewModel.IsUserDataWarningVisible);
    }

    [Fact]
    public async Task ExportCommand_WhenFolderPickerCancels_KeepsDialogOpen()
    {
        ToastNotification? toast = null;
        var toastService = new ToastService();
        toastService.ToastRaised += notification => toast = notification;
        var viewModel = CreateViewModel(toastService: toastService);
        viewModel.OpenCommand.Execute(null);

        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsVisible);
        Assert.Null(toast);
    }

    [Fact]
    public async Task CloseCommand_WhenExportIsWaitingForDestination_CancelsWithoutSuccess()
    {
        var pickerResult = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ToastNotification? toast = null;
        var toastService = new ToastService();
        toastService.ToastRaised += notification => toast = notification;
        var viewModel = CreateViewModel(
            toastService: toastService,
            folderPicker: (_, _) => pickerResult.Task);
        viewModel.OpenCommand.Execute(null);

        var exportTask = viewModel.ExportCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsExporting);
        viewModel.CloseCommand.Execute(null);
        pickerResult.SetResult(Path.Combine(tempDir, "cancelled-selected"));
        await exportTask;

        Assert.False(viewModel.IsVisible);
        Assert.False(viewModel.IsExporting);
        Assert.Null(toast);
        Assert.False(Directory.Exists(Path.Combine(tempDir, "cancelled-selected")));
    }

    [Fact]
    public async Task ExportCommand_WhenExportSucceeds_ClosesDialogAndShowsSuccess()
    {
        await logger.LogAsync(LogEntrySeverity.Info, "Launcher started");
        logger.Dispose(); // flush async sink to disk before exporting
        ToastNotification? toast = null;
        var toastService = new ToastService();
        toastService.ToastRaised += notification => toast = notification;
        string? openedDirectory = null;
        var exportDirectory = Path.Combine(tempDir, "selected");
        var viewModel = CreateViewModel(
            toastService: toastService,
            exportDirectory: exportDirectory,
            openDirectory: directory => openedDirectory = directory);
        viewModel.OpenCommand.Execute(null);

        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsVisible);
        Assert.NotNull(toast);
        Assert.Equal(ToastSeverity.Success, toast.Severity);
        Assert.Equal(exportDirectory, openedDirectory);
        Assert.Single(Directory.GetFiles(exportDirectory, "*.zip"));
    }

    [Fact]
    public async Task ExportCommand_WithPresetRange_CropsEntriesToThatWindow()
    {
        var now = DateTimeOffset.Now;
        // Stamped relative to the wall clock: a fixed "recent" stamp would drift out of the
        // window as the calendar moves.
        var content =
            $"{now.AddDays(-40):O} [INF] [Test] Old entry\n" +
            $"{now.AddMinutes(-5):O} [INF] [Test] Recent entry\n";
        logger.Dispose(); // release the sink so the log can be replaced with deterministic content
        File.WriteAllText(logger.LogFilePath, content);
        var exportDirectory = Path.Combine(tempDir, "preset-selected");
        var viewModel = CreateViewModel(exportDirectory: exportDirectory);
        viewModel.OpenCommand.Execute(null);
        viewModel.SelectedRangeCode = nameof(LogExportRangePreset.LastHour);

        await viewModel.ExportCommand.ExecuteAsync(null);

        var zipPath = Assert.Single(Directory.GetFiles(exportDirectory, "*.zip"));
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.Single(item => item.FullName == "unified.log");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var exported = reader.ReadToEnd();
        Assert.Contains("Recent entry", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("Old entry", exported, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportCommand_WhenOpeningTheFolderFails_StillReportsSuccess()
    {
        await logger.LogAsync(LogEntrySeverity.Info, "Launcher started");
        ToastNotification? toast = null;
        var toastService = new ToastService();
        toastService.ToastRaised += notification => toast = notification;
        var viewModel = CreateViewModel(
            toastService: toastService,
            exportDirectory: Path.Combine(tempDir, "open-failure-selected"),
            openDirectory: _ => throw new IOException("The shell refused to open the folder."));
        viewModel.OpenCommand.Execute(null);

        await viewModel.ExportCommand.ExecuteAsync(null);

        // The archive exists, so a failed reveal must not turn the finished export into an error.
        Assert.False(viewModel.IsVisible);
        Assert.NotNull(toast);
        Assert.Equal(ToastSeverity.Success, toast.Severity);
        logger.Dispose(); // release Serilog file handle before reading
        var diagnostics = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("[LogExport]", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Opening the export folder", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportCommand_WhenExportFails_ShowsErrorAndWritesDiagnostic()
    {
        ToastNotification? toast = null;
        var toastService = new ToastService();
        toastService.ToastRaised += notification => toast = notification;
        var viewModel = CreateViewModel(
            toastService: toastService,
            exportDirectory: "\0");

        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.NotNull(toast);
        Assert.Equal(ToastSeverity.Error, toast.Severity);
        Assert.Contains("ArgumentException", toast.Message, StringComparison.Ordinal);
        logger.Dispose(); // release Serilog file handle before reading
        var diagnostics = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("[LogExport]", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Exporting to", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyLanguage_WhenDisplayNamesAreStale_RefreshesRangeOptionDisplayNames()
    {
        var viewModel = CreateViewModel();
        var option = viewModel.RangeOptions.Single(item => item.Code == nameof(LogExportRangePreset.Last24Hours));
        option.DisplayName = "";

        viewModel.ApplyLanguage();

        Assert.False(string.IsNullOrWhiteSpace(option.DisplayName));
    }

    [Fact]
    public async Task SelectedRange_WhenTheRangeHoldsNoEntry_ShowsTheEmptyRangeWarning()
    {
        WriteLog("2026-09-01T10:00:00.0000000+08:00 [INF] [Test] Old entry\n");
        var viewModel = CreateViewModel();
        viewModel.OpenCommand.Execute(null);

        viewModel.SelectedRangeCode = nameof(LogExportRangePreset.LastHour);
        await viewModel.PendingRangeProbeTask;

        Assert.True(viewModel.IsEmptyRangeWarningVisible);
        // Advisory only: a package with crash reports or user data may still be worth exporting.
        Assert.True(viewModel.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectedRange_WhenTheRangeHoldsEntries_HidesTheEmptyRangeWarning()
    {
        var now = DateTimeOffset.Now;
        WriteLog($"{now.AddMinutes(-5):O} [INF] [Test] Recent entry\n");
        var viewModel = CreateViewModel();
        viewModel.OpenCommand.Execute(null);

        viewModel.SelectedRangeCode = nameof(LogExportRangePreset.LastHour);
        await viewModel.PendingRangeProbeTask;

        Assert.False(viewModel.IsEmptyRangeWarningVisible);
    }

    [Fact]
    public async Task OpenCommand_AfterAnEmptyRangeHint_ResetsTheWarning()
    {
        WriteLog("2026-09-01T10:00:00.0000000+08:00 [INF] [Test] Old entry\n");
        var viewModel = CreateViewModel();
        viewModel.OpenCommand.Execute(null);
        viewModel.SelectedRangeCode = nameof(LogExportRangePreset.LastHour);
        await viewModel.PendingRangeProbeTask;
        Assert.True(viewModel.IsEmptyRangeWarningVisible);

        viewModel.OpenCommand.Execute(null);
        await viewModel.PendingRangeProbeTask;

        Assert.False(viewModel.IsEmptyRangeWarningVisible);
    }

    [Fact]
    public async Task OpenCommand_WhenTheLogIsEmpty_ShowsTheEmptyRangeWarningForAllEntries()
    {
        WriteLog("");
        var viewModel = CreateViewModel();

        viewModel.OpenCommand.Execute(null);
        await viewModel.PendingRangeProbeTask;

        Assert.True(viewModel.IsEmptyRangeWarningVisible);
    }

    /// <summary>
    /// Replaces the log with deterministic content: the sink is released first, so the file the
    /// export and the range probe read is exactly this text.
    /// </summary>
    private void WriteLog(string content)
    {
        logger.Dispose();
        File.WriteAllText(logger.LogFilePath, content);
    }

    private LogExportDialogViewModel CreateViewModel(
        ToastService? toastService = null,
        string? exportDirectory = null,
        Action<string>? openDirectory = null,
        Func<string, string?, Task<string?>>? folderPicker = null) =>
        new(
            new LogExportService(new LocalDiagnostics(logger)),
            new StubFilePickerService
            {
                FolderPicker = folderPicker ?? ((_, _) => Task.FromResult(exportDirectory))
            },
            toastService ?? new ToastService(),
            new LocalizationService(),
            new LocalDiagnostics(logger),
            openDirectory ?? (_ => { }));

    public void Dispose()
    {
        logger.Dispose();
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
