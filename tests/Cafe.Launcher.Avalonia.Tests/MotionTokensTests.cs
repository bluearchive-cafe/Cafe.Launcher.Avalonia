using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class MotionTokensTests
{
    [Fact]
    public void Defaults_ExposeSharedDurationsForMotionConsumers()
    {
        var tokenType = typeof(AnimationTimings).Assembly.GetType(
            "Cafe.Launcher.Avalonia.Helpers.MotionTokens");

        Assert.NotNull(tokenType);
        Assert.Equal(
            TimeSpan.FromMilliseconds(50),
            tokenType.GetField("FasterDuration")?.GetValue(null));
        Assert.Equal(
            TimeSpan.FromMilliseconds(167),
            tokenType.GetField("FastDuration")?.GetValue(null));
        Assert.Equal(
            TimeSpan.FromMilliseconds(200),
            tokenType.GetField("ContentDuration")?.GetValue(null));
        Assert.Equal(
            TimeSpan.FromMilliseconds(250),
            tokenType.GetField("NormalDuration")?.GetValue(null));
        Assert.Equal(
            TimeSpan.FromMilliseconds(50),
            tokenType.GetField("OverlayDelay")?.GetValue(null));
    }
}
