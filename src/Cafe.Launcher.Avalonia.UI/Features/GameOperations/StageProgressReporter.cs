using System;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// 一个逐文件阶段的百分比回报器（D6）：百分比门控（<see cref="PercentProgressGate"/>）、
/// 门控判据的选择与进度快照的构造收在一处，各逐文件阶段不再各自包一遍。
/// 默认判据是「值变化即投递」——阶段折返重报是真实语义，首值 0 由显式阶段发射保证；
/// 并行 worker 回报的阶段传 <paramref name="monotonic"/> 取单调判据（判据差异的理由见
/// <see cref="PercentProgressGate.ShouldDeliverMonotonic"/>）。
/// </summary>
internal sealed class StageProgressReporter
{
    private readonly PercentProgressGate gate = new();
    private readonly Action<int> report;

    /// <summary>Composes each gated percent into a progress snapshot for <paramref name="sink"/>.</summary>
    public StageProgressReporter(
        GameOperationKind kind,
        GameOperationStage stage,
        Action<GameOperationProgress> sink,
        bool monotonic = false)
        : this(value => sink(GameOperationProgressFactory.CreateProgress(kind, stage, value)), monotonic)
    {
    }

    /// <summary>Percent-only sink for callers whose progress snapshot is composed upstream.</summary>
    public StageProgressReporter(Action<int> sink, bool monotonic = false)
    {
        report = percent =>
        {
            if (monotonic ? gate.ShouldDeliverMonotonic(percent) : gate.ShouldDeliver(percent))
            {
                sink(percent);
            }
        };
    }

    /// <summary>Percent consumer: gates and forwards one per-file percent report.</summary>
    public Action<int> Report => report;

    /// <summary>逐文件百分比。存量调用都不以空清单进循环，这里的守卫只防后来者。</summary>
    internal static int Percent(int completed, int total) =>
        total > 0 ? (int)Math.Round(completed * 100d / total) : 100;
}
