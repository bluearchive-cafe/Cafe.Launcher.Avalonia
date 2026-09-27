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
}
