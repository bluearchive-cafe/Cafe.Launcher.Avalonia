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

    public string IconColor => Severity switch
    {
        ToastSeverity.Success => "#22C55E",
        ToastSeverity.Warning => "#F59E0B",
        ToastSeverity.Error => "#E5484D",
        _ => "#2E7DF6"
    };

    public string SeverityLabel => Severity switch
    {
        ToastSeverity.Success => "Success",
        ToastSeverity.Warning => "Warning",
        ToastSeverity.Error => "Error",
        _ => "Info"
    };

    // Static brushes with colors that are visible on both light and dark backgrounds.
    // Theme-aware resource lookup would be ideal but Avalonia 12 Application-level
    // resource resolution does not expose a public TryFindResource API directly.
    private static readonly global::Avalonia.Media.SolidColorBrush InfoBrush = new(0xFF2E7DF6);
    private static readonly global::Avalonia.Media.SolidColorBrush SuccessBrush = new(0xFF22C55E);
    private static readonly global::Avalonia.Media.SolidColorBrush WarningBrush = new(0xFFF59E0B);
    private static readonly global::Avalonia.Media.SolidColorBrush ErrorBrush = new(0xFFE5484D);

    public global::Avalonia.Media.IBrush IconBrush => Severity switch
    {
        ToastSeverity.Success => SuccessBrush,
        ToastSeverity.Warning => WarningBrush,
        ToastSeverity.Error => ErrorBrush,
        _ => InfoBrush
    };
}

public enum ToastSeverity
{
    Info,
    Success,
    Warning,
    Error
}
