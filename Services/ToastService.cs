using System;
using System.Collections.Generic;

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
    public void Show(string message, ToastSeverity severity = ToastSeverity.Info, int durationMs = 4000)
    {
        ToastRaised?.Invoke(new ToastNotification
        {
            Id = Guid.NewGuid().ToString("N"),
            Message = message,
            Severity = severity,
            DurationMs = durationMs,
            CreatedAt = DateTimeOffset.Now
        });
    }

    public void ShowError(string message) => Show(message, ToastSeverity.Error, 8000);
    public void ShowSuccess(string message) => Show(message, ToastSeverity.Success, 4000);
    public void ShowWarning(string message) => Show(message, ToastSeverity.Warning, 6000);
}

/// <summary>
/// Represents a single toast notification to be displayed in the UI.
/// </summary>
public sealed class ToastNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Message { get; set; } = "";
    public ToastSeverity Severity { get; set; } = ToastSeverity.Info;
    public int DurationMs { get; set; } = 4000;
    public DateTimeOffset CreatedAt { get; set; }

    public string IconKind => Severity switch
    {
        ToastSeverity.Success => "CheckCircle",
        ToastSeverity.Warning => "AlertOutline",
        ToastSeverity.Error => "AlertCircle",
        _ => "InformationOutline"
    };

    public string SeverityLabel { get; set; } = "";
}

public enum ToastSeverity
{
    Info,
    Success,
    Warning,
    Error
}
