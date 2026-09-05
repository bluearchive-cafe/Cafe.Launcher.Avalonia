using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.Models;

/// <summary>Describes an asynchronous action presented by a toast notification.</summary>
/// <param name="Label">Localized label displayed for the action.</param>
/// <param name="ExecuteAsync">Operation executed when the user selects the action.</param>
/// <param name="Timeout">Maximum duration for <paramref name="ExecuteAsync"/> before cancellation.</param>
public sealed record ToastAction(
    string Label,
    Func<CancellationToken, Task<ToastActionResult>> ExecuteAsync,
    TimeSpan? Timeout = null);

/// <summary>Represents the outcome of a toast action execution.</summary>
public sealed record ToastActionResult
{
    private ToastActionResult(bool isSuccess, string? message, string? title)
    {
        IsSuccess = isSuccess;
        Message = message;
        Title = title;
    }

    /// <summary>Gets whether the action completed successfully.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets the user-safe failure message when the action did not succeed.</summary>
    public string? Message { get; }

    /// <summary>Gets the optional title that replaces the toast title after a failure.</summary>
    public string? Title { get; }

    /// <summary>Creates a successful action result.</summary>
    public static ToastActionResult Success() => new(true, null, null);

    /// <summary>Creates a failed action result with a user-safe message.</summary>
    public static ToastActionResult Failure(string message, string? title = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new ToastActionResult(false, message, title);
    }
}

/// <summary>Configures the content, severity, lifetime, and actions of a toast notification.</summary>
public sealed class ToastOptions
{
    public string? Title { get; init; }
    public required string Message { get; init; }
    public ToastSeverity Severity { get; init; } = ToastSeverity.Info;
    public int DurationMs { get; init; } = 4000;
    public ToastAction? PrimaryAction { get; init; }
    public ToastAction? SecondaryAction { get; init; }
}

/// <summary>
/// Represents a single toast notification to be displayed in the UI.
/// </summary>
public sealed partial class ToastNotification : ObservableObject
{
    [ObservableProperty]
    private bool isExiting;

    [ObservableProperty]
    private bool isActionExecuting;

    [ObservableProperty]
    private string? title;

    [ObservableProperty]
    private string message = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    private ToastSeverity severity = ToastSeverity.Info;

    [ObservableProperty]
    private string severityLabel = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int DurationMs { get; set; } = 4000;
    public DateTimeOffset CreatedAt { get; set; }
    public ToastAction? PrimaryAction { get; init; }
    public ToastAction? SecondaryAction { get; init; }
    public bool HasPrimaryAction => PrimaryAction is not null;
    public bool HasSecondaryAction => SecondaryAction is not null;
    public bool HasActions => HasPrimaryAction || HasSecondaryAction;
    public string PrimaryActionLabel => PrimaryAction?.Label ?? "";
    public string SecondaryActionLabel => SecondaryAction?.Label ?? "";

    public string IconKind => Severity switch
    {
        ToastSeverity.Success => "CheckCircle",
        ToastSeverity.Warning => "AlertOutline",
        ToastSeverity.Error => "AlertCircle",
        _ => "InformationOutline"
    };
}

public enum ToastSeverity
{
    Info,
    Success,
    Warning,
    Error
}
