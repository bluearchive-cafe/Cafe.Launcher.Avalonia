using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

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
        var result = new DiskSpaceService()
            .ResolveRequiredBytes(isFreshInstall, plannedDownloadBytes, decompressionSize);

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

    [Fact]
    public void SelectLongestVolumeRoot_WhenTargetIsBelowNestedMount_ReturnsDeepestMount()
    {
        var result = DiskSpaceService.SelectLongestVolumeRoot(
            "/mnt/data/BlueArchive/game",
            ["/", "/mnt", "/mnt/data"]);

        Assert.Equal("/mnt/data", result);
    }

    [Fact]
    public void SelectLongestVolumeRoot_WhenTargetEqualsMountRoot_ReturnsThatRoot()
    {
        var result = DiskSpaceService.SelectLongestVolumeRoot(
            "/home",
            ["/", "/home"]);

        Assert.Equal("/home", result);
    }

    [Fact]
    public void SelectLongestVolumeRoot_WhenSubdirectoryIsOnSameVolume_ReturnsVolumeRoot()
    {
        var result = DiskSpaceService.SelectLongestVolumeRoot(
            "/home/user/BlueArchive",
            ["/", "/home"]);

        Assert.Equal("/home", result);
    }

    [Fact]
    public void SelectLongestVolumeRoot_WhenSiblingSharesNamePrefix_DoesNotMatchPartialSegment()
    {
        var result = DiskSpaceService.SelectLongestVolumeRoot(
            "/mnt2/BlueArchive",
            ["/", "/mnt"]);

        Assert.Equal("/", result);
    }

    [Fact]
    public void SelectLongestVolumeRoot_WhenCandidateHasTrailingSeparator_ReturnsNormalizedRoot()
    {
        var result = DiskSpaceService.SelectLongestVolumeRoot(
            "/mnt/BlueArchive",
            ["/", "/mnt/"]);

        Assert.Equal("/mnt", result);
    }

    [Fact]
    public void SelectLongestVolumeRoot_WhenNoVolumeContainsPath_ReturnsNull()
    {
        var result = DiskSpaceService.SelectLongestVolumeRoot(
            "/opt/BlueArchive",
            ["/mnt", "/home"]);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveVolumeRoot_OnWindows_SubdirectoryMapsToDriveLetterRoot()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "盘符语义只在 Windows 上成立。");
        using var directory = TestDirectory.Create();
        var nested = directory.Sub("BlueArchive");
        Directory.CreateDirectory(nested);

        var result = DiskSpaceService.ResolveVolumeRoot(nested);

        Assert.Equal(Path.GetPathRoot(nested), result);
        Assert.Matches(@"^[A-Za-z]:\\$", result);
    }

    [Fact]
    public void GetAvailableBytes_WhenTargetDoesNotExist_UsesNearestExistingAncestorVolume()
    {
        using var directory = TestDirectory.Create();
        var missing = Path.Combine(directory.Path, "not-created", "BlueArchive", "bin");
        var service = new DiskSpaceService();

        var expected = service.GetAvailableBytes(directory.Path);
        Assert.SkipUnless(expected is not null, "当前环境无法读取临时目录所在卷的可用空间。");

        var result = service.GetAvailableBytes(missing);

        Assert.Equal(expected, result);
    }
}
