using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class EasterEggTests
{
    [Theory]
    [InlineData(0, "Midori Launcher")]
    [InlineData(1, "Momoi Launcher")]
    public void ResolveProductName_OnDecemberEighth_ReturnsSpecifiedName(
        int randomIndex,
        string expected)
    {
        var actual = ShellViewModel.ResolveProductName(
            new DateTime(2026, 12, 8),
            randomIndex);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveProductName_OutsideDecemberEighth_ReturnsDefaultName()
    {
        var actual = ShellViewModel.ResolveProductName(
            new DateTime(2026, 12, 9),
            0);

        Assert.Equal(LauncherConstants.ProductName, actual);
    }
}
