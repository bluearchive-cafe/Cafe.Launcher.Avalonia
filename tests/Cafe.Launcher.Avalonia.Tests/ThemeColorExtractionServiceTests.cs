using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ThemeColorExtractionServiceTests
{
    [Theory]
    [InlineData(ThemeColorExtractionAlgorithms.Octree)]
    [InlineData(ThemeColorExtractionAlgorithms.CelebiScore)]
    [InlineData(ThemeColorExtractionAlgorithms.Wu)]
    [InlineData(ThemeColorExtractionAlgorithms.Wsmeans)]
    public void ExtractPaletteFromBgraBuffer_WithAlgorithm_ReturnsRepresentativeColors(string algorithm)
    {
        var pixels = new byte[]
        {
            0x00, 0x00, 0xFF, 0xFF,
            0x00, 0xFF, 0x00, 0xFF,
            0xFF, 0x00, 0x00, 0xFF,
            0x00, 0x00, 0xFF, 0xFF
        };

        var colors = ThemeColorExtractionService.ExtractPaletteFromBgraBuffer(
            pixels,
            width: 4,
            height: 1,
            rowBytes: 16,
            algorithm: algorithm);

        Assert.NotEmpty(colors);
        Assert.All(colors, color => Assert.Equal(0xFF, color.A));
    }
}
