using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>Standardized options for non-critical error handling.</summary>
public sealed class ErrorHandlingOptions
{
    /// <summary>Custom operation message shown before the exception details in the toast.</summary>
    public string? ToastMessage { get; init; }

    /// <summary>Whether to show a toast notification. Defaults to true.</summary>
    public bool ShowToast { get; init; } = true;

    /// <summary>
    /// Localization key for <see cref="ShellViewModel.OperationNote"/>.
    /// When null, OperationNote is not changed.
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
    /// Logs the error, optionally shows a toast, and optionally sets <see cref="ShellViewModel.OperationNote"/>.
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
}

public sealed class ErrorHandlingService : IErrorHandlingService
{
    private readonly LocalizationService localizer;
    private readonly LocalDiagnostics diagnostics;
    private readonly ToastService toastService;
    private readonly ShellViewModel shell;

    public ErrorHandlingService(
        LocalizationService localizer,
        LocalDiagnostics diagnostics,
        ToastService toastService,
        ShellViewModel shell)
    {
        this.localizer = localizer;
        this.diagnostics = diagnostics;
        this.toastService = toastService;
        this.shell = shell;
    }

    public event Action<CriticalErrorInfo>? CriticalErrorRequested;

    public async Task HandleErrorAsync(string context, Exception exception, ErrorHandlingOptions? options = null)
    {
        options ??= new ErrorHandlingOptions();

        await diagnostics.ErrorAsync(context, exception, CancellationToken.None);

        if (options.OperationNoteKey is { } key)
        {
            shell.OperationNote = localizer.F(key, exception.Message);
        }

        if (options.ShowToast)
        {
            toastService.ShowError(FormatToastMessage(options.ToastMessage ?? context, exception));
        }
    }

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

    public async Task HandleCriticalErrorAsync(string context, Exception exception)
    {
        await diagnostics.ErrorAsync(context, exception, CancellationToken.None);

        CriticalErrorRequested?.Invoke(new CriticalErrorInfo
        {
            Context = context,
            Message = exception.Message,
            Details = $"{context}{Environment.NewLine}{exception}"
        });
    }
}
