using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Tests;

public sealed class MotionSettingsResolverTests
{
    [Theory]
    [InlineData(MotionModes.Full, null, false)]
    [InlineData(MotionModes.Reduced, true, true)]
    [InlineData(MotionModes.System, true, false)]
    [InlineData(MotionModes.System, false, true)]
    [InlineData(MotionModes.System, null, true)]
    public void ShouldReduceMotion_ResolvesMode(string mode, bool? systemAnimationsEnabled, bool expected)
    {
        Assert.Equal(expected, MotionSettingsResolver.ShouldReduceMotion(mode, systemAnimationsEnabled));
    }

    [Fact]
    public void ShouldReduceMotion_InvalidModeReducesMotion()
    {
        Assert.True(MotionSettingsResolver.ShouldReduceMotion("invalid", true));
    }
}
