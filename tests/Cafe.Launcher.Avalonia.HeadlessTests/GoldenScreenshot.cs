using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary>
/// Golden-screenshot capture and comparison (design-system spec §10, P1 plan M5).
///
/// Font stability: the app state is fixed for capture (English language, motion
/// reduced, window font forced to Segoe UI), but CI (windows-latest) and local
/// machines can still differ in font hinting/version metrics and Skia raster
/// edges. The comparison therefore allows a small per-channel tolerance and a
/// bounded mismatch ratio instead of requiring bit-exact output. If a baseline
/// must be regenerated (intentional visual change), run:
///   $env:CAFE_GOLDEN_UPDATE = "1"; dotnet test ... --filter FullyQualifiedName~Golden
/// and commit the refreshed PNGs together with the visual change.
/// </summary>
internal static class GoldenScreenshot
{
    private const string BaselineRelativeDir = "tests/Cafe.Launcher.Avalonia.HeadlessTests/Baselines";
    private const int ChannelTolerance = 8;
    private const double MaxMismatchRatio = 0.01;

    /// <summary>
    /// Renders <paramref name="window"/> and compares the frame to the committed
    /// baseline <paramref name="name"/>.png; with CAFE_GOLDEN_UPDATE=1 the frame
    /// (re)writes the baseline instead of comparing. The canonical update path is
    /// <c>.\test.ps1 -UpdateGolden</c>.
    /// </summary>
    public static void Compare(Window window, string name)
    {
        // Baselines are generated on Windows (Segoe UI + Windows Skia raster);
        // font fallback on other platforms always exceeds the mismatch budget.
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Golden baselines are generated on Windows; non-Windows rendering always mismatches.");

        Dispatcher.UIThread.RunJobs();
        var bitmap = Render(window);
        var baselineDirectory = Path.Combine(FindRepositoryRoot(), BaselineRelativeDir);
        var baselinePath = Path.Combine(baselineDirectory, name + ".png");
        var update = Environment.GetEnvironmentVariable("CAFE_GOLDEN_UPDATE") == "1";

        if (!File.Exists(baselinePath))
        {
            if (!update)
            {
                Assert.Fail(
                    $"Golden baseline '{name}' is missing at {baselinePath}. " +
                    "Regenerate intentionally with .\\test.ps1 -UpdateGolden and commit the PNG.");
            }

            Directory.CreateDirectory(baselineDirectory);
            WriteBaseline(bitmap, baselinePath);
            return;
        }

        if (update)
        {
            WriteBaseline(bitmap, baselinePath);
            return;
        }

        AssertMatches(bitmap, baselinePath, name);
    }

