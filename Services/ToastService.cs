using System;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Centralized service for showing transient toast notifications.
/// Subscribers (e.g., the main ViewModel) listen for ToastRaised events to display toasts in the UI.
/// </summary>
public sealed class ToastService
{
    /// <summary>
    /// Raised whenever a toast notification should be displayed.
    /// </summary>
    public event Action<ToastNotification>? ToastRaised;

    /// <summary>
    /// Show an informational toast that auto-dismisses after the default duration.
    /// </summary>
    public void Show(string message, ToastSeverity severity = ToastSeverity.Info, int durationMs = 4000) =>
        Show(new ToastOptions
        {
            Message = message,
            Severity = severity,
            DurationMs = durationMs
        });

    /// <summary>
    /// Show a toast using structured content and optional actions.
    /// </summary>
    public void Show(ToastOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ToastRaised?.Invoke(new ToastNotification
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = options.Title,
            Message = options.Message,
            Severity = options.Severity,
            DurationMs = options.DurationMs,
            CreatedAt = DateTimeOffset.Now,
            PrimaryAction = options.PrimaryAction,
            SecondaryAction = options.SecondaryAction
        });
    }

    public void ShowError(string message) => Show(message, ToastSeverity.Error, 8000);
    public void ShowSuccess(string message) => Show(message, ToastSeverity.Success, 4000);
    public void ShowWarning(string message) => Show(message, ToastSeverity.Warning, 6000);
}
