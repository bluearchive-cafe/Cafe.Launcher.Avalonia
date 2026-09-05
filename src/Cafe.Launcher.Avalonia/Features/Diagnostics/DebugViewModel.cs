using System;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Avalonia.Constants;
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
    private readonly IGameOperationActivity operations;
    private readonly IFilePickerService filePickerService;
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

    /// <summary>Initializes the debug overlay and observes game-operation state.</summary>
    public DebugViewModel(
        ToastService toastService,
        UnifiedLogger unifiedLogger,
        IErrorHandlingService errorHandling,
        LauncherSettingsService settingsService,
        IGameOperationActivity operations,
        ShellViewModel shell,
        IFilePickerService filePickerService,
        LogExportService? logExportService = null)
    {
        this.toastService = toastService;
        this.unifiedLogger = unifiedLogger;
        this.errorHandling = errorHandling;
        this.settingsService = settingsService;
        this.operations = operations;
        this.shell = shell;
        this.filePickerService = filePickerService;
        this.logExportService = logExportService;

        operations.ActivityPropertyChanged += OnOperationsPropertyChanged;
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
            shell.I18n[LocalizationKeys.DebugSystemInfoFormat],
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
        shell.I18n[LocalizationKeys.LogLevelVerbose],
        shell.I18n[LocalizationKeys.LogLevelDebug],
        shell.I18n[LocalizationKeys.LogLevelInformation],
        shell.I18n[LocalizationKeys.LogLevelWarning],
        shell.I18n[LocalizationKeys.LogLevelError],
        shell.I18n[LocalizationKeys.LogLevelFatal]
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
        LastActionResult = Format(shell.I18n[LocalizationKeys.DebugLogLevelSet], LogLevelDisplay);
    }

    // ── Log writes ───────────────────────────────────────────────────────

    [RelayCommand]
    private async Task WriteTestLog(string severity)
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
            "Verbose" => shell.I18n[LocalizationKeys.LogLevelVerbose],
            "Debug" => shell.I18n[LocalizationKeys.LogLevelDebug],
            "Info" => shell.I18n[LocalizationKeys.LogLevelInformation],
            "Warn" => shell.I18n[LocalizationKeys.LogLevelWarning],
            "Error" => shell.I18n[LocalizationKeys.LogLevelError],
            "Fatal" => shell.I18n[LocalizationKeys.LogLevelFatal],
            _ => shell.I18n[LocalizationKeys.LogLevelInformation]
        };
        var message = Format(shell.I18n[LocalizationKeys.DebugTestLogMessage], DateTimeOffset.Now.ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture));
        await unifiedLogger.LogAsync(sev, "DebugPanel", message: message);
        LastActionResult = Format(shell.I18n[LocalizationKeys.DebugLogEntryWritten], severityDisplay);
    }

    // ── Toast notifications ──────────────────────────────────────────────

    [RelayCommand]
    private void TestToast(string severity)
    {
        var severityDisplay = severity switch
        {
            "Info" => shell.I18n[LocalizationKeys.LogLevelInformation],
            "Success" => shell.I18n[LocalizationKeys.ToastSuccess],
            "Warning" => shell.I18n[LocalizationKeys.LogLevelWarning],
            "Error" => shell.I18n[LocalizationKeys.LogLevelError],
            _ => severity
        };
        var message = Format(
            shell.I18n[LocalizationKeys.DebugTestToastMessage],
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
        LastActionResult = Format(shell.I18n[LocalizationKeys.DebugToastShown], severityDisplay);
    }

    [RelayCommand]
    private void TestActionToast()
    {
        toastService.Show(new ToastOptions
        {
            Title = shell.I18n[LocalizationKeys.DebugActionToastTitle],
            Message = shell.I18n[LocalizationKeys.DebugActionToastMessage],
            Severity = ToastSeverity.Info,
            PrimaryAction = new ToastAction(
                shell.I18n[LocalizationKeys.DebugSimulateSuccess],
                _ => Task.FromResult(ToastActionResult.Success())),
            SecondaryAction = new ToastAction(
                shell.I18n[LocalizationKeys.DebugSimulateFailure],
                _ => Task.FromResult(ToastActionResult.Failure(
                    shell.I18n[LocalizationKeys.DebugActionFailureMessage],
                    shell.I18n[LocalizationKeys.DebugActionFailureTitle])))
        });
    }

    // ── Error dialog ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task TriggerErrorDialogAsync()
    {
        var exception = new InvalidOperationException(Format(
            shell.I18n[LocalizationKeys.DebugCriticalErrorMessage],
            DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture)));
        await errorHandling.HandleCriticalErrorAsync(
            "DebugPanel: test critical error", exception);
        LastActionResult = shell.I18n[LocalizationKeys.DebugCriticalErrorTriggered];
    }

    [RelayCommand]
    private async Task SimulateHandledErrorAsync()
    {
        var exception = new InvalidOperationException(Format(
            shell.I18n[LocalizationKeys.DebugHandledErrorMessage],
            DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture)));
        await errorHandling.HandleErrorAsync(
            "DebugPanel: test handled error", exception,
            new ErrorHandlingOptions
            {
                ToastMessage = exception.Message
            });
        LastActionResult = shell.I18n[LocalizationKeys.DebugHandledErrorSimulated];
    }

    // ── Game operations ──────────────────────────────────────────────────

    private void OnOperationsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IGameOperationActivity.IsDownloadRunning)
            or nameof(IGameOperationActivity.IsPaused))
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
        operations.ActivityPropertyChanged -= OnOperationsPropertyChanged;
    }

    private void RefreshDownloadStatus()
    {
        IsDownloadRunning = operations.IsDownloadRunning;
        IsDownloadPaused = operations.IsPaused;
        DownloadStatusText = IsDownloadRunning
            ? (IsDownloadPaused ? shell.I18n[LocalizationKeys.Paused] : shell.I18n[LocalizationKeys.Downloading])
            : shell.I18n[LocalizationKeys.DebugIdle];
    }

    [RelayCommand]
    private void TogglePauseResume()
    {
        if (!operations.CanPauseOperation)
        {
            return;
        }

        operations.PauseResume();
        LastActionResult = operations.IsPaused ? shell.I18n[LocalizationKeys.PauseRequested] : shell.I18n[LocalizationKeys.ResumeRequested];
    }

    [RelayCommand]
    private void StopDownload()
    {
        if (!operations.IsDownloadRunning)
        {
            return;
        }

        operations.StopOperation();
        LastActionResult = shell.I18n[LocalizationKeys.StopRequested];
    }

    [RelayCommand]
    private async Task RefreshStateAsync()
    {
        await AsyncEvent.InvokeSequentiallyAsync(RefreshRequested);
        LastActionResult = shell.I18n[LocalizationKeys.DebugStateRefreshTriggered];
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
            SettingsJsonDisplay = Format(shell.I18n[LocalizationKeys.DebugSettingsReadFailed], ex.Message);
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
        LastActionResult = shell.I18n[LocalizationKeys.DebugSettingsReset];
    }

    // ── File operations ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        if (logExportService is null)
        {
            LastActionResult = shell.I18n[LocalizationKeys.DebugLogExportUnavailable];
            return;
        }

        Directory.CreateDirectory(LauncherUserDataDirectory.Root);
        var dir = await filePickerService.PickFolderAsync(
            shell.I18n[LocalizationKeys.LogExportFolderPickerTitle],
            LauncherUserDataDirectory.Root);
        if (string.IsNullOrWhiteSpace(dir))
        {
            LastActionResult = shell.I18n[LocalizationKeys.DebugExportCancelled];
            return;
        }

        try
        {
            var zipPath = await logExportService.ExportAsync(dir);
            LastActionResult = Format(shell.I18n[LocalizationKeys.LogExportSucceeded], zipPath);

            // Open the containing folder
            var folder = Path.GetDirectoryName(zipPath);
            if (folder is not null)
            {
                ShellFolderOpener.OpenInFileManager(folder);
            }
        }
        catch (Exception ex)
        {
            LastActionResult = Format(shell.I18n[LocalizationKeys.LogExportFailed], ex.Message);
        }
    }

    [RelayCommand]
    private void OpenDataDirectory()
    {
        ShellFolderOpener.OpenInFileManager(DataDirectoryPath);
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
