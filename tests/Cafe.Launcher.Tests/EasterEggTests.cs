using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.ViewModels;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Tests;

public sealed class EasterEggTests
{
    [Theory]
    [InlineData(0, "Midori Launcher")]
    [InlineData(1, "Momoi Launcher")]
    public void ResolveProductName_OnDecemberEighth_ReturnsSpecifiedName(
        int randomIndex,
        string expected)
    {
        var actual = ShellViewModel.ResolveProductName(LauncherProfiles.Cafe.ProductName, 
            new DateTime(2026, 12, 8),
            randomIndex);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveProductName_OutsideDecemberEighth_ReturnsDefaultName()
    {
        var actual = ShellViewModel.ResolveProductName(LauncherProfiles.Cafe.ProductName, 
            new DateTime(2026, 12, 9),
            0);

        Assert.Equal(LauncherProfiles.Cafe.ProductName, actual);
    }
}
