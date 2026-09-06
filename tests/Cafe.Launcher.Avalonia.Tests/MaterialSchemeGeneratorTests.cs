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
    public void BuildRoleBrushes_BrandBlueStrategy_ProvidesFixedSurfaceNeutralRoles()
    {
        var scheme = MaterialSchemeGenerator.CreateScheme(
            DefaultSeed,
            ThemeColorVariants.TonalSpot,
            isDark: false);

        var brushes = MaterialSchemeGenerator.BuildRoleBrushes(scheme, seedFollowingNeutrals: false);

        Assert.True(brushes.ContainsKey("Launcher.Color.Surface"));
        Assert.True(brushes.ContainsKey("Launcher.Color.OnSurface"));
        Assert.True(brushes.ContainsKey("Launcher.Color.Outline"));
        Assert.NotEqual(
            scheme.Surface.Value,
            MaterialColorMapper.ToArgbColor(brushes["Launcher.Color.Surface"].Color).Value);
        Assert.True(brushes.ContainsKey("Launcher.Color.Secondary"));
        Assert.True(brushes.ContainsKey("Launcher.Color.TertiaryContainer"));
        Assert.True(brushes.ContainsKey("Launcher.Color.PrimaryContainer"));
        Assert.True(brushes.ContainsKey("Launcher.Color.OnPrimaryContainer"));

        // Visible solid surfaces stay on the declared neutral business tokens
        // under the Brand Blue strategy (Q13): the dialog family is written with
        // the declared App.axaml defaults so no seed-following override lingers.
        Assert.Equal(
            Color.Parse("#FFFFFFFF"),
            brushes["Launcher.Color.Dialog.Background"].Color);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildRoleBrushes_BrandBlueStrategy_RestoresDeclaredDialogSurfaceFamily(bool isDark)
    {
        var scheme = MaterialSchemeGenerator.CreateScheme(
            DefaultSeed,
            ThemeColorVariants.TonalSpot,
            isDark);

        var brushes = MaterialSchemeGenerator.BuildRoleBrushes(
            scheme,
            seedFollowingNeutrals: false,
            isDark);

        foreach (var (key, light, dark) in MaterialSchemeGenerator.DialogSurfaceDefaults.Concat(MaterialSchemeGenerator.NeutralContentDefaults))
        {
            Assert.True(brushes.ContainsKey(key));
            Assert.Equal(
                Color.Parse(isDark ? dark : light),
                brushes[key].Color);
        }
    }

    [Fact]
    public void BuildRoleBrushes_SeedFollowingStrategy_UsesSelectedNeutralRoles()
    {
        var scheme = MaterialSchemeGenerator.CreateScheme(
            DefaultSeed,
            ThemeColorVariants.TonalSpot,
            isDark: false);

        var brushes = MaterialSchemeGenerator.BuildRoleBrushes(scheme, seedFollowingNeutrals: true);

        Assert.True(brushes.ContainsKey("Launcher.Color.Surface"));
        Assert.True(brushes.ContainsKey("Launcher.Color.OnSurface"));
        Assert.True(brushes.ContainsKey("Launcher.Color.Outline"));
        Assert.True(brushes.ContainsKey("Launcher.Color.PrimaryContainer"));
        Assert.True(brushes.ContainsKey("Launcher.Color.OnPrimaryContainer"));

        // Seed-following dyes the visible solid dialog surfaces from the neutral
        // scheme: Dialog.Background uses the elevated dialog container role.
        Assert.True(brushes.ContainsKey("Launcher.Color.Dialog.Background"));
        Assert.Equal(
            scheme.SurfaceContainerHigh.Value,
            MaterialColorMapper.ToArgbColor(brushes["Launcher.Color.Dialog.Background"].Color).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildRoleBrushes_SeedFollowingStrategy_TintsEntireDialogSurfaceFamily(bool isDark)
    {
        var scheme = MaterialSchemeGenerator.CreateScheme(
            DefaultSeed,
            ThemeColorVariants.TonalSpot,
            isDark);

        var brushes = MaterialSchemeGenerator.BuildRoleBrushes(
            scheme,
            seedFollowingNeutrals: true,
            isDark);

        // The whole dialog family follows the scheme's neutral surface ladder:
        // Background, footer and header share SurfaceContainerHigh; close states
        // apply foreground state layers over the container.
        Assert.Equal(
            scheme.SurfaceContainerHigh.Value,
            MaterialColorMapper.ToArgbColor(brushes["Launcher.Color.Dialog.Background"].Color).Value);
        Assert.Equal(
            scheme.SurfaceContainerHigh.Value,
            MaterialColorMapper.ToArgbColor(brushes["Launcher.Color.Dialog.Footer"].Color).Value);
        Assert.Equal(
            scheme.SurfaceContainerHigh.Value,
            MaterialColorMapper.ToArgbColor(brushes["Launcher.Color.Dialog.Header"].Color).Value);
        Assert.Equal(
            brushes["Launcher.Color.Dialog.Background"].Color,
            brushes["Launcher.Color.Dialog.Header"].Color);
        Assert.NotEqual(
            brushes["Launcher.Color.Dialog.Header"].Color,
            brushes["Launcher.Color.Dialog.Close.Hover"].Color);
        Assert.NotEqual(
            brushes["Launcher.Color.Dialog.Close.Hover"].Color,
            brushes["Launcher.Color.Dialog.Close.Pressed"].Color);
    }

    [Fact]
    public void BuildRoleBrushes_SeedFollowingStrategy_DialogFamilyHueFollowsSeed()
    {
        var blue = MaterialSchemeGenerator.CreateScheme(
            Color.Parse("#FF2E7DF6"),
            ThemeColorVariants.TonalSpot,
            isDark: false);
        var green = MaterialSchemeGenerator.CreateScheme(
            Color.Parse("#FF2E9E46"),
            ThemeColorVariants.TonalSpot,
            isDark: false);

        var blueBrushes = MaterialSchemeGenerator.BuildRoleBrushes(blue, seedFollowingNeutrals: true);
        var greenBrushes = MaterialSchemeGenerator.BuildRoleBrushes(green, seedFollowingNeutrals: true);

        Assert.NotEqual(
            blueBrushes["Launcher.Color.Dialog.Header"].Color,
            greenBrushes["Launcher.Color.Dialog.Header"].Color);
        Assert.NotEqual(
            blueBrushes["Launcher.Color.Dialog.Close.Pressed"].Color,
            greenBrushes["Launcher.Color.Dialog.Close.Pressed"].Color);
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
        Assert.Equal(
            Color.Parse("#FFFFFFFF"),
            brushes["Launcher.Color.Carousel.Dot.Active"].Color);
        Assert.True(brushes.ContainsKey("Launcher.Color.Button.Flat.Hover"));
        Assert.True(brushes.ContainsKey("Launcher.Color.Button.Flat.Pressed"));
        // Info.Background is a fixed business surface (spec §3.4): the generator
        // no longer tints it with the accent, so no key is produced.
        Assert.False(brushes.ContainsKey("Launcher.Color.Info.Background"));
        Assert.True(brushes.ContainsKey("Launcher.Color.SecondaryContainer.Hover"));
        Assert.True(brushes.ContainsKey("Launcher.Color.SecondaryContainer.Pressed"));

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

    [Fact]
    public void BuildRoleBrushes_ProvidesReadableDynamicErrorRoles()
    {
        foreach (var isDark in new[] { false, true })
        {
            var scheme = MaterialSchemeGenerator.CreateScheme(
                DefaultSeed,
                ThemeColorVariants.TonalSpot,
                isDark);
            var brushes = MaterialSchemeGenerator.BuildRoleBrushes(
                scheme,
                seedFollowingNeutrals: false,
                isDark);

            Assert.Contains("Launcher.Color.Error", brushes.Keys);
            Assert.Contains("Launcher.Color.Error.Hover", brushes.Keys);
            Assert.Contains("Launcher.Color.Error.Pressed", brushes.Keys);
            Assert.Contains("Launcher.Color.OnError", brushes.Keys);

            foreach (var state in new[] { "", ".Hover", ".Pressed" })
            {
                var ratio = ColorUtils.GetContrastRatio(
                    brushes["Launcher.Color.OnError"].Color,
                    brushes[$"Launcher.Color.Error{state}"].Color);
                Assert.True(ratio >= 4.5, $"Error{state} contrast was {ratio:F2}:1.");
            }
        }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildRoleBrushes_SeedFollowingNeutrals_PreserveTextAndFieldContrast(bool isDark)
    {
        foreach (var variant in new[]
                 {
                     ThemeColorVariants.TonalSpot, ThemeColorVariants.Vibrant,
                     ThemeColorVariants.Expressive, ThemeColorVariants.Fidelity,
                     ThemeColorVariants.Content, ThemeColorVariants.Monochrome,
                     ThemeColorVariants.Neutral, ThemeColorVariants.Rainbow
                 })
        {
            foreach (var hex in new[] { "#2E7DF6", "#FF0000", "#00FF00", "#6750A4", "#000000", "#FFFFFF" })
            {
                var scheme = MaterialSchemeGenerator.CreateScheme(Color.Parse(hex), variant, isDark);
                var brushes = MaterialSchemeGenerator.BuildRoleBrushes(scheme, true, isDark);
                Assert.Equal(MaterialColorMapper.ToAvaloniaColor(scheme.OnSurface), brushes["Launcher.Text.Primary"].Color);
                Assert.Equal(MaterialColorMapper.ToAvaloniaColor(scheme.OnSurfaceVariant), brushes["Launcher.Text.Secondary"].Color);
                foreach (var backgroundKey in new[] { "Launcher.Color.Dialog.Background", "Launcher.Color.Field.Background" })
                {
                    foreach (var textKey in new[] { "Launcher.Text.Primary", "Launcher.Text.Secondary", "Launcher.Text.Body" })
                    {
                        double ratio = ColorUtils.GetContrastRatio(brushes[textKey].Color, brushes[backgroundKey].Color);
                        Assert.True(ratio >= 4.5, $"{variant}/{hex}/{isDark}: {textKey} on {backgroundKey} = {ratio:F2}:1");
                    }
                }

                double borderRatio = ColorUtils.GetContrastRatio(
                    brushes["Launcher.Color.Field.Border"].Color,
                    brushes["Launcher.Color.Field.Background"].Color);
                Assert.True(borderRatio >= 3, $"{variant}/{hex}/{isDark}: field border = {borderRatio:F2}:1");
            }
        }
    }

    private static string ToHex(MaterialColorUtilities.Utils.ArgbColor color)
    {
        var avalonia = MaterialColorMapper.ToAvaloniaColor(color);
        return $"#{avalonia.A:X2}{avalonia.R:X2}{avalonia.G:X2}{avalonia.B:X2}";
    }
}
