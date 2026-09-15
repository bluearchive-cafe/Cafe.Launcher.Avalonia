using System.Threading;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// Gates per-file percent callbacks so the consumer receives them only when
/// the integer percent actually changes (AUD-PERF-007). The verify/diff/
/// uninstall stages used to dispatch one callback per manifest file — for a
/// full installation that is thousands of UI-thread posts, each doing about a
/// dozen observable-property writes plus localized string formatting, the same
/// flooding <see cref="DownloadProgressAccumulator"/> solves with a time gate
/// on the byte-streaming download stage. A percent gate fits these stages:
/// they report a 0-100 percent derived from a per-stage counter, repeats are
/// suppressed, and a stage restart (percent rolling back) is a value change and
/// is delivered too. Explicit stage emissions (stage switches, repair-confirm)
/// bypass the gate on purpose, and they are what guarantees a stage's 0 reaches
/// the consumer: the serial consumers' first callback is 0, but the parallel one
/// (<see cref="ShouldDeliverMonotonic"/>) can drop a 0 that arrives after a
/// higher bucket, so a consumer must not assume 0 arrives first. Thread-safe:
/// the install-verification stage reports from parallel workers.
/// </summary>
internal sealed class PercentProgressGate
{
    private int lastReported = -1;

    /// <summary>Reports whether <paramref name="percent"/> differs from the last delivered value.</summary>
    internal bool ShouldDeliver(int percent) => Interlocked.Exchange(ref lastReported, percent) != percent;

    /// <summary>
    /// 并行调用方的门控：只投递比上次更大的值。
    /// </summary>
    /// <remarks>
    /// 「值变化即投递」这条判据在并发下有个假象：回调的到达顺序不保证单调，一次落后的回调
    /// 会把已经走过的桶再报一遍——进度条回跳，而且「每个桶只报一次」被破坏（2026-09-15 复核轮：
    /// coverage 运行里 <c>DownloadExecutorTests</c> 的 400 文件去重用例因此偶发红，报出
    /// delivered 102 / distinct 101）。这条路径的百分比由「已完成数 ÷ 总数」算出，同一轮内只增
    /// 不减，所以收紧成单调没有语义损失；需要折返重报的调用方（阶段重启）用 <see cref="ShouldDeliver"/>。
    /// 2026-09-15 深夜 CI 复查补记：同一用例的收集侧当时还把并行 worker 的回调直接接到未加锁的
    /// <c>List&lt;int&gt;</c> 上，并发 <c>Add</c> 同样能造出重复条目——那个 102/101 不能只归给判据。
    /// 判据侧保留单调（落后回调回跳是真实的），收集侧已在用例里加锁，两侧各自不再制造重复。
    /// </remarks>
    internal bool ShouldDeliverMonotonic(int percent)
    {
        while (true)
        {
            var previous = Volatile.Read(ref lastReported);
            if (percent <= previous)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref lastReported, percent, previous) == previous)
            {
                return true;
            }
        }
    }
}
