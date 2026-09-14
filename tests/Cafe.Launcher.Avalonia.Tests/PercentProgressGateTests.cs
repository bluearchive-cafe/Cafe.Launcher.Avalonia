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
}
