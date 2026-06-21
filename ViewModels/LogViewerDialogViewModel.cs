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
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.ViewModels;

public sealed partial class LogViewerDialogViewModel : ViewModelBase
{
    private readonly UnifiedLogger logger;
    private readonly LogExportService? exportService;
    private readonly ToastService? toastService;
    private readonly LocalizationService? localizer;
    private readonly LocalDiagnostics? diagnostics;
    private readonly Func<CancellationToken, Task<IReadOnlyList<LogEntryDisplay>>> entryLoader;
    private IReadOnlyList<LogEntryDisplay> allEntries = [];

    public Func<string, Task<string?>>? PickExportDirectoryAsync { get; set; }

    public Action<string>? OpenExportDirectory { get; set; }

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private string filterText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterAllActive))]
    [NotifyPropertyChangedFor(nameof(IsFilterErrorActive))]
    [NotifyPropertyChangedFor(nameof(IsFilterWarnActive))]
    [NotifyPropertyChangedFor(nameof(IsFilterInfoActive))]
    private LogEntrySeverity? severityFilter; // null = show all

    public bool IsFilterAllActive => SeverityFilter is null;
    public bool IsFilterErrorActive => SeverityFilter == LogEntrySeverity.Error;
    public bool IsFilterWarnActive => SeverityFilter == LogEntrySeverity.Warn;
    public bool IsFilterInfoActive => SeverityFilter == LogEntrySeverity.Info;
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
            allEntries = ReadEntries();
        }
        catch
        {
            allEntries = [];
        }

        ApplyFilter();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnSeverityFilterChanged(LogEntrySeverity? value) => ApplyFilter();

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
        try
        {
            allEntries = await entryLoader(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            allEntries = [];
        }

        ApplyFilter();
    }

    [RelayCommand]
    private void Close()
    {
        IsVisible = false;
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
    private void SetFilterError() => SeverityFilter = LogEntrySeverity.Error;
    [RelayCommand]
    private void SetFilterWarn() => SeverityFilter = LogEntrySeverity.Warn;
    [RelayCommand]
    private void SetFilterInfo() => SeverityFilter = LogEntrySeverity.Info;

    private IReadOnlyList<LogEntryDisplay> ReadEntries()
    {
        var logPath = logger.LogFilePath;
        return File.Exists(logPath)
            ? ParseEntries(File.ReadLines(logPath, Encoding.UTF8))
            : [];
    }

    private async Task<IReadOnlyList<LogEntryDisplay>> LoadEntriesAsync(CancellationToken cancellationToken)
    {
        var logPath = logger.LogFilePath;
        if (!File.Exists(logPath))
            return [];

        var lines = await File.ReadAllLinesAsync(logPath, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
        return ParseEntries(lines);
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

                // Skip session-boundary entries (shown in toast / crash-recovery UI)
                if (title == "Session started" || title == "Session ended")
                {
                    current = null;
                    continue;
                }

                current = new LogEntryDisplay
                {
                    TimestampText = match.Groups[1].Value,
                    SeverityLabel = severityLabel switch
                    {
                        "ERR" => "ERROR",
                        "WRN" => "WARN",
                        "INF" => "INFO",
                        _ => severityLabel
                    },
                    Title = title,
                    Details = "",
                    Severity = severityLabel switch
                    {
                        "ERR" => LogEntrySeverity.Error,
                        "WRN" => LogEntrySeverity.Warn,
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

public sealed class LogEntryDisplay
{
    public string TimestampText { get; set; } = "";
    public string SeverityLabel { get; set; } = "";
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
    public LogEntrySeverity Severity { get; set; }
}
