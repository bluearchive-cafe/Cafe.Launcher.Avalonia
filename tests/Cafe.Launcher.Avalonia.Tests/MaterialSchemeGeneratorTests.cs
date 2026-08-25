using Avalonia.Media;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// M3 dynamic-scheme tests (spec §3.4, P1 plan M3): reference values anchored to
/// the M0 spike fixture, on-colour luminance rule, dual neutral strategy and
/// non-wallpaper seed equivalence.
/// </summary>
public sealed class MaterialSchemeGeneratorTests
{
    private static readonly Color DefaultSeed = Color.Parse("#FF6750A4");

    [Fact]
    public void CreateScheme_Seed6750A4Light_MatchesM0ReferenceFixture()
    {
        var scheme = MaterialSchemeGenerator.CreateScheme(
            DefaultSeed,
            ThemeColorVariants.TonalSpot,
            isDark: false);

        Assert.Equal("#FF65558F", ToHex(scheme.Primary)); // tone 40, M0 fixture
        Assert.Equal("#FFE9DDFF", ToHex(scheme.PrimaryContainer));
        Assert.Equal("#FFE8DEF8", ToHex(scheme.SecondaryContainer));
        Assert.Equal("#FFFDF7FF", ToHex(scheme.Surface));
        Assert.Equal("#FFFFFFFF", ToHex(scheme.OnPrimary));
        Assert.Equal("#FF625B71", ToHex(scheme.Secondary));
        Assert.Equal("#FF7E5260", ToHex(scheme.Tertiary));
        Assert.Equal("#FF7A757F", ToHex(scheme.Outline));
    }

    [Fact]
    public void CreateScheme_Seed6750A4Dark_MatchesM0ReferenceFixture()
    {
        var scheme = MaterialSchemeGenerator.CreateScheme(
            DefaultSeed,
            ThemeColorVariants.TonalSpot,
            isDark: true);

        Assert.Equal("#FFCFBDFE", ToHex(scheme.Primary));
        Assert.Equal("#FF4D3D75", ToHex(scheme.PrimaryContainer));
        Assert.Equal("#FF4A4458", ToHex(scheme.SecondaryContainer));
        Assert.Equal("#FF141218", ToHex(scheme.Surface));
        Assert.Equal("#FF36275D", ToHex(scheme.OnPrimary));
        Assert.Equal("#FFCBC2DB", ToHex(scheme.Secondary));
    }

    [Fact]
    public void CreateScheme_AllEightVariants_ProduceOpaqueRoleSets()
    {
        foreach (var variant in new[]
                 {
                     ThemeColorVariants.TonalSpot,
                     ThemeColorVariants.Vibrant,
                     ThemeColorVariants.Expressive,
                     ThemeColorVariants.Fidelity,
                     ThemeColorVariants.Content,
                     ThemeColorVariants.Monochrome,
                     ThemeColorVariants.Neutral,
                     ThemeColorVariants.Rainbow
                 })
        {
            foreach (var isDark in new[] { false, true })
            {
                var scheme = MaterialSchemeGenerator.CreateScheme(DefaultSeed, variant, isDark);

                Assert.Equal(255, scheme.Primary.Alpha);
                Assert.Equal(255, scheme.Secondary.Alpha);
                Assert.Equal(255, scheme.Tertiary.Alpha);
                Assert.NotEqual(scheme.Primary.Value, scheme.Surface.Value);
            }
        }
    }

    [Fact]
    public void CreateScheme_NonWallpaperSeeds_AreEquivalentShapeAndDeterministic()
    {
        var seeds = new[]
        {
            Color.Parse("#FF0078D4"), // system accent-like blue
            Color.Parse("#FF6750A4"), // default blue (LauncherConstants.DefaultThemeColor)
            Color.Parse("#FFFF00FF"), // custom bright magenta
            Color.Parse("#FFE8B8A0") // wallpaper-warm custom colour
        };

        foreach (var seed in seeds)
        {
            var first = MaterialSchemeGenerator.CreateScheme(seed, ThemeColorVariants.TonalSpot, isDark: false);
            var second = MaterialSchemeGenerator.CreateScheme(seed, ThemeColorVariants.TonalSpot, isDark: false);

            Assert.Equal(first.Primary.Value, second.Primary.Value);
            Assert.Equal(first.SecondaryContainer.Value, second.SecondaryContainer.Value);
            Assert.Equal(first.SurfaceTint.Value, second.SurfaceTint.Value);
            Assert.Equal(255, first.Primary.Alpha);
        }
    }

    [Fact]
    public void BuildRoleBrushes_BrandBlueStrategy_OmitSurfaceNeutralRoles()
    {
        var scheme = MaterialSchemeGenerator.CreateScheme(
            DefaultSeed,
            ThemeColorVariants.TonalSpot,
            isDark: false);

        var brushes = MaterialSchemeGenerator.BuildRoleBrushes(scheme, seedFollowingNeutrals: false);

        Assert.False(brushes.ContainsKey("Launcher.Color.Surface"));
        Assert.False(brushes.ContainsKey("Launcher.Color.OnSurface"));
        Assert.False(brushes.ContainsKey("Launcher.Color.Outline"));
        Assert.True(brushes.ContainsKey("Launcher.Color.Secondary"));
        Assert.True(brushes.ContainsKey("Launcher.Color.TertiaryContainer"));
    }

