using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.Services;

/// <param name="Timeout">Maximum duration for <see cref="ExecuteAsync"/> before the action is
/// automatically cancelled. The toast itself is NOT dismissed after this timeout — cancellation
/// falls through to the failure path, keeping the toast visible with an error state.</param>

public sealed record ToastAction(
    string Label,
    Func<CancellationToken, Task<ToastActionResult>> ExecuteAsync,
    TimeSpan? Timeout = null);

public sealed record ToastActionResult
{
    private ToastActionResult(bool isSuccess, string? message, string? title)
    {
        IsSuccess = isSuccess;
        Message = message;
        Title = title;
    }

    public bool IsSuccess { get; }
    public string? Message { get; }
    public string? Title { get; }

    public static ToastActionResult Success() => new(true, null, null);

    public static ToastActionResult Failure(string message, string? title = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new ToastActionResult(false, message, title);
    }
}

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
    private double autoDismissProgress = 100d;

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
    public bool HasAutoDismissProgress => !HasActions && DurationMs > 0;
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
