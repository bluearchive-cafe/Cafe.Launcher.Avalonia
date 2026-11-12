using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.UI.Features.GameOperations;

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

    /// <summary>
    /// 这道闸门认哪些名字（ADR-032）：本地配置带来宿主名与启动参数里的可执行文件；还没有本地
    /// 配置时（全新安装，或配置缺失/损坏）退回远端配置声明的**同样两个字段**（启动程序名与
    /// 启动参数）——本地配置落盘时写的就是这两项，两条路的判据因此一致。名字一个都取不到时
    /// 返回空：闸门不做无根据的拒绝，会话看护也认同一份家族（ADR-035）——启动路径与破坏性
    /// 操作路径的「游戏在跑」判据同源，不会分叉。
    /// </summary>
    internal static RunningGameQuery ResolveQuery(
        GameLauncherConfig? localConfig,
        GameConfigResponse? remoteConfig,
        string? installDirectory)
    {
        var names = localConfig?.Name is { Length: > 0 } hostExeName
            ? GameProcessNames.FromLaunchConfiguration(hostExeName, localConfig.Params)
            : GameProcessNames.FromLaunchConfiguration(remoteConfig?.GameStartExeName, remoteConfig?.GameStartParams);
        return new RunningGameQuery(names, installDirectory);
    }

    public static async Task<GameOperationResult?> FindFailureAsync(
        IGameProcessTracker gameProcessTracker,
        LocalizationService localizer,
        LocalizationKeys runningMessageKey,
        RunningGameQuery query,
        CancellationToken cancellationToken,
        TimeSpan? scanTimeout = null)
    {
        if (query.KnownExeNames.Count == 0)
        {
            return null;
        }

        var runningNames = await BoundedProcessScan.RunAsync(
            token => gameProcessTracker.FindRunningGameProcessesAsync(query, token),
            scanTimeout ?? DefaultScanTimeout,
            cancellationToken).ConfigureAwait(false);
        if (runningNames is null)
        {
            // 与「枚举抛异常按没在跑放行」（ADR-032 决策 6）相反：枚举**没有答案**时拒绝。
            // 异常是确定的读取失败、与游戏是否在跑无关；答不出来偏偏与游戏在跑相关——
            // 正是反作弊在场才挂得住枚举。闸门防的是在跑时改文件，「可能还在跑」时按
            // 拒绝处理是安全分支，而「请关闭游戏后重试」在两个世界里都是可动作的指引。
            return GameOperationOutcomes.Failed(
                localizer.T(LocalizationKeys.GameProcessScanUnavailable),
                GameOperationErrorCode.GameRunning);
        }

        return runningNames.Count == 0
            ? null
            : GameOperationOutcomes.Failed(
                localizer.F(runningMessageKey, GameProcessNames.DescribeForDisplay(runningNames)),
                GameOperationErrorCode.GameRunning);
    }
}
