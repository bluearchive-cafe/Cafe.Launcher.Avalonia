using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;

namespace Cafe.Launcher.UI.Models;

/// <summary>Describes an asynchronous action presented by a toast notification.</summary>
/// <param name="Label">Localized label displayed for the action.</param>
/// <param name="ExecuteAsync">Operation executed when the user selects the action.</param>
/// <param name="Timeout">Maximum duration for <paramref name="ExecuteAsync"/> before cancellation.</param>
internal sealed record ToastAction(
    string Label,
    Func<CancellationToken, Task<ToastActionResult>> ExecuteAsync,
    TimeSpan? Timeout = null);

/// <summary>Represents the outcome of a toast action execution.</summary>
internal sealed record ToastActionResult
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
internal sealed class ToastOptions
{
    public string? Title { get; init; }
    public required string Message { get; init; }
    public ToastSeverity Severity { get; init; } = ToastSeverity.Info;

    /// <summary>
    /// Gets the optional key that merges this notification into an already-shown card with the
    /// same key (a per-occurrence count, per [ADR-070](../../docs/design/adr/ADR-070-对话框设置与通知统一为实心Fluent表面.md)).
    /// Leave it <see langword="null"/> to always add a new card.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>
    /// Gets the delivery policy of this notification. This batch only defines the in-app tier;
    /// system-notification tiers are added with that capability without touching call sites.
    /// </summary>
    public ToastPolicy Policy { get; init; } = ToastPolicy.InApp;

    /// <summary>
    /// Gets the tier that decides how long the toast stays on screen before it disappears.
    /// Leave it <see langword="null"/> for the severity's tier from <see cref="ToastDurations"/>.
    /// </summary>
    public ToastDuration? Duration { get; init; }

    public ToastAction? PrimaryAction { get; init; }
    public ToastAction? SecondaryAction { get; init; }
}

/// <summary>
/// Represents a single toast notification to be displayed in the UI.
/// </summary>
internal sealed partial class ToastNotification : ObservableObject
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
    [NotifyPropertyChangedFor(
        nameof(FluentSeverity),
        nameof(IsInformational),
        nameof(IsSuccess),
        nameof(IsWarning),
        nameof(IsError))]
    private ToastSeverity severity = ToastSeverity.Info;

    [ObservableProperty]
    private string severityLabel = "";

    /// <summary>同 key 合并进本卡的次数；1 表示没有发生过合并。</summary>
    [ObservableProperty]
    private int mergeCount = 1;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Gets the optional key that merges same-key notifications into this card.</summary>
    public string? Key { get; set; }

    /// <summary>Gets the delivery policy reserved for the system-notification tier switch.</summary>
    public ToastPolicy Policy { get; set; } = ToastPolicy.InApp;

    /// <summary>Gets the tier that decides how long the toast stays on screen.</summary>
    public ToastDuration Duration { get; set; } = ToastDuration.Brief;
    public DateTimeOffset CreatedAt { get; set; }
    public ToastAction? PrimaryAction { get; init; }
    public ToastAction? SecondaryAction { get; init; }
    public bool HasPrimaryAction => PrimaryAction is not null;
    public bool HasSecondaryAction => SecondaryAction is not null;
    public bool HasActions => HasPrimaryAction || HasSecondaryAction;
    public string PrimaryActionLabel => PrimaryAction?.Label ?? "";
    public string SecondaryActionLabel => SecondaryAction?.Label ?? "";

    public FAInfoBarSeverity FluentSeverity => Severity switch
    {
        ToastSeverity.Success => FAInfoBarSeverity.Success,
        ToastSeverity.Warning => FAInfoBarSeverity.Warning,
        ToastSeverity.Error => FAInfoBarSeverity.Error,
        _ => FAInfoBarSeverity.Informational
    };

    /// <summary>严重级条件类（ADR-070 同色描边）：卡片外框按档位选用语义底色收边。</summary>
    public bool IsInformational => Severity == ToastSeverity.Info;
    public bool IsSuccess => Severity == ToastSeverity.Success;
    public bool IsWarning => Severity == ToastSeverity.Warning;
    public bool IsError => Severity == ToastSeverity.Error;
}

public enum ToastSeverity
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// 通知的呈现档位。本轮只定义应用内栈；Windows 系统通知档位随该能力引入时在此追加，
/// 调用点与 <see cref="Services.INotificationService"/> 接缝不变（ADR-070）。
/// </summary>
internal enum ToastPolicy
{
    /// <summary>应用内右上角通知栈；当前唯一档位。</summary>
    InApp
}
