using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Core.Services.Diagnostics;

/// <summary>
/// Core-side diagnostic seam. Presentation and process hosts adapt their logging implementation
/// to this narrow contract; Core never needs to know about a UI or logger implementation.
/// </summary>
public interface ILauncherDiagnostics
{
    Task DebugAsync(
        string title,
        string? message = null,
        CancellationToken cancellationToken = default);

    Task ErrorAsync(
        string title,
        string? message,
        Exception exception,
        CancellationToken cancellationToken = default);

    /// <summary>信息级诊断：可恢复但需要留痕的事件（下载校验失败、文件损坏等）。</summary>
    Task MessageAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 同步诊断：调用点无法等待时使用（例如在属性读取里解析系统代理）。
    /// 名字刻意不同于实现类的静态 <c>LogSync</c>——同名同参会与静态重载冲突（CS0111）。
    /// </summary>
    void LogMessage(LogEntrySeverity severity, string title, string? message = null);

    /// <summary>详细级诊断：只在诊断面板开 Verbose 时才落盘。</summary>
    Task VerboseAsync(
        string title,
        string? message = null,
        CancellationToken cancellationToken = default);

    /// <summary>错误级诊断，异常即消息。</summary>
    Task ErrorAsync(
        string title,
        Exception exception,
        CancellationToken cancellationToken = default);

    /// <summary>致命级诊断：进程即将结束（崩溃处理器）。</summary>
    Task FatalAsync(
        string title,
        Exception exception,
        CancellationToken cancellationToken = default);
    /// <summary>警告级诊断：可继续、但用户可见地降级了的行为。</summary>
    Task WarningAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>当前统一日志文件路径（日志查看器与导出面板要展示/打包它）。</summary>
    string LogFilePath { get; }

    /// <summary>当前最低记录级别。</summary>
    LogEntrySeverity MinimumLevel { get; }

    /// <summary>设置最低记录级别（诊断面板的级别选择）。</summary>
    void SetMinimumLevel(LogEntrySeverity severity);
}
