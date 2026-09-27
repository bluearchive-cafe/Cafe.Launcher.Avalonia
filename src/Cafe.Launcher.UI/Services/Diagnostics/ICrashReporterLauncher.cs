namespace Cafe.Launcher.UI.Services.Diagnostics;

/// <summary>
/// 拉起独立崩溃报告进程的窄接缝。接口与消费者（<c>FatalCrashService</c>、崩溃报告窗口）一起
/// 留在表现层，实现落在宿主——它要用宿主自己的可执行文件与崩溃报告参数，是进程入口的知识。
/// </summary>
public interface ICrashReporterLauncher
{
    bool TryLaunch(string snapshotPath);
}
