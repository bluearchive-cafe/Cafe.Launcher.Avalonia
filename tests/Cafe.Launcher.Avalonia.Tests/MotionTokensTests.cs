using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class MotionTokensTests
{
    [Fact]
    public void Defaults_ExposeSharedDurationsForMotionConsumers()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(83), MotionTokens.FasterDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(167), MotionTokens.FastDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(250), MotionTokens.NormalDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(333), MotionTokens.SpatialDuration);
    }
}
