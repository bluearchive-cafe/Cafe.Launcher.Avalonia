using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Features.Diagnostics;

public sealed partial class LogViewerDialogViewModel : ViewModelBase, IModalContentViewModel
{
    private const int PageSize = 500;
    private static readonly TimeSpan FilterDebounceDelay = TimeSpan.FromMilliseconds(200);
    private readonly UnifiedLogger logger;
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
        ToastService toastService,
        LocalizationService localizer,
        LocalDiagnostics diagnostics)
        : this(logger, toastService, localizer, diagnostics, null)
    {
    }

    internal LogViewerDialogViewModel(
        UnifiedLogger logger,
        ToastService? toastService,
        LocalizationService? localizer,
        LocalDiagnostics? diagnostics,
        Func<CancellationToken, Task<IReadOnlyList<LogEntryDisplay>>>? entryLoader)
    {
        this.logger = logger;
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
            // 同步路径无法 await 诊断管道，走静态 LogSync 保证失败进入本地日志
            // 而非只在调试器可见；用户可见反馈由异步的 OpenAsync 路径负责。
            LocalDiagnostics.LogSync(
                LogEntrySeverity.Error,
                "LogViewer",
                $"failed to read log entries synchronously: {ex.Message}");
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
            // 日志读取失败正是用户最需要日志的时刻：空列表必须伴随显式的
            // 错误提示与本地日志记录，避免「加载失败」被误读成「没有日志」。
            toastService?.ShowError(ErrorHandlingService.FormatToastMessage(
                localizer?.T(LocalizationKeys.LogLoadFailed) ?? "Failed to load log entries",
                ex));
            if (diagnostics is not null)
            {
                await diagnostics.ErrorAsync(
                    "LogViewer load failed.",
                    ex,
                    CancellationToken.None);
            }

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
            toastService?.ShowError(ErrorHandlingService.FormatToastMessage(
                localizer?.T(LocalizationKeys.LogLoadFailed) ?? "Failed to load log entries",
                ex));
            if (diagnostics is not null)
            {
                await diagnostics.ErrorAsync(
                    "LogViewer load earlier failed.",
                    ex,
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

    private static IReadOnlyList<LogEntryDisplay> ParseEntries(IEnumerable<string> lines)
    {
        var entries = new List<LogEntryDisplay>();
        foreach (var record in LogEntryReader.Read(lines))
        {
            entries.Add(new LogEntryDisplay
            {
                TimestampText = record.TimestampText,
                SeverityLabel = record.SeverityCode switch
                {
                    "VRB" => "VERBOSE",
                    "DBG" => "DEBUG",
                    "INF" => "INFO",
                    "WRN" => "WARN",
                    "ERR" => "ERROR",
                    "FTL" => "FATAL",
                    _ => record.SeverityCode
                },
                Title = record.Title,
                Details = record.Lines.Count > 1
                    ? string.Join("\n", record.Lines.Skip(1))
                    : "",
                Severity = record.SeverityCode switch
                {
                    "VRB" => LogEntrySeverity.Verbose,
                    "DBG" => LogEntrySeverity.Debug,
                    "INF" => LogEntrySeverity.Info,
                    "WRN" => LogEntrySeverity.Warn,
                    "ERR" => LogEntrySeverity.Error,
                    "FTL" => LogEntrySeverity.Fatal,
                    _ => LogEntrySeverity.Info
                }
            });
        }

        return entries;
    }
}
