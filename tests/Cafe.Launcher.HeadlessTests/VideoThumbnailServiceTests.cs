using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.Controls.VideoBackground;
using Cafe.Launcher.UI.Services;

namespace Cafe.Launcher.HeadlessTests;

public sealed class VideoThumbnailServiceTests
{
    // Tiny, generated fixtures: two red MPEG-4 frames and a blue PNG/JPEG covr image.
    // They need no encoder or external FFmpeg executable when the tests run.
    [AvaloniaTheory]
    [InlineData("red-video-blue-cover.mp4")]
    [InlineData("red-video-blue-jpeg-cover.mp4")]
    public async Task LoadThumbnailAsync_WhenMp4HasEmbeddedCover_PrefersCoverOverVideoFrame(string fileName)
    {
        var path = TestRepository.FromRepositoryRoot(
            $"tests/Cafe.Launcher.HeadlessTests/Fixtures/VideoThumbnails/{fileName}");
        var service = new VideoThumbnailService(new LocalDiagnostics());
        var originalBudget = VideoThumbnailService.CaptureTimeout;
        VideoThumbnailService.CaptureTimeout = TimeSpan.Zero;
        try
        {
            using var image = Assert.IsType<Bitmap>(await service.LoadThumbnailAsync(path, CancellationToken.None));

            Assert.InRange(image.PixelSize.Width, 1, 256);
            Assert.InRange(image.PixelSize.Height, 1, 176);
            var pixel = ReadCenterPixel(image);
            Assert.True(pixel[0] > 200 && pixel[2] < 60,
                $"Expected the blue embedded cover, got BGR {pixel[0]},{pixel[1]},{pixel[2]}.");
        }
        finally
        {
            VideoThumbnailService.CaptureTimeout = originalBudget;
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadThumbnailAsync_WhenMp4CoverIsMissingOrCorrupt_FallsBackToFirstFrame(bool corrupt)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Uses the bundled Windows decoder.");
        using var directory = TestDirectory.Create();
        var bytes = await File.ReadAllBytesAsync(TestRepository.FromRepositoryRoot(
            "tests/Cafe.Launcher.HeadlessTests/Fixtures/VideoThumbnails/red-video-blue-cover.mp4"));
        var coverOffset = bytes.AsSpan().IndexOf("covr"u8);
        Assert.True(coverOffset > 0);
        if (corrupt)
        {
            var dataOffset = coverOffset + bytes.AsSpan(coverOffset).IndexOf("data"u8);
            var dataSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(dataOffset - 4, 4));
            bytes.AsSpan(dataOffset + 12, (int)dataSize - 16).Clear();
        }
        else
        {
            "free"u8.CopyTo(bytes.AsSpan(coverOffset, 4));
        }
        var path = directory.Sub("fallback.mp4");
        await File.WriteAllBytesAsync(path, bytes);
        var service = new VideoThumbnailService(new LocalDiagnostics());

        using var image = Assert.IsType<Bitmap>(await service.LoadThumbnailAsync(path, CancellationToken.None));

        var pixel = ReadCenterPixel(image);
        Assert.True(pixel[2] > 200 && pixel[0] < 60,
            $"Expected the red first frame, got BGR {pixel[0]},{pixel[1]},{pixel[2]}.");
    }

    [AvaloniaFact]
    public async Task LoadThumbnailAsync_WhenVideoHasDifferentFrames_ReturnsFirstFrameAndReleasesDecoder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Uses the bundled Windows decoder.");
        await HeadlessTestHost.WaitUntilAsync(() => MpvNative.ActiveLibraryCount == 0, TimeSpan.FromSeconds(5));
        using var directory = TestDirectory.Create();
        var path = directory.Sub("first-frame.mp4");
        HeadlessTestHost.WriteTwoFrameVideo(path);
        var libraries = MpvNative.ActiveLibraryCount;
        var service = new VideoThumbnailService(new LocalDiagnostics());

        using var image = Assert.IsType<Bitmap>(await service.LoadThumbnailAsync(path, CancellationToken.None));

        Assert.InRange(image.PixelSize.Width, 1, 256);
        Assert.InRange(image.PixelSize.Height, 1, 176);
        var pixel = ReadCenterPixel(image);
        Assert.True(pixel[2] > 200 && pixel[0] < 60, $"Expected the red first frame, got BGR {pixel[0]},{pixel[1]},{pixel[2]}.");
        Assert.Equal(libraries, MpvNative.ActiveLibraryCount);
    }

    [AvaloniaFact]
    public async Task LoadThumbnailAsync_WhenMediaIsInvalid_ReturnsNullAndReleasesDecoder()
    {
        await HeadlessTestHost.WaitUntilAsync(() => MpvNative.ActiveLibraryCount == 0, TimeSpan.FromSeconds(5));
        using var directory = TestDirectory.Create();
        var path = directory.Sub("invalid.mp4");
        await File.WriteAllTextAsync(path, "not a movie");
        var libraries = MpvNative.ActiveLibraryCount;
        var service = new VideoThumbnailService(new LocalDiagnostics());
        Assert.Null(await service.LoadThumbnailAsync(path, CancellationToken.None));
        Assert.Equal(libraries, MpvNative.ActiveLibraryCount);
    }

    [AvaloniaFact]
    public async Task LoadThumbnailAsync_WhenCaptureTimesOut_ReturnsNullAndReleasesDecoder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Uses the bundled Windows decoder.");
        await HeadlessTestHost.WaitUntilAsync(() => MpvNative.ActiveLibraryCount == 0, TimeSpan.FromSeconds(5));
        using var directory = TestDirectory.Create();
        var path = directory.Sub("timeout.mp4");
        HeadlessTestHost.WriteTwoFrameVideo(path);
        var originalBudget = VideoThumbnailService.CaptureTimeout;
        VideoThumbnailService.CaptureTimeout = TimeSpan.Zero;
        try
        {
            var service = new VideoThumbnailService(new LocalDiagnostics());
            Assert.Null(await service.LoadThumbnailAsync(path, CancellationToken.None));
            Assert.Equal(0, MpvNative.ActiveLibraryCount);
        }
        finally
        {
            VideoThumbnailService.CaptureTimeout = originalBudget;
        }
    }

    [AvaloniaFact]
    public async Task LoadThumbnailAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new VideoThumbnailService(new LocalDiagnostics());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LoadThumbnailAsync("video.mp4", cancellation.Token));
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("missing.mp4")]
    [InlineData("unsupported.png")]
    public async Task LoadThumbnailAsync_WhenSourceUnavailable_ReturnsNull(string? path)
    {
        var service = new VideoThumbnailService(new LocalDiagnostics());
        Assert.Null(await service.LoadThumbnailAsync(path, CancellationToken.None));
    }

    private static byte[] ReadCenterPixel(Bitmap image)
    {
        var pixel = new byte[4];
        var pinned = GCHandle.Alloc(pixel, GCHandleType.Pinned);
        try
        {
            image.CopyPixels(new PixelRect(image.PixelSize.Width / 2, image.PixelSize.Height / 2, 1, 1),
                pinned.AddrOfPinnedObject(), pixel.Length, pixel.Length);
        }
        finally { pinned.Free(); }
        return pixel;
    }
}
