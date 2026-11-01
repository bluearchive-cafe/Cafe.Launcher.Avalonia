using System.Buffers.Binary;
using System.Text;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

/// <summary>
/// Checks delivered release banners and their changelog references, independent of authoring tools.
/// </summary>
public sealed class ReleaseBannerContractTests
{
    private const string BannerDirectoryRelativePath = "docs/assets/release-banners";
    private const int MinimumCanvasWidth = 1920;
    private const int MaximumCanvasWidth = 2400;

    [Fact]
    public void CommittedBanner_ForTheDeclaredProjectVersion_IsAPngWithAcceptedCanvas()
    {
        var version = ProjectMetadata.ReadVersionPrefix();
        var relativePath = $"{BannerDirectoryRelativePath}/v{version}.png";
        var bannerPath = TestRepository.FromRepositoryRoot(relativePath);

        Assert.True(File.Exists(bannerPath), $"Release banner is missing: {relativePath}");

        var (width, height) = ReadPngSize(bannerPath);
        AssertBannerCanvasIsAccepted(width, height, relativePath);
    }

    [Fact]
    public void CommittedBanners_HaveAcceptedPngCanvases()
    {
        var directory = TestRepository.FromRepositoryRoot(BannerDirectoryRelativePath);
        var banners = Directory.GetFiles(directory, "*.png");
        Assert.NotEmpty(banners);

        foreach (var banner in banners)
        {
            var (width, height) = ReadPngSize(banner);
            AssertBannerCanvasIsAccepted(width, height, banner);
        }
    }

    [Fact]
    public void Changelog_ReferencesTheDeclaredVersionsBanner()
    {
        var version = ProjectMetadata.ReadVersionPrefix();
        var changelog = File.ReadAllText(TestRepository.FromRepositoryRoot("CHANGELOG_RELEASE.md"));
        var expectedUrl = $"https://raw.githubusercontent.com/bluearchive-cafe/Cafe.Launcher.Avalonia/v{version}/{BannerDirectoryRelativePath}/v{version}.png";

        Assert.Contains($"]({expectedUrl})", changelog, StringComparison.Ordinal);
    }

    /// <summary>Checks the delivered image's 16:9 ratio and accepted width range.</summary>
    private static void AssertBannerCanvasIsAccepted(int width, int height, string subject)
    {
        var isSixteenByNine = (long)height * 16 == (long)width * 9;
        Assert.True(
            isSixteenByNine && width >= MinimumCanvasWidth && width <= MaximumCanvasWidth,
            $"{subject} 的画布是 {width}×{height}；本仓库接受 {MinimumCanvasWidth}×{MinimumCanvasWidth * 9 / 16}"
            + $" 到 {MaximumCanvasWidth}×{MaximumCanvasWidth * 9 / 16} 之间的 16:9 画布。");
    }

    /// <summary>Validates the PNG signature and reads its IHDR dimensions without a UI runtime.</summary>
    private static (int Width, int Height) ReadPngSize(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 24, $"{path} is too short to be a PNG ({bytes.Length} bytes).");

        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Assert.True(
            bytes.AsSpan(0, 8).SequenceEqual(signature),
            $"{path} does not start with the PNG signature.");
        Assert.Equal("IHDR", Encoding.ASCII.GetString(bytes, 12, 4));

        return (
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
    }
}
