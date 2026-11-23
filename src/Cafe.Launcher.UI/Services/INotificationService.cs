using Cafe.Launcher.UI.Models;

namespace Cafe.Launcher.UI.Services;

/// <summary>
/// 通知的窄接缝：调用方只声明「要通知什么」，不绑定应用内呈现（ADR-070）。
/// 本轮只有应用内栈一个实现；将来的 Windows 系统通知以新实现接入本接口，
/// 调用点与签名保持不变。
/// </summary>
/// <remarks>
/// 应用内呈现端（<c>ToastHostViewModel</c>）订阅的是实现类 <see cref="ToastService"/> 的
/// <see cref="ToastService.ToastRaised"/> 事件，不经本接口——那是呈现内部管线，不是调用方契约。
/// </remarks>
internal interface INotificationService
{
    /// <summary>
    /// Show a toast that disappears after the tier configured for <paramref name="severity"/>.
    /// </summary>
    /// <param name="message">Text displayed by the toast.</param>
    /// <param name="severity">Severity that selects the icon, color, and default duration tier.</param>
    /// <param name="duration">Display duration tier; <see langword="null"/> uses the severity default.</param>
    void Show(string message, ToastSeverity severity = ToastSeverity.Info, ToastDuration? duration = null);

    /// <summary>
    /// Show a toast using structured content, an optional merge key, and optional actions.
    /// </summary>
    void Show(ToastOptions options);

    void ShowError(string message);
    void ShowSuccess(string message);
    void ShowWarning(string message);
}
