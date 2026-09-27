namespace Cafe.Launcher.Core.Services.Diagnostics;

/// <summary>
/// 静态诊断入口：给 **没有注入缝的调用点** 用——pre-DI 阶段（进程启动、跨进程信号）与纯静态
/// 帮助类。实例调用点一律走 <see cref="ILauncherDiagnostics"/>；本类只转发到共享日志器，
/// 不做任何加工，因此它是「谁都能调」的唯一残余静态写入口，改动它要同时看
/// <c>DiagnosticsStaticSealTests</c> 的声明表。
/// </summary>
public static class LauncherLog
{
    /// <summary>
    /// 独立诊断实例：写进进程临时目录，**不**改绑共享静态缝。给测试与静态工厂用——
    /// 它们需要一个不依赖 DI 的 <see cref="ILauncherDiagnostics"/>，而不该自己 new 实现类型。
    /// </summary>
    public static ILauncherDiagnostics CreateDetached() => new LocalDiagnostics();
    /// <summary>信息级同步写入。</summary>
    public static void LogSync(string title, string message) =>
        LocalDiagnostics.LogSync(title, message);

    /// <summary>指定级别的同步写入。</summary>
    public static void LogSync(LogEntrySeverity severity, string title, string? message = null) =>
        LocalDiagnostics.LogSync(severity, title, message);

    /// <summary>指定级别的异步写入。</summary>
    public static System.Threading.Tasks.Task LogAsync(
        LogEntrySeverity severity,
        string title,
        string? message = null) =>
        LocalDiagnostics.LogAsync(severity, title, message);
}
