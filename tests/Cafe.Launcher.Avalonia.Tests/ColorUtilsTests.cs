using Avalonia.Media;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ColorUtilsTests
{
    [Theory]
    [InlineData("#FF2E7DF6")]
    [InlineData("#FFC9CDD8")]
    [InlineData("#FF777777")]
    [InlineData("#FF336699")]
    [InlineData("#FFB35679")]
    public void NormalizeAccentColorForUi_UsesForegroundWithWcagNormalTextContrast(string colorText)
    {
        var accent = ColorUtils.NormalizeAccentColorForUi(Color.Parse(colorText));
        var foreground = ColorUtils.GetReadableOnAccentColor(accent);

        Assert.True(ColorUtils.GetContrastRatio(accent, foreground) >= 4.5d);
    }

    [Fact]
    public void GetReadableOnAccentColor_DefaultCafeBlue_UsesAccessibleDarkForeground()
    {
        var accent = Color.Parse("#FF2E7DF6");

        var foreground = ColorUtils.GetReadableOnAccentColor(accent);

        Assert.Equal(Color.FromRgb(0x12, 0x18, 0x20), foreground);
        Assert.True(ColorUtils.GetContrastRatio(accent, foreground) >= 4.5d);
    }

    [Theory]
    [InlineData("#FF5796F8")]
    [InlineData("#FF276AD1")]
    [InlineData("#FFE5484D")]
    [InlineData("#FFC9353A")]
    public void GetReadableForegroundColor_ForInteractiveState_MeetsWcagNormalTextContrast(
        string backgroundText)
    {
        var background = Color.Parse(backgroundText);
        var foreground = ColorUtils.GetReadableForegroundColor(background);

        Assert.True(ColorUtils.GetContrastRatio(background, foreground) >= 4.5d);
    }
}