    private static RenderTargetBitmap Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        var size = new PixelSize(
            (int)Math.Ceiling(window.Bounds.Width),
            (int)Math.Ceiling(window.Bounds.Height));
        var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(window);
        return bitmap;
    }

    private static void WriteBaseline(RenderTargetBitmap bitmap, string path)
    {
        using var stream = File.Create(path);
        bitmap.Save(stream, new PngBitmapEncoderOptions());
    }

    private static void AssertMatches(RenderTargetBitmap actual, string baselinePath, string name)
    {
        using var baseline = new Bitmap(baselinePath);
        var size = actual.PixelSize;
        if (baseline.PixelSize != size)
        {
            Assert.Fail(
                $"Golden '{name}' size changed: baseline {baseline.PixelSize.Width}x{baseline.PixelSize.Height}, " +
                $"actual {size.Width}x{size.Height}.");
        }

        var stride = size.Width * 4;
        var bufferSize = stride * size.Height;
        var actualPixels = new byte[bufferSize];
        var baselinePixels = new byte[bufferSize];
        CopyPixels(actual, size, stride, actualPixels);
        CopyPixels(baseline, size, stride, baselinePixels);

        var mismatched = 0L;
        for (var i = 0; i < bufferSize; i += 4)
        {
            var differs =
                Math.Abs(actualPixels[i] - baselinePixels[i]) > ChannelTolerance
                || Math.Abs(actualPixels[i + 1] - baselinePixels[i + 1]) > ChannelTolerance
                || Math.Abs(actualPixels[i + 2] - baselinePixels[i + 2]) > ChannelTolerance;
            if (differs)
            {
                mismatched++;
            }
        }

        var ratio = mismatched / (double)(size.Width * size.Height);
        if (ratio > MaxMismatchRatio)
        {
            var artifactDirectory = WriteFailureArtifacts(
                actual, actualPixels, baselinePixels, size, name);
            Assert.Fail(
                $"Golden '{name}' deviates {ratio:P2} of pixels (allowed {MaxMismatchRatio:P0}). " +
                $"Actual and diff images saved to {artifactDirectory}. " +
                "Intentional visual change? Regenerate with .\\test.ps1 -UpdateGolden.");
        }
    }

    /// <summary>
    /// Persists the failing frame and a red-on-white diff mask under
    /// TestResults/Golden so the failing regions can be inspected without
    /// re-running the capture.
    /// </summary>
    private static string WriteFailureArtifacts(
        RenderTargetBitmap actual,
        byte[] actualPixels,
        byte[] baselinePixels,
        PixelSize size,
        string name)
    {
        var artifactDirectory = Path.Combine(FindRepositoryRoot(), "TestResults", "Golden");
        Directory.CreateDirectory(artifactDirectory);

        var actualPath = Path.Combine(artifactDirectory, name + "-actual.png");
        using (var stream = File.Create(actualPath))
        {
            actual.Save(stream, new PngBitmapEncoderOptions());
        }

        var diffPath = Path.Combine(artifactDirectory, name + "-diff.png");
        var rowBytes = size.Width * 4;
        var diffPixels = new byte[rowBytes * size.Height];
        for (var pixel = 0; pixel < size.Width * size.Height; pixel++)
        {
            var sourceOffset = pixel * 4;
            var targetOffset = pixel * 4;
            var differs =
                Math.Abs(actualPixels[sourceOffset] - baselinePixels[sourceOffset]) > ChannelTolerance
                || Math.Abs(actualPixels[sourceOffset + 1] - baselinePixels[sourceOffset + 1]) > ChannelTolerance
                || Math.Abs(actualPixels[sourceOffset + 2] - baselinePixels[sourceOffset + 2]) > ChannelTolerance;
            if (!differs)
            {
                continue;
            }

            // BGRA：失配像素涂红（其余保持透明），掩膜只标出偏差区域。
            diffPixels[targetOffset] = 255;
            diffPixels[targetOffset + 1] = 0;
            diffPixels[targetOffset + 2] = 0;
            diffPixels[targetOffset + 3] = 255;
        }

        using var diff = new WriteableBitmap(
            size,
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using (var frame = diff.Lock())
        {
            // Marshal.Copy 没有 IntPtr→IntPtr 重载，逐行经中间缓冲拷入帧内存。
            var rowBuffer = new byte[rowBytes];
            for (var row = 0; row < size.Height; row++)
            {
                Array.Copy(diffPixels, row * rowBytes, rowBuffer, 0, rowBytes);
                Marshal.Copy(rowBuffer, 0, frame.Address + (row * frame.RowBytes), rowBytes);
            }
        }

        using (var stream = File.Create(diffPath))
        {
            diff.Save(stream, new PngBitmapEncoderOptions());
        }

        return artifactDirectory;
    }

    private static void CopyPixels(Bitmap bitmap, PixelSize size, int stride, byte[] target)
    {
        var handle = GCHandle.Alloc(target, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(
                new PixelRect(0, 0, size.Width, size.Height),
                handle.AddrOfPinnedObject(),
                target.Length,
                stride);
        }
        finally
        {
            handle.Free();
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null
               && !File.Exists(Path.Combine(current.FullName, "Cafe.Launcher.Avalonia.slnx")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return current!.FullName;
    }
}
