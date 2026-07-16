using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.ViewModels;

public sealed partial class LogViewerDialogViewModel : ViewModelBase, IModalContentViewModel
{
    private const int PageSize = 500;
    private static readonly TimeSpan FilterDebounceDelay = TimeSpan.FromMilliseconds(200);
    private readonly UnifiedLogger logger;
    private readonly LogExportService? exportService;
    private readonly ToastService? toastService;
    private readonly LocalizationService? localizer;
    private readonly LocalDiagnostics? diagnostics;
    private readonly Func<CancellationToken, Task<IReadOnlyList<LogEntryDisplay>>> entryLoader;
    private IReadOnlyList<LogEntryDisplay> allEntries = [];
    private CancellationTokenSource? filterCancellationTokenSource;
    private int loadedPageCount = 1;
    private int totalEntryCount;

    /// <summary>Gets the active debounced filter operation for deterministic coordination.</summary>
    internal Task PendingFilterTask { get; private set; } = Task.CompletedTask;

    /// <summary>Gets whether another 500-entry page is available before the loaded entries.</summary>
    public bool HasEarlierEntries => allEntries.Count < totalEntryCount;

    public Func<string, Task<string?>>? PickExportDirectoryAsync { get; set; }

    public Action<string>? OpenExportDirectory { get; set; }

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private string filterText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterAllActive))]
    [NotifyPropertyChangedFor(nameof(IsFilterVerboseActive))]
    [NotifyPropertyChangedFor(nameof(IsFilterDebugActive))]
    [NotifyPropertyChangedFor(nameof(IsFilterInfoActive))]
    [NotifyPropertyChangedFor(nameof(IsFilterWarnActive))]
    [NotifyPropertyChangedFor(nameof(IsFilterErrorActive))]
    [NotifyPropertyChangedFor(nameof(IsFilterFatalActive))]
    private LogEntrySeverity? severityFilter; // null = show all

    public bool IsFilterAllActive => SeverityFilter is null;
    public bool IsFilterVerboseActive => SeverityFilter == LogEntrySeverity.Verbose;
    public bool IsFilterDebugActive => SeverityFilter == LogEntrySeverity.Debug;
    public bool IsFilterInfoActive => SeverityFilter == LogEntrySeverity.Info;
    public bool IsFilterWarnActive => SeverityFilter == LogEntrySeverity.Warn;
    public bool IsFilterErrorActive => SeverityFilter == LogEntrySeverity.Error;
    public bool IsFilterFatalActive => SeverityFilter == LogEntrySeverity.Fatal;
    public bool HasFilteredEntries => FilteredEntries.Count > 0;
    public bool IsEmpty => FilteredEntries.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilteredEntries))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private ObservableCollection<LogEntryDisplay> filteredEntries = [];

    public LogViewerDialogViewModel(
        UnifiedLogger logger,
        LogExportService exportService,
        ToastService toastService,
        LocalizationService localizer,
        LocalDiagnostics diagnostics)
        : this(logger, exportService, toastService, localizer, diagnostics, null)
    {
    }

    internal LogViewerDialogViewModel(
        UnifiedLogger logger,
        LogExportService? exportService,
        ToastService? toastService,
        LocalizationService? localizer,
        LocalDiagnostics? diagnostics,
        Func<CancellationToken, Task<IReadOnlyList<LogEntryDisplay>>>? entryLoader)
    {
        this.logger = logger;
        this.exportService = exportService;
        this.toastService = toastService;
        this.localizer = localizer;
        this.diagnostics = diagnostics;
        this.entryLoader = entryLoader ?? LoadEntriesAsync;
    }

    public void LoadEntries()
    {
        try
        {
            loadedPageCount = 1;
            SetLoadedEntries(ReadEntries());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"LogViewer: failed to read log entries synchronously: {ex.Message}");
            allEntries = [];
        }

        ApplyFilter();
    }

    partial void OnFilterTextChanged(string value)
    {
        filterCancellationTokenSource?.Cancel();
        filterCancellationTokenSource?.Dispose();
        filterCancellationTokenSource = new CancellationTokenSource();
        PendingFilterTask = ApplyFilterAfterDelayAsync(filterCancellationTokenSource.Token);
    }

    partial void OnSeverityFilterChanged(LogEntrySeverity? value) => ApplyFilter();

    private async Task ApplyFilterAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(FilterDebounceDelay, cancellationToken);
            ApplyFilter();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<LogEntryDisplay> filtered = allEntries;
        if (SeverityFilter is not null)
            filtered = filtered.Where(e => e.Severity == SeverityFilter.Value);
        if (!string.IsNullOrWhiteSpace(FilterText))
            filtered = filtered.Where(e =>
                e.Title.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                e.Details.Contains(FilterText, StringComparison.OrdinalIgnoreCase));

        FilteredEntries = new ObservableCollection<LogEntryDisplay>(filtered);
    }

    [RelayCommand]
    private async Task OpenAsync(CancellationToken cancellationToken)
    {
        IsVisible = true;
        loadedPageCount = 1;
        try
        {
            SetLoadedEntries(await entryLoader(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"LogViewer: failed to load log entries: {ex.Message}");
            allEntries = [];
        }

        ApplyFilter();
    }

    [RelayCommand]
    private void Close()
    {
        filterCancellationTokenSource?.Cancel();
        IsVisible = false;
    }

    [RelayCommand(CanExecute = nameof(HasEarlierEntries))]
    private async Task LoadEarlierAsync(CancellationToken cancellationToken)
    {
        loadedPageCount++;
        try
        {
            SetLoadedEntries(await entryLoader(cancellationToken));
            ApplyFilter();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            loadedPageCount--;
        }
        catch (Exception ex)
        {
            loadedPageCount--;
            System.Diagnostics.Debug.WriteLine(
                $"LogViewer: failed to load earlier entries: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (exportService is null || PickExportDirectoryAsync is null)
            return;

        try
        {
            Directory.CreateDirectory(LogExportService.DefaultExportDirectory);
            var selectedDirectory = await PickExportDirectoryAsync(
                LogExportService.DefaultExportDirectory);
            if (string.IsNullOrWhiteSpace(selectedDirectory))
                return;

            var zipPath = await exportService.ExportAsync(selectedDirectory);
            toastService?.ShowSuccess(
                localizer?.F("logExportSucceeded", zipPath)
                ?? $"Logs exported to {zipPath}");
            try
            {
                OpenExportDirectory?.Invoke(selectedDirectory);
            }
            catch (Exception exception)
            {
                if (diagnostics is not null)
                {
                    await diagnostics.ErrorAsync(
                        "Log export directory open failed.",
                        exception,
                        CancellationToken.None);
                }
            }
        }
        catch (Exception exception)
        {
            toastService?.ShowError(
                localizer?.F("logExportFailed", exception.Message)
                ?? $"Log export failed: {exception.Message}");
            if (diagnostics is not null)
            {
                await diagnostics.ErrorAsync(
                    "Log export failed.",
                    exception,
                    CancellationToken.None);
            }
        }
    }

    [RelayCommand]
    private void SetFilterAll() => SeverityFilter = null;
    [RelayCommand]
    private void SetFilterVerbose() => SeverityFilter = LogEntrySeverity.Verbose;
    [RelayCommand]
    private void SetFilterDebug() => SeverityFilter = LogEntrySeverity.Debug;
    [RelayCommand]
    private void SetFilterInfo() => SeverityFilter = LogEntrySeverity.Info;
    [RelayCommand]
    private void SetFilterWarn() => SeverityFilter = LogEntrySeverity.Warn;
    [RelayCommand]
    private void SetFilterError() => SeverityFilter = LogEntrySeverity.Error;
    [RelayCommand]
    private void SetFilterFatal() => SeverityFilter = LogEntrySeverity.Fatal;

    private IReadOnlyList<LogEntryDisplay> ReadEntries()
    {
        var logPath = logger.LogFilePath;
        if (!File.Exists(logPath))
            return [];

        using var reader = OpenLogReader(logPath);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
            lines.Add(line);

        return ParseEntries(lines);
    }

    private void SetLoadedEntries(IReadOnlyList<LogEntryDisplay> entries)
    {
        totalEntryCount = entries.Count;
        var takeCount = Math.Min(entries.Count, checked(loadedPageCount * PageSize));
        allEntries = entries.Skip(entries.Count - takeCount).ToArray();
        OnPropertyChanged(nameof(HasEarlierEntries));
        LoadEarlierCommand.NotifyCanExecuteChanged();
    }

    private async Task<IReadOnlyList<LogEntryDisplay>> LoadEntriesAsync(CancellationToken cancellationToken)
    {
        var logPath = logger.LogFilePath;
        if (!File.Exists(logPath))
            return [];

        using var reader = OpenLogReader(logPath);
        var lines = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            lines.Add(line);

        return ParseEntries(lines);
    }

    private static StreamReader OpenLogReader(string logPath)
    {
        var stream = new FileStream(
            logPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    }

    private static readonly Regex EntryLineRegex = new(
        @"^(\d{4}-\d{2}-\d{2}T[\d:.+-]+) \[(ERR|WRN|INF|VRB|DBG|FTL)\] (.+)",
        RegexOptions.CultureInvariant);

    private static IReadOnlyList<LogEntryDisplay> ParseEntries(IEnumerable<string> lines)
    {
        var entries = new List<LogEntryDisplay>();
        LogEntryDisplay? current = null;

        foreach (var line in lines)
        {
            var match = EntryLineRegex.Match(line);
            if (match.Success)
            {
                // Commit previous entry
                if (current is not null)
                    entries.Add(current);

                var severityLabel = match.Groups[2].Value;
                var title = match.Groups[3].Value;

                current = new LogEntryDisplay
                {
                    TimestampText = match.Groups[1].Value,
                    SeverityLabel = severityLabel switch
                    {
                        "VRB" => "VERBOSE",
                        "DBG" => "DEBUG",
                        "INF" => "INFO",
                        "WRN" => "WARN",
                        "ERR" => "ERROR",
                        "FTL" => "FATAL",
                        _ => severityLabel
                    },
                    Title = title,
                    Details = "",
                    Severity = severityLabel switch
                    {
                        "VRB" => LogEntrySeverity.Verbose,
                        "DBG" => LogEntrySeverity.Debug,
                        "INF" => LogEntrySeverity.Info,
                        "WRN" => LogEntrySeverity.Warn,
                        "ERR" => LogEntrySeverity.Error,
                        "FTL" => LogEntrySeverity.Fatal,
                        _ => LogEntrySeverity.Info
                    }
                };
            }
            else if (current is not null)
            {
                // Continuation line (message body or exception stack trace)
                current.Details += (current.Details.Length > 0 ? "\n" : "") + line;
            }
        }

        if (current is not null)
            entries.Add(current);

        return entries;
    }
}
