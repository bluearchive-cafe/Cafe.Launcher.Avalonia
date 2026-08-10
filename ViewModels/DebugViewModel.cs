using System;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Serilog.Events;

namespace Cafe.Launcher.Avalonia.ViewModels;

/// <summary>
/// Supplies the development-only diagnostics overlay with state, commands, and shell coordination hooks.
/// </summary>
public sealed partial class DebugViewModel : ViewModelBase, IModalContentViewModel, IDisposable
{
    private readonly ToastService toastService;
    private readonly UnifiedLogger unifiedLogger;
    private readonly IErrorHandlingService errorHandling;
    private readonly LauncherSettingsService settingsService;
    private readonly GameOperationsViewModel operations;
    private readonly ShellViewModel shell;
    private readonly LogExportService? logExportService;
    private bool disposed;

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private string logFilePath = "";

    [ObservableProperty]
    private string dataDirectoryPath = "";

    [ObservableProperty]
    private string systemInfoText = "";

    [ObservableProperty]
    private string logLevelDisplay = "";

    [ObservableProperty]
    private int selectedLogLevelIndex;

    [ObservableProperty]
    private bool isDownloadRunning;

    [ObservableProperty]
    private bool isDownloadPaused;

    [ObservableProperty]
    private string downloadStatusText = "";

    [ObservableProperty]
    private string settingsJsonDisplay = "";

    [ObservableProperty]
    private string lastActionResult = "";

    public event Func<Task>? RefreshRequested;
    public event Func<Task>? ResetSettingsRequested;
    public event Action? ResetSettingsConfirmationRequested;

    public Action<string>? OpenDirectory { get; set; }
    public Func<string, Task<string?>>? PickExportDirectoryAsync { get; set; }

    public DebugViewModel(
        ToastService toastService,
        UnifiedLogger unifiedLogger,
        IErrorHandlingService errorHandling,
        LauncherSettingsService settingsService,
        GameOperationsViewModel operations,
        ShellViewModel shell,
        LogExportService? logExportService = null)
    {
        this.toastService = toastService;
        this.unifiedLogger = unifiedLogger;
        this.errorHandling = errorHandling;
        this.settingsService = settingsService;
        this.operations = operations;
        this.shell = shell;
        this.logExportService = logExportService;

        operations.PropertyChanged += OnOperationsPropertyChanged;
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        RefreshSystemInfo();
        RefreshLogLevel();
        RefreshDownloadStatus();
        await RefreshSettingsDisplayAsync();
        IsVisible = true;
    }

    [RelayCommand]
    private void Close()
    {
        IsVisible = false;
    }

    // ── System info ──────────────────────────────────────────────────────

    private void RefreshSystemInfo()
    {
        LogFilePath = unifiedLogger.LogFilePath;
        DataDirectoryPath = LauncherUserDataDirectory.Root;

        SystemInfoText = Format(
            shell.I18n.DebugSystemInfoFormat,
            BuildInfo.LauncherVersion,
            BuildInfo.CommitSha,
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Environment.OSVersion,
            BuildInfo.BuildConfiguration,
            typeof(global::Avalonia.Application).Assembly.GetName().Version ?? new Version());
    }

    // ── Log level ────────────────────────────────────────────────────────

    public string[] LogLevelOptions =>
    [
        shell.I18n.LogLevelVerbose,
        shell.I18n.LogLevelDebug,
        shell.I18n.LogLevelInformation,
        shell.I18n.LogLevelWarning,
        shell.I18n.LogLevelError,
        shell.I18n.LogLevelFatal
    ];

    public void ApplyLanguage()
    {
        OnPropertyChanged(nameof(LogLevelOptions));
        RefreshSystemInfo();
        RefreshLogLevel();
        RefreshDownloadStatus();
    }

    private void RefreshLogLevel()
    {
        var level = unifiedLogger.MinimumLevel;
        SelectedLogLevelIndex = level switch
        {
            LogEventLevel.Verbose => 0,
            LogEventLevel.Debug => 1,
            LogEventLevel.Information => 2,
            LogEventLevel.Warning => 3,
            LogEventLevel.Error => 4,
            LogEventLevel.Fatal => 5,
            _ => 2
        };
        LogLevelDisplay = LogLevelOptions[SelectedLogLevelIndex];
    }

