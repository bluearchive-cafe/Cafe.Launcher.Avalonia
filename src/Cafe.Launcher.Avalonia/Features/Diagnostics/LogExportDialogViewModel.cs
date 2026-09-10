using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Features.Diagnostics;

/// <summary>
/// Collects the diagnostics export options (time range and optional content) and owns the
/// export flow, so every export entry point shares one implementation.
/// </summary>
public sealed partial class LogExportDialogViewModel : ViewModelBase, IModalContentViewModel
{
    /// <summary>Log title of the diagnostics this view model writes.</summary>
    private const string LogTitle = "LogExport";

    private static readonly TimeSpan RangeProbeDebounceDelay = TimeSpan.FromMilliseconds(200);

    private readonly LogExportService exportService;
    private readonly IFilePickerService filePickerService;
    private readonly ToastService toastService;
    private readonly LocalizationService localizer;
    private readonly LocalDiagnostics diagnostics;
    private readonly Action<string> openDirectory;
    private CancellationTokenSource? rangeProbeCancellationTokenSource;
    private CancellationTokenSource? exportCancellationTokenSource;
    private int rangeProbeGeneration;
    private bool isEmptyRangeWarningVisible;

    /// <summary>Gets the active debounced range probe, for deterministic coordination.</summary>
    internal Task PendingRangeProbeTask { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private string selectedRangeCode = LogExportRangePreset.All.ToString();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool isExporting;

    [ObservableProperty]
    private bool includeCrashReports;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUserDataWarningVisible))]
    private bool includeUserData;

    public LogExportDialogViewModel(
        LogExportService exportService,
        IFilePickerService filePickerService,
        ToastService toastService,
        LocalizationService localizer,
        LocalDiagnostics diagnostics)
        : this(
            exportService,
            filePickerService,
            toastService,
            localizer,
            diagnostics,
            static path => ShellFolderOpener.OpenInFileManager(path))
    {
    }

    internal LogExportDialogViewModel(
        LogExportService exportService,
        IFilePickerService filePickerService,
        ToastService toastService,
        LocalizationService localizer,
        LocalDiagnostics diagnostics,
        Action<string> openDirectory)
    {
        this.exportService = exportService;
        this.filePickerService = filePickerService;
        this.toastService = toastService;
        this.localizer = localizer;
        this.diagnostics = diagnostics;
        this.openDirectory = openDirectory;

        RangeOptions =
        [
            new() { Code = LogExportRangePreset.All.ToString() },
            new() { Code = LogExportRangePreset.LastHour.ToString() },
            new() { Code = LogExportRangePreset.Last24Hours.ToString() },
            new() { Code = LogExportRangePreset.Last7Days.ToString() },
            new() { Code = LogExportRangePreset.Last30Days.ToString() }
        ];
        RefreshDisplayNames();
    }

    /// <summary>Gets the time-range choices, in the order the dialog presents them.</summary>
    public ObservableCollection<SettingOption> RangeOptions { get; }

    /// <summary>
    /// Gets whether user data is selected, which carries local paths, the player UID, and the
    /// install attribution code.
    /// </summary>
    public bool IsUserDataWarningVisible => IncludeUserData;

    /// <summary>
    /// Gets whether the chosen range holds no log entry, which would leave the package carrying an
    /// empty log file. Advisory only: crash reports and user data may still be worth exporting.
    /// </summary>
    public bool IsEmptyRangeWarningVisible
    {
        get => isEmptyRangeWarningVisible;
        private set => SetProperty(ref isEmptyRangeWarningVisible, value);
    }

    /// <summary>Gets whether a new export can start.</summary>
    public bool CanExport => !IsExporting;

    /// <summary>Refreshes option display names after the active UI language changes.</summary>
    public void ApplyLanguage() => RefreshDisplayNames();

    private void RefreshDisplayNames()
    {
        foreach (var option in RangeOptions)
        {
            option.DisplayName = option.Code switch
            {
                nameof(LogExportRangePreset.LastHour) => localizer.T(LocalizationKeys.LogExportRangeLastHour),
                nameof(LogExportRangePreset.Last24Hours) => localizer.T(LocalizationKeys.LogExportRangeLast24Hours),
                nameof(LogExportRangePreset.Last7Days) => localizer.T(LocalizationKeys.LogExportRangeLast7Days),
                nameof(LogExportRangePreset.Last30Days) => localizer.T(LocalizationKeys.LogExportRangeLast30Days),
                _ => localizer.T(LocalizationKeys.LogExportRangeAll)
            };
        }
    }

    /// <summary>Opens the dialog with defaults so a previous choice never leaks into the next export.</summary>
    [RelayCommand]
    private void Open()
    {
        CancelRangeProbe();
        SelectedRangeCode = LogExportRangePreset.All.ToString();
        IncludeCrashReports = false;
        IncludeUserData = false;
        IsEmptyRangeWarningVisible = false;
        IsVisible = true;
        QueueRangeProbe();
    }

    [RelayCommand]
    private void Close()
    {
        if (IsExporting)
        {
            exportCancellationTokenSource?.Cancel();
            return;
        }

        CancelRangeProbe();
        IsVisible = false;
    }

    partial void OnSelectedRangeCodeChanged(string value) => QueueRangeProbe();

    /// <summary>
    /// Re-checks whether the chosen range holds any log entry. The previous probe is cancelled so
    /// only the latest selection decides, and the short delay keeps a dragged date picker from
    /// reading the log files on every intermediate value.
    /// </summary>
    private void QueueRangeProbe()
    {
        CancelRangeProbe();
        rangeProbeCancellationTokenSource = new CancellationTokenSource();
        var generation = rangeProbeGeneration;
        PendingRangeProbeTask = ProbeRangeAsync(generation, rangeProbeCancellationTokenSource.Token);
    }

    private void CancelRangeProbe()
    {
        rangeProbeGeneration++;
        rangeProbeCancellationTokenSource?.Cancel();
        rangeProbeCancellationTokenSource?.Dispose();
        rangeProbeCancellationTokenSource = null;
    }

    private async Task ProbeRangeAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(RangeProbeDebounceDelay, cancellationToken);
            var hasEntries = await exportService.HasLogEntriesAsync(
                BuildOptions(),
                cancellationToken);
            if (generation == rangeProbeGeneration && !cancellationToken.IsCancellationRequested)
            {
                IsEmptyRangeWarningVisible = !hasEntries;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // The hint is advisory: a failed probe hides it and leaves the export alone.
            IsEmptyRangeWarningVisible = false;
            await diagnostics.WarningAsync(
                LogTitle,
                $"Probing the export range failed: {exception.Message}",
                CancellationToken.None);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var options = BuildOptions();
        CancelRangeProbe();
        exportCancellationTokenSource?.Dispose();
        exportCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = exportCancellationTokenSource.Token;
        IsExporting = true;
        // Kept outside the try so the failure diagnostic can name the folder it was writing to.
        string? destination = null;
        try
        {
            Directory.CreateDirectory(LogExportService.DefaultExportDirectory);
            destination = await filePickerService.PickFolderAsync(
                localizer.T(LocalizationKeys.LogExportFolderPickerTitle),
                LogExportService.DefaultExportDirectory);
            if (string.IsNullOrWhiteSpace(destination))
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var zipPath = await exportService.ExportAsync(destination, options, cancellationToken);
            IsVisible = false;
            toastService.ShowSuccess(localizer.F(LocalizationKeys.LogExportSucceeded, zipPath));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            IsVisible = false;
            return;
        }
        catch (Exception exception)
        {
            toastService.ShowError(ErrorHandlingService.FormatToastMessage(
                localizer.T(LocalizationKeys.LogExportFailed),
                exception));
            await diagnostics.ErrorAsync(
                LogTitle,
                $"Exporting to {destination ?? "an unpicked folder"} failed.",
                exception,
                CancellationToken.None);
            return;
        }
        finally
        {
            IsExporting = false;
            exportCancellationTokenSource.Dispose();
            exportCancellationTokenSource = null;
        }

        await TryOpenDestinationAsync(destination);
    }

    /// <summary>
    /// Reveals the folder holding the new archive. A shell failure must not report a finished
    /// export as failed, so it is logged and otherwise ignored.
    /// </summary>
    private async Task TryOpenDestinationAsync(string destination)
    {
        try
        {
            openDirectory(destination);
        }
        catch (Exception exception)
        {
            await diagnostics.ErrorAsync(
                LogTitle,
                $"Opening the export folder {destination} failed.",
                exception,
                CancellationToken.None);
        }
    }

    private LogExportOptions BuildOptions()
    {
        var range = Enum.TryParse<LogExportRangePreset>(SelectedRangeCode, out var parsed)
            ? parsed
            : LogExportRangePreset.All;
        return new LogExportOptions
        {
            Range = range,
            IncludeCrashReports = IncludeCrashReports,
            IncludeUserData = IncludeUserData
        };
    }
}
