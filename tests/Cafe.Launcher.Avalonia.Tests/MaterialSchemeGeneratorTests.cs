using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
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

    /// <summary>
    /// 钉住默认表与 App.axaml 声明值的一致性：默认表是 Brand Blue 策略下
    /// 「恢复声明值」的唯一事实来源，若与 XAML 漂移，上面的恢复断言会退化为
    /// 用代码常量自证。此处直接解析 App.axaml 的 Light/Dark ThemeDictionaries
    /// 取真值，改令牌颜色而忘记同步默认表时在此失败。
    /// </summary>
    [Fact]
    public void NeutralDefaults_MatchDeclaredAppXamlThemeValues()
    {
        var themeValues = LoadAppXamlThemeSolidBrushes();

        foreach (var (key, light, dark) in MaterialSchemeGenerator.DialogSurfaceDefaults.Concat(MaterialSchemeGenerator.NeutralContentDefaults))
        {
            Assert.True(
                themeValues.TryGetValue(("Light", key), out var declaredLight),
                $"{key} is not declared in App.axaml Light theme; the defaults table references a token that no longer exists.");
            Assert.True(
                themeValues.TryGetValue(("Dark", key), out var declaredDark),
                $"{key} is not declared in App.axaml Dark theme; the defaults table references a token that no longer exists.");
            Assert.True(
                Color.TryParse(declaredLight, out var lightColor) && lightColor == Color.Parse(light),
                $"{key} light default {light} drifted from App.axaml value {declaredLight}.");
            Assert.True(
                Color.TryParse(declaredDark, out var darkColor) && darkColor == Color.Parse(dark),
                $"{key} dark default {dark} drifted from App.axaml value {declaredDark}.");
        }
    }

    private static Dictionary<(string Theme, string Key), string> LoadAppXamlThemeSolidBrushes()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null
               && !File.Exists(Path.Combine(current.FullName, "Cafe.Launcher.Avalonia.slnx")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        var appXamlPath = Path.Combine(
            current!.FullName, "src", "Cafe.Launcher.Avalonia", "App.axaml");

        var values = new Dictionary<(string, string), string>();
        var document = XDocument.Load(appXamlPath);
        foreach (var dictionary in document.Descendants()
                     .Where(element => element.Name.LocalName == "ResourceDictionary"
                         && element.Attributes().Any(attribute => attribute.Name.LocalName == "Key")))
        {
            var theme = dictionary.Attributes()
                .Single(attribute => attribute.Name.LocalName == "Key")
                .Value;
            if (theme is not ("Light" or "Dark"))
            {
                continue;
            }

            foreach (var brush in dictionary.Elements()
                         .Where(element => element.Name.LocalName == "SolidColorBrush"))
            {
                var key = brush.Attributes()
                    .SingleOrDefault(attribute => attribute.Name.LocalName == "Key")
                    ?.Value;
                var color = brush.Attribute("Color")?.Value;
                if (key is not null && color is not null)
                {
                    values[(theme, key)] = color;
                }
            }
        }

        return values;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildRoleBrushes_SeedFollowingStrategy_MapsContentSurfacesToNeutralLadder(bool isDark)
    {
        var scheme = MaterialSchemeGenerator.CreateScheme(DefaultSeed, ThemeColorVariants.TonalSpot, isDark);
        var brushes = MaterialSchemeGenerator.BuildRoleBrushes(scheme, seedFollowingNeutrals: true, isDark);

        Assert.Equal(
            (isDark ? scheme.SurfaceContainerHighest : scheme.SurfaceContainerLow).Value,
            MaterialColorMapper.ToArgbColor(brushes["Launcher.Color.Card.Background"].Color).Value);
        Assert.Equal(
            scheme.SurfaceContainer.Value,
            MaterialColorMapper.ToArgbColor(brushes["Launcher.Color.Content.Row"].Color).Value);
        Assert.Equal(
            (isDark ? scheme.SurfaceContainer : scheme.Surface).Value,
            MaterialColorMapper.ToArgbColor(brushes["Launcher.Color.SiteButton.Background"].Color).Value);
        Assert.Equal(
            (isDark ? scheme.SurfaceContainerHigh : scheme.Surface).Value,
            MaterialColorMapper.ToArgbColor(brushes["Launcher.Color.Toast.Background"].Color).Value);
        foreach (var borderKey in new[]
                 {
                     "Launcher.Color.Card.Border",
                     "Launcher.Color.Button.Border",
                     "Launcher.Color.SiteButton.Border"
                 })
        {
            Assert.Equal(
                scheme.OutlineVariant.Value,
                MaterialColorMapper.ToArgbColor(brushes[borderKey].Color).Value);
        }
    }

    [Fact]
    public void BuildRoleBrushes_SeedFollowingStrategy_ContentSurfacesHueFollowSeed()
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
            blueBrushes["Launcher.Color.Card.Background"].Color,
            greenBrushes["Launcher.Color.Card.Background"].Color);
        Assert.NotEqual(
            blueBrushes["Launcher.Color.Content.Row"].Color,
            greenBrushes["Launcher.Color.Content.Row"].Color);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildRoleBrushes_BrandBlueStrategy_RestoresDeclaredContentSurfaces(bool isDark)
    {
        var scheme = MaterialSchemeGenerator.CreateScheme(
            Color.Parse("#FF2E9E46"),
            ThemeColorVariants.TonalSpot,
            isDark);
        var brushes = MaterialSchemeGenerator.BuildRoleBrushes(scheme, seedFollowingNeutrals: false, isDark);

        foreach (var (key, light, dark) in MaterialSchemeGenerator.NeutralSurfaceDefaults)
        {
            Assert.Equal(Color.Parse(isDark ? dark : light), brushes[key].Color);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildRoleBrushes_SeedFollowingStrategy_ComposesOnSurfaceStateLayers(bool isDark)
    {
        var scheme = MaterialSchemeGenerator.CreateScheme(DefaultSeed, ThemeColorVariants.TonalSpot, isDark);
        var brushes = MaterialSchemeGenerator.BuildRoleBrushes(scheme, seedFollowingNeutrals: true, isDark);
        var onSurface = MaterialColorMapper.ToAvaloniaColor(scheme.OnSurface);

        // M3 状态层盘：hover 8%（0x14）/ pressed 12%（0x1F）的 onSurface 预合成，
        // 供 radio 图标底盘消费（与 Launcher.StateLayer.* 不透明度令牌一致）。
        Assert.Equal(
            Color.FromArgb(0x14, onSurface.R, onSurface.G, onSurface.B),
            brushes["Launcher.Color.StateLayer.OnSurface.Hover"].Color);
        Assert.Equal(
            Color.FromArgb(0x1F, onSurface.R, onSurface.G, onSurface.B),
            brushes["Launcher.Color.StateLayer.OnSurface.Pressed"].Color);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildRoleBrushes_BrandBlueStrategy_RestoresDeclaredStateLayers(bool isDark)
    {
        var scheme = MaterialSchemeGenerator.CreateScheme(
            Color.Parse("#FF2E9E46"),
            ThemeColorVariants.TonalSpot,
            isDark);
        var brushes = MaterialSchemeGenerator.BuildRoleBrushes(scheme, seedFollowingNeutrals: false, isDark);

        foreach (var (key, light, dark) in MaterialSchemeGenerator.StateLayerDefaults)
        {
            Assert.Equal(Color.Parse(isDark ? dark : light), brushes[key].Color);
        }
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BuildRoleBrushes_FocusRing_ContrastsNeutralSurfaces(bool isDark, bool seedFollowingNeutrals)
    {
        foreach (var variant in new[]
                 {
                     ThemeColorVariants.TonalSpot,
                     ThemeColorVariants.Expressive,
                     ThemeColorVariants.Fidelity,
                     ThemeColorVariants.Monochrome
                 })
        {
            foreach (var seed in new[]
                     {
                         DefaultSeed,
                         Color.Parse("#FFF4E5A1"),
                         Color.Parse("#FF101010")
                     })
            {
                var scheme = MaterialSchemeGenerator.CreateScheme(seed, variant, isDark);
                var brushes = MaterialSchemeGenerator.BuildRoleBrushes(
                    scheme,
                    seedFollowingNeutrals,
                    isDark);
                var focusRing = brushes["Launcher.Color.FocusRing"].Color;

                Assert.Equal(255, focusRing.A);
                Assert.Equal(MaterialColorMapper.ToAvaloniaColor(scheme.Primary), focusRing);
                foreach (var surfaceKey in new[]
                         {
                             "Launcher.Color.Dialog.Background",
                             "Launcher.Color.Card.Background",
                             "Launcher.Color.Content.Row",
                             "Launcher.Color.Field.Background",
                             "Launcher.Color.SiteButton.Background",
                             "Launcher.Color.Toast.Background"
                         })
                {
                    double contrast = ColorUtils.GetContrastRatio(focusRing, brushes[surfaceKey].Color);
                    Assert.True(
                        contrast >= 3.0,
                        $"{variant}/{isDark}/{seedFollowingNeutrals}/{surfaceKey}: focus contrast is {contrast:F2}:1.");
                }
            }
        }
    }

    [Fact]
    public void OnPrimaryColor_UsesSchemeRolePair()
    {
        var lightScheme = MaterialSchemeGenerator.CreateScheme(
            DefaultSeed,
            ThemeColorVariants.TonalSpot,
            isDark: false);
        var darkScheme = MaterialSchemeGenerator.CreateScheme(
            DefaultSeed,
            ThemeColorVariants.TonalSpot,
            isDark: true);

        var lightBrushes = MaterialSchemeGenerator.BuildRoleBrushes(lightScheme, seedFollowingNeutrals: false);
        var darkBrushes = MaterialSchemeGenerator.BuildRoleBrushes(darkScheme, seedFollowingNeutrals: false);

        // The property that matters is readability, not which source supplied the
        // value: onPrimary must stay legible on primary in both brightnesses.
        foreach (var brushes in new[] { lightBrushes, darkBrushes })
        {
            var ratio = ColorUtils.GetContrastRatio(
                brushes["Launcher.Color.OnPrimary"].Color,
                brushes["Launcher.Color.Primary"].Color);
            Assert.True(
                ratio >= 4.5,
                $"OnPrimary must stay readable on Primary; contrast is {ratio:F2}:1.");
        }

        // Light scheme primary is dark (tone 40) -> readable on-colour is white.
        Assert.Equal(Colors.White, lightBrushes["Launcher.Color.OnPrimary"].Color);
        // Dark scheme primary is light (tone 80) -> the on-colour is the scheme's
        // paired tonal role, not an independent black/white pick.
        Assert.Equal(
            MaterialColorMapper.ToAvaloniaColor(darkScheme.OnPrimary),
            darkBrushes["Launcher.Color.OnPrimary"].Color);
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
                foreach (var backgroundKey in new[]
                         {
                             "Launcher.Color.Dialog.Background",
                             "Launcher.Color.Field.Background",
                             "Launcher.Color.Card.Background",
                             "Launcher.Color.Content.Row",
                             "Launcher.Color.SiteButton.Background",
                             "Launcher.Color.Toast.Background"
                         })
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildRoleBrushes_ActionStates_PreserveTextContrastAndMoveTowardOnColor(bool isDark)
    {
        // M3 state-layer recipe: hover 8% / pressed 12% of the component's
        // content colour layered over the container. Rest contrast is the WCAG
        // contract; pressed stays above the large-text floor while moving
        // monotonically toward the on-colour.
        foreach (var variant in new[] { ThemeColorVariants.TonalSpot, ThemeColorVariants.Fidelity, ThemeColorVariants.Monochrome })
        {
            var scheme = MaterialSchemeGenerator.CreateScheme(DefaultSeed, variant, isDark);
            var brushes = MaterialSchemeGenerator.BuildRoleBrushes(scheme, true, isDark);
            foreach (var (fillKey, textKey) in new[]
                     {
                         ("Launcher.Color.Primary", "Launcher.Color.OnPrimary"),
                         ("Launcher.Color.Error", "Launcher.Color.OnError"),
                         ("Launcher.Color.SecondaryContainer", "Launcher.Color.OnSecondaryContainer")
                     })
            {
                var textColor = brushes[textKey].Color;
                double normalContrast = ColorUtils.GetContrastRatio(brushes[fillKey].Color, textColor);
                double hoverContrast = ColorUtils.GetContrastRatio(brushes[fillKey + ".Hover"].Color, textColor);
                double pressedContrast = ColorUtils.GetContrastRatio(brushes[fillKey + ".Pressed"].Color, textColor);
                Assert.True(normalContrast >= 4.5,
                    $"{variant}/{isDark}/{fillKey}: rest={normalContrast:F2}:1 below WCAG normal-text floor");
                Assert.True(normalContrast > hoverContrast && hoverContrast > pressedContrast,
                    $"{variant}/{isDark}/{fillKey}: rest={normalContrast:F2}, hover={hoverContrast:F2}, pressed={pressedContrast:F2}");
                Assert.True(pressedContrast >= 3.0,
                    $"{variant}/{isDark}/{fillKey}: pressed={pressedContrast:F2}:1 below large-text floor");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildRoleBrushes_TextActionLabel_PrimaryMeetsAaOnDialogSurfaces(bool isDark)
    {
        // M3 text button（如向导“跳过引导”）以 Primary 作标签直接压在对话框
        // 底色上，普通字号文本须满足 WCAG AA 4.5:1；两种中性策略的对话底面
        // （Brand Blue 声明默认值 / seed-following SurfaceContainerHigh）都不得跌破。
        foreach (var variant in new[]
                 {
                     ThemeColorVariants.TonalSpot, ThemeColorVariants.Vibrant,
                     ThemeColorVariants.Expressive, ThemeColorVariants.Fidelity,
                     ThemeColorVariants.Content, ThemeColorVariants.Monochrome,
                     ThemeColorVariants.Neutral, ThemeColorVariants.Rainbow
                 })
        {
            foreach (var seed in new[]
                     {
                         DefaultSeed,
                         Color.Parse("#FFF4E5A1"),
                         Color.Parse("#FF101010")
                     })
            {
                foreach (var seedFollowingNeutrals in new[] { false, true })
                {
                    var scheme = MaterialSchemeGenerator.CreateScheme(seed, variant, isDark);
                    var brushes = MaterialSchemeGenerator.BuildRoleBrushes(scheme, seedFollowingNeutrals, isDark);
                    double ratio = ColorUtils.GetContrastRatio(
                        brushes["Launcher.Color.Primary"].Color,
                        brushes["Launcher.Color.Dialog.Background"].Color);
                    Assert.True(
                        ratio >= 4.5,
                        $"{variant}/{isDark}/{seedFollowingNeutrals}: primary on dialog = {ratio:F2}:1.");
                }
            }
        }
    }

    private static string ToHex(MaterialColorUtilities.Utils.ArgbColor color)
    {
        var avalonia = MaterialColorMapper.ToAvaloniaColor(color);
        return $"#{avalonia.A:X2}{avalonia.R:X2}{avalonia.G:X2}{avalonia.B:X2}";
    }
}
