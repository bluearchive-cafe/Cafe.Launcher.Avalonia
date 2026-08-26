using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

// M2 contract additions: new-family token existence, legacy flat-key regression
// gate, and the "{StaticResource} for static families" resource-reference rule
// (design-system spec §3.1 / §3.2). See design-system-spec.md §8.
public sealed partial class UiStyleContractTests
{
    [Fact]
    public void DesignTokens_NewFamilies_DeclareExpectedScaleValues()
    {
        var app = XDocument.Load(ProjectFile("App.axaml"));
        var keyed = app
            .Descendants()
            .Where(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Key"))
            .GroupBy(
                element => element.Attributes()
                    .Single(attribute => attribute.Name.LocalName == "Key")
                    .Value,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);

        foreach (var (key, value) in new Dictionary<string, string>(StringComparer.Ordinal)
                 {
                     ["Launcher.Elevation.Shadow.None"] = "0 0 0 0 #00000000",
                     ["Launcher.Elevation.Shadow.Sm"] = "0 4 8 0 #26000000",
                     ["Launcher.Elevation.Shadow.Md"] = "0 12 32 0 #33000000",
                     ["Launcher.Elevation.Shadow.Lg"] = "0 22 55 0 #66000000",
                     ["Launcher.Layout.Banner.Height"] = "220",
                     ["Launcher.Layout.Banner.EdgeGradient.Width"] = "180",
                     ["Launcher.Component.Banner.Indicator.CornerRadius"] = "1",
                     ["Launcher.Layout.Window.Width"] = "1300",
                     ["Launcher.Layout.Window.Height"] = "754",
                     ["Launcher.Layout.Settings.MaxWidth"] = "960",
                     ["Launcher.Layout.Settings.MaxHeight"] = "620",
                     ["Launcher.Layout.SetupWizard.Width"] = "920",
                     ["Launcher.Layout.SetupWizard.Height"] = "560",
                     ["Launcher.Layout.ResourcePanel.Width"] = "720",
                     ["Launcher.Layout.ResourcePanel.Height"] = "592",
                     ["Launcher.Layout.DesignGallery.Width"] = "1000",
                     ["Launcher.Layout.DesignGallery.Height"] = "680"
                 })
        {
            Assert.Equal(value, keyed[key].Value.Trim());
        }

        foreach (var (key, value) in new Dictionary<string, string>(StringComparer.Ordinal)
                 {
                     ["Launcher.StateLayer.Hover"] = "0.08",
                     ["Launcher.StateLayer.Focus"] = "0.12",
                     ["Launcher.StateLayer.Pressed"] = "0.16",
                     ["Launcher.StateLayer.Selected"] = "0.24",
                     ["Launcher.Spacing.Thickness.Xxl"] = "24",
                     ["Launcher.Spacing.Thickness.Section"] = "40",
                     ["Launcher.Component.Banner.Indicator.Margin"] = "0,0,0,16",
                     ["Launcher.Typography.LetterSpacing.None"] = "0",
                     ["Launcher.Typography.LetterSpacing.Sm"] = "0.5",
                     ["Launcher.Typography.LetterSpacing.Md"] = "1",
                     ["Launcher.Typography.LetterSpacing.Lg"] = "2"
                 })
        {
            Assert.Equal(value, keyed[key].Value.Trim());
        }

        Assert.Equal("#40000000", keyed["Launcher.Color.Overlay.Scrim.Sm"].Attribute("Color")?.Value);
        Assert.Equal("#99000000", keyed["Launcher.Color.Overlay.Scrim.Md"].Attribute("Color")?.Value);
        Assert.Equal("#E6000000", keyed["Launcher.Color.Overlay.Scrim.Lg"].Attribute("Color")?.Value);
        Assert.Equal(
            "#FFFFFFFF",
            keyed["Launcher.Color.Carousel.Dot.Active"].Attribute("Color")?.Value);
        Assert.Equal(
            "#99D0D4DA",
            keyed["Launcher.Color.Carousel.Dot.Inactive"].Attribute("Color")?.Value);
    }

    [Fact]
    public void DesignTokens_StaticFamilies_AreReferencedWithStaticResourceOnly()
    {
        var staticFamilies = "Launcher\\.(Spacing|Radius|Typography|Icon|Control|Component|Layout|Motion|Elevation|StateLayer)";
        var dynamicStaticReference = new Regex($"\\{{\\s*DynamicResource\\s+{staticFamilies}", RegexOptions.CultureInvariant);

        var files = Directory.GetFiles(ProjectFile("Views"), "*.axaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(ProjectFile("Controls"), "*.axaml", SearchOption.AllDirectories));

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.False(
                dynamicStaticReference.IsMatch(text),
                $"Static-family tokens must use {{StaticResource}}, not {{DynamicResource}}: {file}");
        }
    }

    [Fact]
    public void DesignTokens_LegacyFlatKeys_AreAbsentFromMarkup()
    {
        var legacyReference = new Regex(
            @"\bLauncher[A-Z][A-Za-z0-9]*",
            RegexOptions.CultureInvariant);
        var files = Directory.GetFiles(ProjectFile("Views"), "*.axaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(ProjectFile("Controls"), "*.axaml", SearchOption.AllDirectories))
            .Append(ProjectFile("App.axaml"));

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (Match match in legacyReference.Matches(text))
            {
                var preceded = match.Index > 0 ? text[match.Index - 1] : '\0';
                var followed = match.Index + match.Length < text.Length ? text[match.Index + match.Length] : '\0';
                if (preceded is ':' or '.')
                {
                    // Namespace-qualified type or member access (e.g. constants:LauncherConstants).
                    continue;
                }

                if (followed is '_')
                {
                    // Event-handler member name (e.g. LauncherVersionChip_OnPointerPressed).
                    continue;
                }

                Assert.True(
                    match.Value == "LauncherBorderButtonTemplate",
                    $"Legacy flat token key '{match.Value}' found in {file} — rename gate violated.");
            }
        }
    }
}
