using System;

using Cafe.Launcher.Avalonia.Models;

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
    /// Show a toast that disappears after the tier configured for <paramref name="severity"/>.
    /// </summary>
    /// <param name="message">Text displayed by the toast.</param>
    /// <param name="severity">Severity that selects the icon, color, and default duration tier.</param>
    /// <param name="duration">Display duration tier; <see langword="null"/> uses the severity default.</param>
    public void Show(string message, ToastSeverity severity = ToastSeverity.Info, ToastDuration? duration = null) =>
        Show(new ToastOptions
        {
            Message = message,
            Severity = severity,
            Duration = duration
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
            Duration = options.Duration ?? ToastDurations.ForSeverity(options.Severity),
            CreatedAt = DateTimeOffset.Now,
            PrimaryAction = options.PrimaryAction,
            SecondaryAction = options.SecondaryAction
        });
    }

    public void ShowError(string message) => Show(message, ToastSeverity.Error);
    public void ShowSuccess(string message) => Show(message, ToastSeverity.Success);
    public void ShowWarning(string message) => Show(message, ToastSeverity.Warning);
}
