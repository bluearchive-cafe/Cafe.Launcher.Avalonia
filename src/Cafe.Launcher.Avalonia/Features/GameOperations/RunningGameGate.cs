using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// 「游戏是不是在跑」这道闸门的共用正文（ADR-032）：卸载（预检与删除前复查）与
/// 下载／安装／修复（计划阶段与写入边界复查）消费同一份——判据为空不拦、扫描走进程
/// 名家族，命中时报出的名字由 <see cref="GameProcessNames.DescribeForDisplay"/> 统一
/// （名字补回 .exe，报的是实际在跑的那几个，而不是只报配置里的宿主），两条路径的判据
/// 与报法因此不会分叉。返回 null 表示放行。<paramref name="runningMessageKey"/> 由
/// 调用方引用编译期常量——下载要改文件，说「请先关闭」；卸载只是陈述在跑，按各自场景取词。
/// </summary>
internal static class RunningGameGate
{
    /// <summary>
    /// 闸门等扫描回答的时限。正常一次全系统进程枚举远不到一秒；限时只拦「枚举本身没有
    /// 答案」的那一档——游戏运行时反作弊内核组件在场，快照查询可能长期不返回，而扫描
    /// 正文是同步枚举、令牌拦不住它，不设限时会话就停在首个进度之前（界面停在
    /// 「正在生成文件列表」，取消也无人观察）。
    /// </summary>
    internal static readonly TimeSpan DefaultScanTimeout = TimeSpan.FromSeconds(5);

    public static async Task<GameOperationResult?> FindFailureAsync(
        IGameProcessTracker gameProcessTracker,
        LocalizationService localizer,
        string runningMessageKey,
        IReadOnlyList<string> knownProcessNames,
        CancellationToken cancellationToken,
        TimeSpan? scanTimeout = null)
    {
        if (knownProcessNames.Count == 0)
        {
            return null;
        }

        var scan = await ScanRunningProcessesAsync(
            gameProcessTracker,
            knownProcessNames,
            scanTimeout ?? DefaultScanTimeout,
            cancellationToken).ConfigureAwait(false);
        if (scan.TimedOut)
        {
            // 与「枚举抛异常按没在跑放行」（ADR-032 决策 6）相反：枚举**没有答案**时拒绝。
            // 异常是确定的读取失败、与游戏是否在跑无关；答不出来偏偏与游戏在跑相关——
            // 正是反作弊在场才挂得住枚举。闸门防的是在跑时改文件，「可能还在跑」时按
            // 拒绝处理是安全分支，而「请关闭游戏后重试」在两个世界里都是可动作的指引。
            return GameOperationOutcomes.Failed(
                localizer.T(LocalizationKeys.GameProcessScanUnavailable),
                GameOperationErrorCode.GameRunning);
        }

        return scan.RunningNames.Count == 0
            ? null
            : GameOperationOutcomes.Failed(
                localizer.F(runningMessageKey, GameProcessNames.DescribeForDisplay(scan.RunningNames)),
                GameOperationErrorCode.GameRunning);
    }

    /// <summary>
    /// 把扫描丢到线程池，与一个带令牌的限时等待赛跑：谁先到算谁的。扫描正文是同步枚举
    /// （<see cref="ProcessService.FindRunningExeNamesAsync"/>），令牌只在开头看一眼，
    /// 直接 await 它，反作弊把枚举挂住时取消就永远无人观察。用户停止从限时等待这一侧
    /// 立即浮出；被落下的扫描任务接一个观察续体，迟到的失败不会变成未观察异常。
    /// </summary>
    private static async Task<RunningProcessScan> ScanRunningProcessesAsync(
        IGameProcessTracker gameProcessTracker,
        IReadOnlyList<string> knownProcessNames,
        TimeSpan scanTimeout,
        CancellationToken cancellationToken)
    {
        var scanTask = Task.Run(
            () => gameProcessTracker.FindRunningGameProcessesAsync(knownProcessNames, cancellationToken),
            CancellationToken.None);
        var timeoutTask = Task.Delay(scanTimeout, cancellationToken);
        if (ReferenceEquals(await Task.WhenAny(scanTask, timeoutTask).ConfigureAwait(false), scanTask))
        {
            return new RunningProcessScan(await scanTask.ConfigureAwait(false), TimedOut: false);
        }

        ObserveAbandonedScan(scanTask);
        // 取消优先于超时判定：两者都可能让 Delay 先完成，而取消必须按用户停止展开，
        // 不能被「无法确认」的错误结果吃掉。
        cancellationToken.ThrowIfCancellationRequested();
        return new RunningProcessScan([], TimedOut: true);
    }

    private static void ObserveAbandonedScan(Task<IReadOnlyList<string>> scanTask) =>
        _ = scanTask.ContinueWith(
            static abandoned => _ = abandoned.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    private readonly record struct RunningProcessScan(IReadOnlyList<string> RunningNames, bool TimedOut);
}
