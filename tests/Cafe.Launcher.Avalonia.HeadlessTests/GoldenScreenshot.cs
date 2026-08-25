using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
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
    /// (re)writes the baseline instead of comparing.
    /// </summary>
    public static void Compare(Window window, string name)
    {
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
                    "Regenerate intentionally with $env:CAFE_GOLDEN_UPDATE=1 and commit the PNG.");
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
        Assert.True(
            ratio <= MaxMismatchRatio,
            $"Golden '{name}' deviates {ratio:P2} of pixels (allowed {MaxMismatchRatio:P0}). " +
            "Intentional visual change? Regenerate with $env:CAFE_GOLDEN_UPDATE=1.");
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
