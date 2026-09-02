using Avalonia;
using Avalonia.Media;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Design-gallery classification logic (spec §9 Q11): family derivation from the
/// token key segments, group ordering and value formatting.
/// </summary>
public sealed class DesignTokenGroupingTests
{
    [Theory]
    [InlineData("Launcher.Color.Primary", "Color")]
    [InlineData("Launcher.Spacing.Thickness.Xs", "Spacing")]
    [InlineData("Launcher.Typography.FontSize.Body.Md", "Typography")]
    [InlineData("LauncherBorderButtonTemplate", "Other")]
    public void FamilyForKey_DerivesFamilyFromSecondSegment(string key, string expected)
    {
        Assert.Equal(expected, DesignTokenGrouping.FamilyForKey(key));
    }

    [Fact]
    public void BuildGroups_OrdersFamiliesPerSpecAndItemsPerKey()
    {
        var pairs = new (string Key, object? Value)[]
        {
            ("Launcher.Spacing.Md", 12d),
            ("Launcher.Color.Primary", new SolidColorBrush(Color.Parse("#FF6750A4"))),
            ("Launcher.Spacing.Xs", 4d),
            ("Launcher.Color.Secondary", new SolidColorBrush(Color.Parse("#FF625B71")))
        };

        var groups = DesignTokenGrouping.BuildGroups(pairs, key => $"L:{key}");

        Assert.Equal(2, groups.Count);
        Assert.Equal("Color", groups[0].Family);
        Assert.Equal("designGroupColor", groups[0].ResxKey);
        Assert.Equal("L:designGroupColor", groups[0].DisplayName);
        Assert.Equal(["Primary", "Secondary"], groups[0].Items.Select(item => item.Key.Split('.').Last()));
        Assert.Equal("Spacing", groups[1].Family);
        Assert.Equal(["Md", "Xs"], groups[1].Items.Select(item => item.Key.Split('.').Last())); // Xs first by ordinal? Md < Xs
    }

    [Fact]
    public void BuildGroups_GroupsUngroupedKeysIntoOtherAsLastGroup()
    {
        var groups = DesignTokenGrouping.BuildGroups(
            [("Launcher_Undotted", null)],
            key => key);

        var other = Assert.Single(groups);
        Assert.Equal("Other", other.Family);
        Assert.Equal("Launcher_Undotted", other.Items[0].Key);
    }

    [Fact]
    public void FormatValue_RendersResourceTypesForGallery()
    {
        Assert.Equal("#FF6750A4", DesignTokenGrouping.FormatValue(
            new SolidColorBrush(Color.Parse("#FF6750A4"))).ToUpperInvariant());
        Assert.Equal("8,8,8,8", DesignTokenGrouping.FormatValue(new Thickness(8)));
        Assert.Equal("4,0,2,0", DesignTokenGrouping.FormatValue(new Thickness(4, 0, 2, 0)));
        Assert.Equal("50 ms", DesignTokenGrouping.FormatValue(TimeSpan.FromMilliseconds(50)));
        Assert.Equal("12", DesignTokenGrouping.FormatValue(12d));
        Assert.Equal("—", DesignTokenGrouping.FormatValue(null));
    }
}
