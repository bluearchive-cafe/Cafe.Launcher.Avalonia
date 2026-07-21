using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services.VideoWallpaper;
using Xunit;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class VideoWallpaperLoadGateTests
{
    [Fact]
    public async Task WaitAsync_WhenFirstFrameArrives_ReturnsTrue()
    {
        using var gate = new VideoWallpaperLoadGate(CancellationToken.None);

        gate.Succeed();

        Assert.True(await gate.WaitAsync());
    }

    [Fact]
    public async Task WaitAsync_WhenPlaybackFails_ReturnsFalse()
    {
        using var gate = new VideoWallpaperLoadGate(CancellationToken.None);

        gate.Fail();

        Assert.False(await gate.WaitAsync());
    }

    [Fact]
    public async Task WaitAsync_WhenCanceled_ThrowsOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        using var gate = new VideoWallpaperLoadGate(cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.WaitAsync());
    }

    [Fact]
    public async Task WaitAsync_WhenCompleted_IgnoresLaterSignals()
    {
        using var gate = new VideoWallpaperLoadGate(CancellationToken.None);

        gate.Fail();
        gate.Succeed();

        Assert.False(await gate.WaitAsync());
    }

    [Theory]
    [InlineData(10_000_000L, 333_333L)]
    [InlineData(3_000_000L, 100_000L)]
    public void CalculateFrameInterval_Frequency_ReturnsThirtyFpsInterval(
        long frequency,
        long expected)
    {
        Assert.Equal(expected, VideoWallpaperEngine.CalculateFrameInterval(frequency));
    }
}
