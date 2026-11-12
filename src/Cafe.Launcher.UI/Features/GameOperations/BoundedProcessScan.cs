using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>
/// 把一次进程枚举丢到线程池，再与一个带令牌的限时等待赛跑：谁先到算谁的。扫描正文是
/// 同步枚举（<see cref="Cafe.Launcher.Core.Services.GameRuntime.ProcessService.FindRunningExeNamesAsync"/>），
/// 令牌只在开头看一眼；直接 await 它，反作弊把枚举挂住时会钉死调用线程（闸门在 UI 线程、
/// 看护在退出事件线程）。闸门（<see cref="RunningGameGate"/>）与会话看护
/// （<see cref="GameSessionMonitor"/>）共用这一份，两处对「没有答案」的取舍因此同源。
/// </summary>
internal static class BoundedProcessScan
{
    /// <summary>
    /// 返回枚举结果；限时内没有答案时返回 <c>null</c>（未知，调用方不得据此判定进程缺席）。
    /// 取消优先于超时展开，按 <see cref="OperationCanceledException"/> 抛出。被落下的扫描
    /// 接一个观察续体，迟到的失败不会变成未观察异常。
    /// </summary>
    internal static async Task<IReadOnlyList<string>?> RunAsync(
        Func<CancellationToken, Task<IReadOnlyList<string>>> scan,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var scanTask = Task.Run(() => scan(cancellationToken), CancellationToken.None);
        var timeoutTask = Task.Delay(timeout, cancellationToken);
        if (ReferenceEquals(await Task.WhenAny(scanTask, timeoutTask).ConfigureAwait(false), scanTask))
        {
            return await scanTask.ConfigureAwait(false);
        }

        _ = scanTask.ContinueWith(
            static abandoned => _ = abandoned.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }
}
