using Avalonia.Media;
using Cafe.Launcher.Avalonia.Helpers;
using MaterialColorUtilities.Utils;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// ArgbColor ↔ Avalonia conversion layer tests (M0 spike option B wiring).
/// </summary>
public sealed class MaterialColorMapperTests
{
    [Fact]
    public void ToAvaloniaColor_PreservesArgbChannels()
    {
        var source = new ArgbColor(0xAB, 0x12, 0x34, 0x56);

        var converted = MaterialColorMapper.ToAvaloniaColor(source);

        Assert.Equal((byte)0xAB, converted.A);
        Assert.Equal((byte)0x12, converted.R);
        Assert.Equal((byte)0x34, converted.G);
        Assert.Equal((byte)0x56, converted.B);
    }

    [Fact]
    public void ToArgbColor_PreservesChannels()
    {
        var source = Color.FromArgb(0xCD, 0x99, 0x77, 0x55);

        var converted = MaterialColorMapper.ToArgbColor(source);

        Assert.Equal((byte)0xCD, converted.Alpha);
        Assert.Equal((byte)0x99, converted.Red);
        Assert.Equal((byte)0x77, converted.Green);
        Assert.Equal((byte)0x55, converted.Blue);
    }

    [Fact]
    public void RoundTrip_ArgbColorToAvaloniaAndBack_PreservesValue()
    {
        var source = new ArgbColor(0xFF, 0x67, 0x50, 0xA4);

        var roundTripped = MaterialColorMapper.ToArgbColor(
            MaterialColorMapper.ToAvaloniaColor(source));

        Assert.Equal(source.Value, roundTripped.Value);
        Assert.Equal(source.Alpha, roundTripped.Alpha);
        Assert.Equal(source.Red, roundTripped.Red);
        Assert.Equal(source.Green, roundTripped.Green);
        Assert.Equal(source.Blue, roundTripped.Blue);
    }

    [Fact]
    public void ToBrush_UsesFullAlphaFromSchemeColor()
    {
        var brush = MaterialColorMapper.ToBrush(new ArgbColor(0xFF, 0x22, 0x33, 0x44));

        Assert.NotNull(brush);
        Assert.Equal(Color.FromArgb(0xFF, 0x22, 0x33, 0x44), brush.Color);
    }
}
