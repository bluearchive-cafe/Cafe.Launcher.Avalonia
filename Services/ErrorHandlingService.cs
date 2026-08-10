using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>Standardized options for non-critical error handling.</summary>
public sealed class ErrorHandlingOptions
{
    /// <summary>Custom operation message shown before the exception details in the toast.</summary>
    public string? ToastMessage { get; init; }

    /// <summary>Whether to show a toast notification. Defaults to true.</summary>
    public bool ShowToast { get; init; } = true;

    /// <summary>
    /// Localization key for <see cref="IErrorHandlingService.OperationNoteRequested"/>.
    /// When null, the event is not raised.
    /// </summary>
    public string? OperationNoteKey { get; init; }
}

/// <summary>Payload for the critical-error dialog requested event.</summary>
public sealed class CriticalErrorInfo
{
    public string Context { get; init; } = "";
    public string Message { get; init; } = "";
    public string Details { get; init; } = "";
}

/// <summary>
/// Centralized error handling for ViewModel-layer exceptions.
/// Combines diagnostic logging, toast notification, and inline status update
/// so call sites no longer hand-roll the triad.
/// </summary>
public interface IErrorHandlingService
{
    /// <summary>
    /// Logs the error, optionally shows a toast, and optionally raises <see cref="OperationNoteRequested"/>.
    /// Use for recoverable failures where the user can continue.
    /// </summary>
    Task HandleErrorAsync(string context, Exception exception, ErrorHandlingOptions? options = null);

    /// <summary>
    /// Logs the error and raises <see cref="CriticalErrorRequested"/> so the shell
    /// can present a modal error dialog. Does NOT show a toast or set OperationNote.
    /// </summary>
    Task HandleCriticalErrorAsync(string context, Exception exception);

    /// <summary>Raised when a critical error requires a modal dialog.</summary>
    event Action<CriticalErrorInfo>? CriticalErrorRequested;

    /// <summary>Raised when a recoverable error requests an operation note update.</summary>
    event Action<string>? OperationNoteRequested;
}

/// <summary>
/// Implements recoverable and critical error handling with local diagnostics and shell notifications.
/// </summary>
public sealed class ErrorHandlingService : IErrorHandlingService
{
    private readonly LocalizationService localizer;
    private readonly LocalDiagnostics diagnostics;
    private readonly ToastService toastService;

    /// <summary>Initializes the service with its localization, diagnostics, and toast collaborators.</summary>
    public ErrorHandlingService(
        LocalizationService localizer,
        LocalDiagnostics diagnostics,
        ToastService toastService)
    {
        this.localizer = localizer;
        this.diagnostics = diagnostics;
        this.toastService = toastService;
    }

    /// <summary>Raised when a critical failure must be presented in a modal dialog.</summary>
    public event Action<CriticalErrorInfo>? CriticalErrorRequested;

    /// <summary>Raised when a recoverable error requests an operation note update.</summary>
    public event Action<string>? OperationNoteRequested;

    /// <summary>Logs a recoverable failure and applies its requested shell notification behavior.</summary>
    public async Task HandleErrorAsync(string context, Exception exception, ErrorHandlingOptions? options = null)
    {
        options ??= new ErrorHandlingOptions();

        await diagnostics.ErrorAsync("ErrorHandling", exception, CancellationToken.None);

        if (options.OperationNoteKey is { } key)
        {
            OperationNoteRequested?.Invoke(localizer.F(key, exception.Message));
        }

        if (options.ShowToast)
        {
            toastService.ShowError(FormatToastMessage(options.ToastMessage ?? context, exception));
        }
    }

    /// <summary>Formats a user-safe exception summary without stack traces or source locations.</summary>
    internal static string FormatToastMessage(string? operationMessage, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var message = new StringBuilder(operationMessage?.Trim());
        var isFirstException = true;
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var exceptionType = current.GetType().Name;
            var exceptionDetail = string.IsNullOrWhiteSpace(current.Message)
                ? exceptionType
                : $"{exceptionType}：{current.Message}";

            if (isFirstException && message.Length > 0)
            {
                message.Append('（').Append(exceptionType).Append('）');
                if (!string.IsNullOrWhiteSpace(current.Message))
                {
                    message.Append('：').Append(current.Message);
                }
            }
            else if (isFirstException)
            {
                message.Append(exceptionDetail);
            }
            else
            {
                message.Append(" → ").Append(exceptionDetail);
            }

            isFirstException = false;
        }

        return message.ToString();
    }

    /// <summary>Logs a critical failure and requests that the shell show its modal error dialog.</summary>
    public async Task HandleCriticalErrorAsync(string context, Exception exception)
    {
        await diagnostics.ErrorAsync("ErrorHandling", exception, CancellationToken.None);

        CriticalErrorRequested?.Invoke(new CriticalErrorInfo
        {
            Context = context,
            Message = exception.Message,
            Details = $"{context}{Environment.NewLine}{exception}"
        });
    }
}