    partial void OnSelectedLogLevelIndexChanged(int value)
    {
        if (value < 0 || value >= LogLevelOptions.Length)
        {
            return;
        }

        var level = value switch
        {
            0 => LogEventLevel.Verbose,
            1 => LogEventLevel.Debug,
            2 => LogEventLevel.Information,
            3 => LogEventLevel.Warning,
            4 => LogEventLevel.Error,
            5 => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };
        unifiedLogger.SetMinimumLevel(level);
        LogLevelDisplay = LogLevelOptions[Math.Clamp(value, 0, LogLevelOptions.Length - 1)];
        LastActionResult = Format(shell.I18n.DebugLogLevelSet, LogLevelDisplay);
    }

    // ── Log writes ───────────────────────────────────────────────────────

    [RelayCommand]
    private void WriteTestLog(string severity)
    {
        var sev = severity switch
        {
            "Verbose" => LogEntrySeverity.Verbose,
            "Debug" => LogEntrySeverity.Debug,
            "Info" => LogEntrySeverity.Info,
            "Warn" => LogEntrySeverity.Warn,
            "Error" => LogEntrySeverity.Error,
            "Fatal" => LogEntrySeverity.Fatal,
            _ => LogEntrySeverity.Info
        };
        var severityDisplay = severity switch
        {
            "Verbose" => shell.I18n.LogLevelVerbose,
            "Debug" => shell.I18n.LogLevelDebug,
            "Info" => shell.I18n.LogLevelInformation,
            "Warn" => shell.I18n.LogLevelWarning,
            "Error" => shell.I18n.LogLevelError,
            "Fatal" => shell.I18n.LogLevelFatal,
            _ => shell.I18n.LogLevelInformation
        };
        var message = Format(shell.I18n.DebugTestLogMessage, DateTimeOffset.Now.ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture));
        LocalDiagnostics.LogSync(sev, "DebugPanel", message);
        LastActionResult = Format(shell.I18n.DebugLogEntryWritten, severityDisplay);
    }

    // ── Toast notifications ──────────────────────────────────────────────

    [RelayCommand]
    private void TestToast(string severity)
    {
        var severityDisplay = severity switch
        {
            "Info" => shell.I18n.LogLevelInformation,
            "Success" => shell.I18n.ToastSuccess,
            "Warning" => shell.I18n.LogLevelWarning,
            "Error" => shell.I18n.LogLevelError,
            _ => severity
        };
        var message = Format(
            shell.I18n.DebugTestToastMessage,
            severityDisplay,
            DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture));
        switch (severity)
        {
            case "Info":
                toastService.Show(message, ToastSeverity.Info);
                break;
            case "Success":
                toastService.ShowSuccess(message);
                break;
            case "Warning":
                toastService.ShowWarning(message);
                break;
            case "Error":
                toastService.ShowError(message);
                break;
        }
        LastActionResult = Format(shell.I18n.DebugToastShown, severityDisplay);
    }

    [RelayCommand]
    private void TestActionToast()
    {
        toastService.Show(new ToastOptions
        {
            Title = shell.I18n.DebugActionToastTitle,
            Message = shell.I18n.DebugActionToastMessage,
            Severity = ToastSeverity.Info,
            PrimaryAction = new ToastAction(
                shell.I18n.DebugSimulateSuccess,
                _ => Task.FromResult(ToastActionResult.Success())),
            SecondaryAction = new ToastAction(
                shell.I18n.DebugSimulateFailure,
                _ => Task.FromResult(ToastActionResult.Failure(
                    shell.I18n.DebugActionFailureMessage,
                    shell.I18n.DebugActionFailureTitle)))
        });
    }

    // ── Error dialog ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task TriggerErrorDialogAsync()
    {
        var exception = new InvalidOperationException(Format(
            shell.I18n.DebugCriticalErrorMessage,
            DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture)));
        await errorHandling.HandleCriticalErrorAsync(
            "DebugPanel: test critical error", exception);
        LastActionResult = shell.I18n.DebugCriticalErrorTriggered;
    }

    [RelayCommand]
    private async Task SimulateHandledErrorAsync()
    {
        var exception = new InvalidOperationException(Format(
            shell.I18n.DebugHandledErrorMessage,
            DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture)));
        await errorHandling.HandleErrorAsync(
            "DebugPanel: test handled error", exception,
            new ErrorHandlingOptions
            {
                ToastMessage = exception.Message,
                OperationNoteKey = "networkWithMessage"
            });
        LastActionResult = shell.I18n.DebugHandledErrorSimulated;
    }

    // ── Game operations ──────────────────────────────────────────────────

    private void OnOperationsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameOperationsViewModel.IsDownloadRunning)
            or nameof(GameOperationsViewModel.IsPaused))
        {
            RefreshDownloadStatus();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        operations.PropertyChanged -= OnOperationsPropertyChanged;
    }

    private void RefreshDownloadStatus()
    {
        IsDownloadRunning = operations.IsDownloadRunning;
        IsDownloadPaused = operations.IsPaused;
        DownloadStatusText = IsDownloadRunning
            ? (IsDownloadPaused ? shell.I18n.Paused : shell.I18n.Downloading)
            : shell.I18n.DebugIdle;
    }

    [RelayCommand]
    private void TogglePauseResume()
    {
        if (!operations.CanPauseOperation)
        {
            return;
        }

        operations.PauseResumeCommand.Execute(null);
        LastActionResult = operations.IsPaused ? shell.I18n.PauseRequested : shell.I18n.ResumeRequested;
    }

    [RelayCommand]
    private void StopDownload()
    {
        if (!operations.IsDownloadRunning)
        {
            return;
        }

        operations.StopOperationCommand.Execute(null);
        LastActionResult = shell.I18n.StopRequested;
    }

    [RelayCommand]
    private async Task RefreshStateAsync()
    {
        if (RefreshRequested is not null)
        {
            await RefreshRequested.Invoke();
        }
        LastActionResult = shell.I18n.DebugStateRefreshTriggered;
    }

    // ── Settings ─────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions SettingsJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private async Task RefreshSettingsDisplayAsync()
    {
        try
        {
            var settings = await settingsService.ReadAsync();
            SettingsJsonDisplay = JsonSerializer.Serialize(settings, SettingsJsonOptions);
        }
        catch (Exception ex)
        {
            SettingsJsonDisplay = Format(shell.I18n.DebugSettingsReadFailed, ex.Message);
        }
    }

    [RelayCommand]
    private void ResetSettings()
    {
        ResetSettingsConfirmationRequested?.Invoke();
    }

    public async Task ConfirmResetSettingsAsync()
    {
        if (ResetSettingsRequested is not null)
        {
            await ResetSettingsRequested.Invoke();
            await RefreshSettingsDisplayAsync();
            LastActionResult = shell.I18n.DebugSettingsReset;
        }
    }

    // ── File operations ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        if (logExportService is null || PickExportDirectoryAsync is null)
        {
            LastActionResult = shell.I18n.DebugLogExportUnavailable;
            return;
        }

        var dir = await PickExportDirectoryAsync(LauncherUserDataDirectory.Root);
        if (string.IsNullOrWhiteSpace(dir))
        {
            LastActionResult = shell.I18n.DebugExportCancelled;
            return;
        }

        try
        {
            var zipPath = await logExportService.ExportAsync(dir);
            LastActionResult = Format(shell.I18n.LogExportSucceeded, zipPath);

            // Open the containing folder
            var folder = Path.GetDirectoryName(zipPath);
            if (folder is not null)
            {
                OpenDirectory?.Invoke(folder);
            }
        }
        catch (Exception ex)
        {
            LastActionResult = Format(shell.I18n.LogExportFailed, ex.Message);
        }
    }

    [RelayCommand]
    private void OpenDataDirectory()
    {
        OpenDirectory?.Invoke(DataDirectoryPath);
    }

    private static string Format(string template, params object[] values)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, values);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
