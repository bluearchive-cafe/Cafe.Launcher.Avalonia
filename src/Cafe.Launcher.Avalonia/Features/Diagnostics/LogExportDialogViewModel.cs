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
    private static readonly string CustomRangeCode = LogExportRangePreset.Custom.ToString();

    private readonly LogExportService exportService;
    private readonly IFilePickerService filePickerService;
    private readonly ToastService toastService;
    private readonly LocalizationService localizer;
    private readonly LocalDiagnostics diagnostics;
    private readonly Action<string> openDirectory;

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomRangeVisible))]
    [NotifyPropertyChangedFor(nameof(IsRangeInvalid))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private string selectedRangeCode = LogExportRangePreset.All.ToString();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRangeInvalid))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private DateTimeOffset? customFromDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRangeInvalid))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private DateTimeOffset? customToDate;

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
            new() { Code = LogExportRangePreset.Last30Days.ToString() },
            new() { Code = CustomRangeCode }
        ];
        RefreshDisplayNames();
    }

    public ObservableCollection<SettingOption> RangeOptions { get; }

    /// <summary>Gets whether the custom date pickers are shown.</summary>
    public bool IsCustomRangeVisible => SelectedRangeCode == CustomRangeCode;

    /// <summary>Gets whether the custom range has its bounds the wrong way round.</summary>
    public bool IsRangeInvalid =>
        IsCustomRangeVisible
        && CustomFromDate is not null
        && CustomToDate is not null
        && CustomFromDate.Value.Date > CustomToDate.Value.Date;

    /// <summary>Gets whether user data is selected, which carries local paths and the player UID.</summary>
    public bool IsUserDataWarningVisible => IncludeUserData;

    /// <summary>Gets whether the export can run: a custom range needs at least one bound in a valid order.</summary>
    public bool CanExport =>
        !IsRangeInvalid
        && (!IsCustomRangeVisible || CustomFromDate is not null || CustomToDate is not null);

    /// <summary>Refreshes option display names after the active UI language changes.</summary>
    public void ApplyLanguage()
    {
        RefreshDisplayNames();
        OnPropertyChanged(nameof(IsRangeInvalid));
    }

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
                nameof(LogExportRangePreset.Custom) => localizer.T(LocalizationKeys.LogExportRangeCustom),
                _ => localizer.T(LocalizationKeys.LogExportRangeAll)
            };
        }
    }

    /// <summary>Opens the dialog with defaults so a previous choice never leaks into the next export.</summary>
    [RelayCommand]
    private void Open()
    {
        SelectedRangeCode = LogExportRangePreset.All.ToString();
        CustomFromDate = null;
        CustomToDate = null;
        IncludeCrashReports = false;
        IncludeUserData = false;
        IsVisible = true;
    }

    [RelayCommand]
    private void Close() => IsVisible = false;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        try
        {
            var options = BuildOptions();
            Directory.CreateDirectory(LogExportService.DefaultExportDirectory);
            var selectedDirectory = await filePickerService.PickFolderAsync(
                localizer.T(LocalizationKeys.LogExportFolderPickerTitle),
                LogExportService.DefaultExportDirectory);
            if (string.IsNullOrWhiteSpace(selectedDirectory))
            {
                return;
            }

            var zipPath = await exportService.ExportAsync(selectedDirectory, options);
            IsVisible = false;
            toastService.ShowSuccess(localizer.F(LocalizationKeys.LogExportSucceeded, zipPath));
            try
            {
                openDirectory(selectedDirectory);
            }
            catch (Exception exception)
            {
                await diagnostics.ErrorAsync(
                    "Log export directory open failed.",
                    exception,
                    CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            toastService.ShowError(ErrorHandlingService.FormatToastMessage(
                localizer.T(LocalizationKeys.LogExportFailed),
                exception));
            await diagnostics.ErrorAsync(
                "Log export failed.",
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
            CustomFrom = CustomFromDate,
            CustomTo = CustomToDate,
            IncludeCrashReports = IncludeCrashReports,
            IncludeUserData = IncludeUserData
        };
    }
}
