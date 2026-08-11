using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class DiskSpaceServiceTests
{
    [Theory]
    [InlineData(true, 10L, "20B", 20L)]
    [InlineData(true, 30L, "20B", 30L)]
    [InlineData(false, 10L, "20B", 10L)]
    [InlineData(true, 10L, null, 10L)]
    [InlineData(true, 10L, "invalid", 10L)]
    public void ResolveRequiredBytes_StateAndSizes_ReturnsOperationPeakRequirement(
        bool isFreshInstall,
        long plannedDownloadBytes,
        string? decompressionSize,
        long expected)
    {
        var result = DiskSpaceService.ResolveRequiredBytes(isFreshInstall, plannedDownloadBytes, decompressionSize);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Check_WhenAvailableEqualsRequired_ReturnsEnough()
    {
        var service = new DiskSpaceService
        {
            GetAvailableBytesOverride = _ => 20L,
        };

        var result = service.Check("C:\\", 20L);

        Assert.True(result.IsAvailableKnown);
        Assert.True(result.HasEnoughSpace);
    }

    [Fact]
    public void Check_WhenAvailableIsUnknown_ReturnsUnknownAndNotEnough()
    {
        var service = new DiskSpaceService
        {
            GetAvailableBytesOverride = _ => null,
        };

        var result = service.Check("C:\\", 20L);

        Assert.False(result.IsAvailableKnown);
        Assert.False(result.HasEnoughSpace);
    }

    [Fact]
    public void Check_WhenRequiredIsNegative_NormalizesRequiredAndReadsAvailabilityOnce()
    {
        var readCount = 0;
        var service = new DiskSpaceService
        {
            GetAvailableBytesOverride = _ =>
            {
                readCount++;
                return 0L;
            },
        };

        var result = service.Check("C:\\", -1L);

        Assert.Equal(0L, result.RequiredBytes);
        Assert.Equal(1, readCount);
    }

    [Fact]
    public void HasEnoughSpace_WhenRequiredIsNotPositive_ReturnsTrueWithoutReadingAvailability()
    {
        var service = new DiskSpaceService
        {
            GetAvailableBytesOverride = _ => throw new InvalidOperationException(),
        };

        var result = service.HasEnoughSpace("C:\\", 0L);

        Assert.True(result);
    }

    [Fact]
    public void GetAvailableBytes_WhenPathIsInvalid_ReturnsNull()
    {
        var service = new DiskSpaceService();

        var result = service.GetAvailableBytes("invalid\0path");

        Assert.Null(result);
    }
}
