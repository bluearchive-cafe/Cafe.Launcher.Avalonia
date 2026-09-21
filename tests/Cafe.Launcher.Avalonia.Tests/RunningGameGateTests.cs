using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class RunningGameGateTests
{
    [Fact]
    public async Task FindFailureAsync_WhenTheScanNeverAnswers_RefusesInsteadOfHangingForever()
    {
        // 游戏在跑时反作弊可能把进程枚举挂住：扫描正文是同步枚举，令牌拦不住它。
        // 没有限时就会把会话停在首个进度之前（界面停在「正在生成文件列表」，取消也
        // 无人观察）。替身是一个永不完成的探测，限时到了必须给出拒绝而不是继续等。
        var localizer = new LocalizationService();
        var tracker = new GameProcessTracker((_, _) =>
            new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously).Task);

        var result = await RunningGameGate.FindFailureAsync(
            tracker,
            localizer,
            LocalizationKeys.GameExecutableRunning,
            KnownNames,
            CancellationToken.None,
            TimeSpan.FromMilliseconds(50));

        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal(GameOperationErrorCode.GameRunning, result.ErrorCode);
        Assert.Equal(localizer.T(LocalizationKeys.GameProcessScanUnavailable), result.Message);
    }

    [Fact]
    public async Task FindFailureAsync_WhenTheUserStopsWhileTheScanIsRunning_StopsInsteadOfWaitingForTheScan()
    {
        // 取消必须能穿透这道闸门：扫描不返回时，用户停止要从限时等待那一侧立即浮出，
        // 而不是被等成一个错误结果。替身只在令牌取消时才结束。
        using var stopSource = new CancellationTokenSource();
        var tracker = new GameProcessTracker(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        });

        var pending = RunningGameGate.FindFailureAsync(
            tracker,
            new LocalizationService(),
            LocalizationKeys.GameExecutableRunning,
            KnownNames,
            stopSource.Token,
            TimeSpan.FromSeconds(30));
        stopSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task FindFailureAsync_WhenTheScanAnswersWithinTheTimeout_NamesTheRunningFamily()
    {
        // 快速回答的扫描不受限时影响：赛跑判定的方向不能颠倒，超时分支不能吃掉正常命中。
        // 限时放宽到 2 秒：要证明的是「先到的回答不被误判」，不是赛跑贴线——整机并行跑
        // 测试时线程池可能挤占片刻，50 毫秒在这种负载下会偶发翻车。
        var tracker = new GameProcessTracker((_, _) => Task.FromResult<IReadOnlyList<string>>(["BlueArchive"]));

        var result = await RunningGameGate.FindFailureAsync(
            tracker,
            new LocalizationService(),
            LocalizationKeys.GameExecutableRunning,
            KnownNames,
            CancellationToken.None,
            TimeSpan.FromSeconds(2));

        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal(GameOperationErrorCode.GameRunning, result.ErrorCode);
        Assert.Contains("BlueArchive.exe", result.Message, StringComparison.Ordinal);
    }

    private static readonly IReadOnlyList<string> KnownNames =
        GameProcessNames.FromLaunchConfiguration("xldr_BlueArchiveOnline_JP_loader_x64", ["BlueArchive.exe"]);
}
