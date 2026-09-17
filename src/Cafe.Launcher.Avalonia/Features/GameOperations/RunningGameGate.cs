using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    public static async Task<GameOperationResult?> FindFailureAsync(
        IGameProcessTracker gameProcessTracker,
        LocalizationService localizer,
        string runningMessageKey,
        IReadOnlyList<string> knownProcessNames,
        CancellationToken cancellationToken)
    {
        if (knownProcessNames.Count == 0)
        {
            return null;
        }

        var runningProcesses = await gameProcessTracker
            .FindRunningGameProcessesAsync(knownProcessNames, cancellationToken)
            .ConfigureAwait(false);
        return runningProcesses.Count == 0
            ? null
            : GameOperationOutcomes.Failed(
                localizer.F(runningMessageKey, GameProcessNames.DescribeForDisplay(runningProcesses)),
                GameOperationErrorCode.GameRunning);
    }
}
