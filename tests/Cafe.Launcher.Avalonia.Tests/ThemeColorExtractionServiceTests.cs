namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ThemeColorExtractionServiceTests
{
    [Fact]
    public void ExtractPaletteFromBgraBuffer_WhenMoreThanFourColors_ReturnsFourDesignSwatches()
    {
        var pixels = new byte[]
        {
            0x00, 0x00, 0xFF, 0xFF,
            0x00, 0xFF, 0x00, 0xFF,
            0xFF, 0x00, 0x00, 0xFF,
            0x00, 0xFF, 0xFF, 0xFF,
            0xFF, 0x00, 0xFF, 0xFF
        };

        var palette = Services.ThemeColorExtractionService.ExtractPaletteFromBgraBuffer(
            pixels,
            width: 5,
            height: 1,
            rowBytes: 20);

        Assert.Equal(4, palette.Count);
    }
}