    [Fact]
    public void BuildRoleBrushes_SeedFollowingStrategy_IncludeSurfaceNeutralRoles()
    {
        var scheme = MaterialSchemeGenerator.CreateScheme(
            DefaultSeed,
            ThemeColorVariants.TonalSpot,
            isDark: false);

        var brushes = MaterialSchemeGenerator.BuildRoleBrushes(scheme, seedFollowingNeutrals: true);

        Assert.True(brushes.ContainsKey("Launcher.Color.Surface"));
        Assert.True(brushes.ContainsKey("Launcher.Color.OnSurface"));
        Assert.True(brushes.ContainsKey("Launcher.Color.Outline"));
    }

    [Fact]
    public void BuildRoleBrushes_KeepsPreM3OverrideSubsetAndDropsInfo()
    {
        var scheme = MaterialSchemeGenerator.CreateScheme(
            DefaultSeed,
            ThemeColorVariants.TonalSpot,
            isDark: false);

        var brushes = MaterialSchemeGenerator.BuildRoleBrushes(scheme, seedFollowingNeutrals: false);

        // Pre-M3 accent family subset.
        Assert.True(brushes.ContainsKey("Launcher.Color.Primary"));
        Assert.True(brushes.ContainsKey("Launcher.Color.Primary.Hover"));
        Assert.True(brushes.ContainsKey("Launcher.Color.Primary.Pressed"));
        Assert.True(brushes.ContainsKey("Launcher.Color.Primary.Soft"));
        Assert.True(brushes.ContainsKey("Launcher.Color.Primary.Border"));
        Assert.True(brushes.ContainsKey("Launcher.Color.OnPrimary"));
        Assert.True(brushes.ContainsKey("Launcher.Color.FocusRing"));
        Assert.True(brushes.ContainsKey("Launcher.Color.Carousel.Dot.Active"));
        Assert.True(brushes.ContainsKey("Launcher.Color.Button.Flat.Hover"));
        Assert.True(brushes.ContainsKey("Launcher.Color.Button.Flat.Pressed"));
        Assert.True(brushes.ContainsKey("Launcher.Color.Info.Background"));

        // Business colours are not dynamic-per-spec (spec §3.4).
        Assert.False(brushes.ContainsKey("Launcher.Color.Info"));
        Assert.False(brushes.ContainsKey("Launcher.Color.Success"));
    }

    [Fact]
    public void OnPrimaryColor_UsesHigherContrastOnColor()
    {
        var darkScheme = MaterialSchemeGenerator.CreateScheme(
            DefaultSeed,
            ThemeColorVariants.TonalSpot,
            isDark: false);
        var lightScheme = MaterialSchemeGenerator.CreateScheme(
            DefaultSeed,
            ThemeColorVariants.TonalSpot,
            isDark: true);

        var lightBrushes = MaterialSchemeGenerator.BuildRoleBrushes(darkScheme, seedFollowingNeutrals: false);
        var darkBrushes = MaterialSchemeGenerator.BuildRoleBrushes(lightScheme, seedFollowingNeutrals: false);

        // Light scheme primary is dark (tone 40) -> readable on-colour is white.
        Assert.Equal(Colors.White, lightBrushes["Launcher.Color.OnPrimary"].Color);
        // Dark scheme primary is light (tone 80) -> readable on-colour is near-black.
        Assert.Equal(Color.FromRgb(0x12, 0x18, 0x20), darkBrushes["Launcher.Color.OnPrimary"].Color);
    }

    [Theory]
    [InlineData("#FFFFFFFF", true)]
    [InlineData("#FFB8B8B8", true)]
    [InlineData("#FFE5484D", true)]
    [InlineData("#FF000000", false)]
    public void GetReadableOnAccentColor_SelectsHigherContrastText(string hex, bool expectsDark)
    {
        var source = Color.Parse(hex);

        var onColor = ColorUtils.GetReadableOnAccentColor(source);

        if (expectsDark)
        {
            Assert.Equal(Color.FromRgb(0x12, 0x18, 0x20), onColor);
        }
        else
        {
            Assert.Equal(Colors.White, onColor);
        }
    }

    [Fact]
    public void GetReadableOnAccentColor_MediumGray_UsesHigherContrastDarkText()
    {
        // Dark text provides the higher contrast for this medium-gray fill.
        var boundary = Color.Parse("#FFB8B8B8");

        Assert.Equal(Color.FromRgb(0x12, 0x18, 0x20), ColorUtils.GetReadableOnAccentColor(boundary));
    }

    private static string ToHex(MaterialColorUtilities.Utils.ArgbColor color)
    {
        var avalonia = MaterialColorMapper.ToAvaloniaColor(color);
        return $"#{avalonia.A:X2}{avalonia.R:X2}{avalonia.G:X2}{avalonia.B:X2}";
    }
}
