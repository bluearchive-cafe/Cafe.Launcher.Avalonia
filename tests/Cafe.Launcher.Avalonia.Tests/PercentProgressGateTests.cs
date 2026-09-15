using Cafe.Launcher.Avalonia.Features.GameOperations;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class PercentProgressGateTests
{
    [Fact]
    public void ShouldDeliver_WhenFirstValueIsZero_ReturnsTrue()
    {
        var gate = new PercentProgressGate();

        Assert.True(gate.ShouldDeliver(0));
    }

    [Fact]
    public void ShouldDeliver_WhenPercentRepeats_ReturnsFalse()
    {
        var gate = new PercentProgressGate();

        Assert.True(gate.ShouldDeliver(3));

        Assert.False(gate.ShouldDeliver(3));
        Assert.False(gate.ShouldDeliver(3));
    }

    [Fact]
    public void ShouldDeliver_WhenPercentChanges_ReturnsTrue()
    {
        var gate = new PercentProgressGate();
        _ = gate.ShouldDeliver(3);

        Assert.True(gate.ShouldDeliver(4));
    }

    [Fact]
    public void ShouldDeliver_WhenPercentRollsBackOnStageRestart_ReturnsTrue()
    {
        // 阶段折返（重试轮）百分比回到 0 是值变化，必须放行——
        // 否则新阶段的进度会被上一阶段的旧值吞掉。
        var gate = new PercentProgressGate();
        _ = gate.ShouldDeliver(100);

        Assert.True(gate.ShouldDeliver(0));
    }

    [Fact]
    public void ShouldDeliverMonotonic_WhenPercentIncreases_ReturnsTrue()
    {
        var gate = new PercentProgressGate();

        Assert.True(gate.ShouldDeliverMonotonic(0));
        Assert.True(gate.ShouldDeliverMonotonic(1));
        Assert.True(gate.ShouldDeliverMonotonic(100));
    }

    [Fact]
    public void ShouldDeliverMonotonic_WhenALaggingCallbackArrivesLate_ReturnsFalse()
    {
        // 并行校验的到达顺序不保证单调：25 落后于 26 时不能再报一次，否则「每个桶只报一次」
        // 被破坏（coverage 运行里那条 400 文件去重用例就是这样偶发红的）。
        var gate = new PercentProgressGate();
        _ = gate.ShouldDeliverMonotonic(26);

        Assert.False(gate.ShouldDeliverMonotonic(25));
        Assert.False(gate.ShouldDeliverMonotonic(26));
        // 落后的值也不许把门退回去：之后的 27 仍然照常投递。
        Assert.True(gate.ShouldDeliverMonotonic(27));
    }

    [Fact]
    public void ShouldDeliverMonotonic_WhenProducersRace_DeliversEveryBucketOnlyOnce()
    {
        // 半真实并发：8 个 worker 各报自己的完成数，门控只应放行严格递增的那些值。
        var gate = new PercentProgressGate();
        var delivered = new System.Collections.Concurrent.ConcurrentBag<int>();
        var seen = 0;
        Parallel.For(
            0,
            400,
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            _ =>
            {
                var percent = (int)Math.Round(Interlocked.Increment(ref seen) * 100d / 400);
                if (gate.ShouldDeliverMonotonic(percent))
                {
                    delivered.Add(percent);
                }
            });

        var ordered = delivered.OrderBy(value => value).ToArray();
        Assert.Equal(ordered.Length, ordered.Distinct().Count());
        // 不假设最小项是 0：谁先赢下 CAS 由线程竞速决定，若首个赢家已经是 1 桶，落后的 0 会被
        // 单调判据压掉（那个 400 文件去重用例的「首项是 0」断言就是这样在 CI 上偶发红的）。
        Assert.All(ordered, value => Assert.InRange(value, 0, 100));
        Assert.Equal(100, ordered[^1]);
    }

    [Fact]
    public void ShouldDeliverMonotonic_WhenTheZeroBucketLagsBehind_SuppressesIt()
    {
        // 0 桶没有特权：并行校验里 completed 1、2 都算 0、3 就算 1，前者被抢占一次就排在后者之后。
        // 阶段开头的 0 由阶段切换的显式投递负责，不靠这条路径。
        var gate = new PercentProgressGate();
        _ = gate.ShouldDeliverMonotonic(1);

        Assert.False(gate.ShouldDeliverMonotonic(0));
        Assert.True(gate.ShouldDeliverMonotonic(2));
    }
}
