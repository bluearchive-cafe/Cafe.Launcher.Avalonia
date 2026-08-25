using System;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Serilog.Events;

namespace Cafe.Launcher.Avalonia.Features.Diagnostics;

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

    /// <summary>Raised when the debug panel requests a shell refresh.</summary>
    public event Func<Task>? RefreshRequested;

    /// <summary>Raised after the user confirms that persisted settings should be reset.</summary>
    public event Func<Task>? ResetSettingsRequested;

    /// <summary>Raised when the debug panel needs the shell to present reset confirmation.</summary>
    public event Action? ResetSettingsConfirmationRequested;

    /// <summary>Gets or sets the action used to open a local directory.</summary>
    public Action<string>? OpenDirectory { get; set; }

    /// <summary>Gets or sets the picker used to select a directory for exported logs.</summary>
    public Func<string, Task<string?>>? PickExportDirectoryAsync { get; set; }

    /// <summary>Initializes the debug overlay and observes game-operation state.</summary>
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
            shell.I18n["debugSystemInfoFormat"],
            BuildInfo.LauncherVersion,
            BuildInfo.CommitSha,
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Environment.OSVersion,
            BuildInfo.BuildConfiguration,
            typeof(global::Avalonia.Application).Assembly.GetName().Version ?? new Version());
    }

    // ── Log level ────────────────────────────────────────────────────────

    /// <summary>Gets localized display names for the selectable diagnostic log levels.</summary>
    public string[] LogLevelOptions =>
    [
        shell.I18n["logLevelVerbose"],
        shell.I18n["logLevelDebug"],
        shell.I18n["logLevelInformation"],
        shell.I18n["logLevelWarning"],
        shell.I18n["logLevelError"],
        shell.I18n["logLevelFatal"]
    ];

    /// <summary>Refreshes debug-panel text after the active UI language changes.</summary>
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
        LastActionResult = Format(shell.I18n["debugLogLevelSet"], LogLevelDisplay);
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
            "Verbose" => shell.I18n["logLevelVerbose"],
            "Debug" => shell.I18n["logLevelDebug"],
            "Info" => shell.I18n["logLevelInformation"],
            "Warn" => shell.I18n["logLevelWarning"],
            "Error" => shell.I18n["logLevelError"],
            "Fatal" => shell.I18n["logLevelFatal"],
            _ => shell.I18n["logLevelInformation"]
        };
        var message = Format(shell.I18n["debugTestLogMessage"], DateTimeOffset.Now.ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture));
        LocalDiagnostics.LogSync(sev, "DebugPanel", message);
        LastActionResult = Format(shell.I18n["debugLogEntryWritten"], severityDisplay);
    }

    // ── Toast notifications ──────────────────────────────────────────────

    [RelayCommand]
    private void TestToast(string severity)
    {
        var severityDisplay = severity switch
        {
            "Info" => shell.I18n["logLevelInformation"],
            "Success" => shell.I18n["toastSuccess"],
            "Warning" => shell.I18n["logLevelWarning"],
            "Error" => shell.I18n["logLevelError"],
            _ => severity
        };
        var message = Format(
            shell.I18n["debugTestToastMessage"],
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
        LastActionResult = Format(shell.I18n["debugToastShown"], severityDisplay);
    }

    [RelayCommand]
    private void TestActionToast()
    {
        toastService.Show(new ToastOptions
        {
            Title = shell.I18n["debugActionToastTitle"],
            Message = shell.I18n["debugActionToastMessage"],
            Severity = ToastSeverity.Info,
            PrimaryAction = new ToastAction(
                shell.I18n["debugSimulateSuccess"],
                _ => Task.FromResult(ToastActionResult.Success())),
            SecondaryAction = new ToastAction(
                shell.I18n["debugSimulateFailure"],
                _ => Task.FromResult(ToastActionResult.Failure(
                    shell.I18n["debugActionFailureMessage"],
                    shell.I18n["debugActionFailureTitle"])))
        });
    }

    // ── Error dialog ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task TriggerErrorDialogAsync()
    {
        var exception = new InvalidOperationException(Format(
            shell.I18n["debugCriticalErrorMessage"],
            DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture)));
        await errorHandling.HandleCriticalErrorAsync(
            "DebugPanel: test critical error", exception);
        LastActionResult = shell.I18n["debugCriticalErrorTriggered"];
    }

    [RelayCommand]
    private async Task SimulateHandledErrorAsync()
    {
        var exception = new InvalidOperationException(Format(
            shell.I18n["debugHandledErrorMessage"],
            DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture)));
        await errorHandling.HandleErrorAsync(
            "DebugPanel: test handled error", exception,
            new ErrorHandlingOptions
            {
                ToastMessage = exception.Message
            });
        LastActionResult = shell.I18n["debugHandledErrorSimulated"];
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

    /// <summary>Stops observing game-operation state.</summary>
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
            ? (IsDownloadPaused ? shell.I18n["paused"] : shell.I18n["downloading"])
            : shell.I18n["debugIdle"];
    }

    [RelayCommand]
    private void TogglePauseResume()
    {
        if (!operations.CanPauseOperation)
        {
            return;
        }

        operations.PauseResumeCommand.Execute(null);
        LastActionResult = operations.IsPaused ? shell.I18n["pauseRequested"] : shell.I18n["resumeRequested"];
    }

    [RelayCommand]
    private void StopDownload()
    {
        if (!operations.IsDownloadRunning)
        {
            return;
        }

        operations.StopOperationCommand.Execute(null);
        LastActionResult = shell.I18n["stopRequested"];
    }

    [RelayCommand]
    private async Task RefreshStateAsync()
    {
        await AsyncEvent.InvokeSequentiallyAsync(RefreshRequested);
        LastActionResult = shell.I18n["debugStateRefreshTriggered"];
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
            SettingsJsonDisplay = Format(shell.I18n["debugSettingsReadFailed"], ex.Message);
        }
    }

    [RelayCommand]
    private void ResetSettings()
    {
        ResetSettingsConfirmationRequested?.Invoke();
    }

    /// <summary>Runs the reset callback after the shell has confirmed the destructive action.</summary>
    public async Task ConfirmResetSettingsAsync()
    {
        if (ResetSettingsRequested is null)
        {
            return;
        }

        await AsyncEvent.InvokeSequentiallyAsync(ResetSettingsRequested);
        await RefreshSettingsDisplayAsync();
        LastActionResult = shell.I18n["debugSettingsReset"];
    }

    // ── File operations ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        if (logExportService is null || PickExportDirectoryAsync is null)
        {
            LastActionResult = shell.I18n["debugLogExportUnavailable"];
            return;
        }

        var dir = await PickExportDirectoryAsync(LauncherUserDataDirectory.Root);
        if (string.IsNullOrWhiteSpace(dir))
        {
            LastActionResult = shell.I18n["debugExportCancelled"];
            return;
        }

        try
        {
            var zipPath = await logExportService.ExportAsync(dir);
            LastActionResult = Format(shell.I18n["logExportSucceeded"], zipPath);

            // Open the containing folder
            var folder = Path.GetDirectoryName(zipPath);
            if (folder is not null)
            {
                OpenDirectory?.Invoke(folder);
            }
        }
        catch (Exception ex)
        {
            LastActionResult = Format(shell.I18n["logExportFailed"], ex.Message);
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
