using System.Globalization;
using System.Xml.Linq;
using Avalonia.Media;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// WCAG AA contrast contract for design-token pairs (design-system spec §8, Q9/Q19).
/// Pairs are driven by a declarative list; the exemption list is explicit and
/// asserted to stay in sync with the token set. Light/Dark theme values come from
/// App.axaml ThemeDictionaries; translucent tokens are composited over the nominal
/// backdrop (white for Light, black for Dark).
/// </summary>
public sealed class DesignTokenContrastTests
{
    private readonly record struct TokenPair(
        string Name,
        string Foreground,
        string Background,
        double Minimum);

    private sealed record TokenColor(double Alpha, long Argb, bool Translucent);

    private sealed class TokenBrushes
    {
        public Dictionary<string, TokenColor> Root { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, TokenColor> Light { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, TokenColor> Dark { get; } = new(StringComparer.Ordinal);
    }

    // Text pairs: WCAG AA normal text >= 4.5:1.
    private static readonly TokenPair[] TextPairs =
    [
        new("panel primary text", "Launcher.Text.Primary", "Launcher.Color.Panel.Background", 4.5),
        new("card primary text", "Launcher.Text.Primary", "Launcher.Color.Card.Background", 4.5),
        new("content row primary text", "Launcher.Text.Primary", "Launcher.Color.Content.Row", 4.5),
        new("dialog primary text", "Launcher.Text.Primary", "Launcher.Color.Dialog.Background", 4.5),
        new("toast primary text", "Launcher.Text.Primary", "Launcher.Color.Toast.Background", 4.5),
        new("site button primary text", "Launcher.Text.Primary", "Launcher.Color.SiteButton.Background", 4.5),
        new("card secondary text", "Launcher.Text.Secondary", "Launcher.Color.Card.Background", 4.5),
        new("panel secondary text", "Launcher.Text.Secondary", "Launcher.Color.Panel.Background", 4.5),
        new("content row secondary text", "Launcher.Text.Secondary", "Launcher.Color.Content.Row", 4.5),
        new("dialog secondary text", "Launcher.Text.Secondary", "Launcher.Color.Dialog.Background", 4.5),
        new("field secondary text", "Launcher.Text.Secondary", "Launcher.Color.Field.Background", 4.5),
        new("toast secondary text", "Launcher.Text.Secondary", "Launcher.Color.Toast.Background", 4.5),
        new("warning surface secondary text", "Launcher.Text.Secondary", "Launcher.Color.Warning.Background", 4.5),
        new("notice surface secondary text", "Launcher.Text.Secondary", "Launcher.Color.Notice.Background", 4.5),
        new("dialog body text", "Launcher.Text.Body", "Launcher.Color.Dialog.Background", 4.5),
        new("toast body text", "Launcher.Text.Body", "Launcher.Color.Toast.Background", 4.5),
        new("notice body text", "Launcher.Text.Body", "Launcher.Color.Notice.Background", 4.5),
        new("card link text", "Launcher.Text.Link", "Launcher.Color.Card.Background", 4.5),
        new("info strip text", "Launcher.Text.Info", "Launcher.Color.Info.Background", 4.5),
        new("danger soft primary text", "Launcher.Text.Primary", "Launcher.Color.Danger.Soft", 4.5),
        new("warning surface primary text", "Launcher.Text.Primary", "Launcher.Color.Warning.Background", 4.5),
        new("error action label", "Launcher.Color.OnError", "Launcher.Color.Error", 4.5),
        new("error action label (hover)", "Launcher.Color.OnError", "Launcher.Color.Error.Hover", 4.5),
        new("error action label (pressed)", "Launcher.Color.OnError", "Launcher.Color.Error.Pressed", 4.5),
        new("success status text", "Launcher.Color.Success", "Launcher.Color.Dialog.Background", 4.5)
    ];

    // Non-text/UI pairs: WCAG AA >= 3:1 (progress, icons, severity indicators, borders).
    // Danger fills are consumed by the exempt chrome-close (Text.OnChrome, spec §8) and
    // by the error-filled danger-action (OnError/Error label pairs above); the static
    // white-on-danger guard lives in OnPrimaryContrastRule_WhiteOnDangerFillsMeetsAa...
    private static readonly TokenPair[] UiPairs =
    [
        new("danger icon on card", "Launcher.Color.Danger", "Launcher.Color.Card.Background", 3.0),
        new("danger icon on dialog", "Launcher.Color.Danger", "Launcher.Color.Dialog.Background", 3.0),
        new("toast success severity", "Launcher.Color.Success", "Launcher.Color.Toast.Background", 3.0),
        new("toast warning severity", "Launcher.Color.Warning", "Launcher.Color.Toast.Background", 3.0),
        new("toast error severity", "Launcher.Color.Danger", "Launcher.Color.Toast.Background", 3.0),
        new("toast info severity", "Launcher.Color.Info", "Launcher.Color.Toast.Background", 3.0),
        new("field border on light/dark field", "Launcher.Color.Field.Border", "Launcher.Color.Field.Background", 3.0)
    ];

    // Explicit exemption list (design-system spec §8): over-wallpaper chrome/banner
    // content and runtime-dynamic scheme roles are covered by rule/walkthrough, not
    // static pair assertions. M3 adds runtime on-color luminance for scheme roles.
    private static readonly string[] ExemptedKeys =
    [
        "Launcher.Text.OnChrome",
        "Launcher.Text.OnChrome.Muted",
        "Launcher.Text.OnDark",
        "Launcher.Color.Chrome.Hover",
        "Launcher.Color.Chrome.Pressed",
        "Launcher.Color.TitleBar.Gradient",
        "Launcher.Color.Overlay.Scrim.Sm",
        "Launcher.Color.Overlay.Scrim.Md",
        "Launcher.Color.Overlay.Scrim.Lg",
        "Launcher.Color.Primary",
        "Launcher.Color.Primary.Hover",
        "Launcher.Color.Primary.Pressed",
        "Launcher.Color.Primary.Soft",
        "Launcher.Color.Primary.Border",
        "Launcher.Color.OnPrimary",
        "Launcher.Color.Carousel.Dot.Active",
        "Launcher.Color.Carousel.Dot.Inactive"
    ];

    [Fact]
    public void TextTokenPairs_MeetWcagAa_AcrossLightAndDark()
    {
        var app = LoadTokenBrushes();

        foreach (var theme in new[] { "Light", "Dark" })
        {
            foreach (var pair in TextPairs)
            {
                AssertPair(app, theme, pair);
            }
        }
    }

    [Fact]
    public void UiTokenPairs_MeetWcagAa_AcrossLightAndDark()
    {
        var app = LoadTokenBrushes();

        foreach (var theme in new[] { "Light", "Dark" })
        {
            foreach (var pair in UiPairs)
            {
                AssertPair(app, theme, pair);
            }
        }
    }

    [Fact]
    public void ContrastExemptions_AreExplicitlyListedAndPresentInTokens()
    {
        var app = LoadTokenBrushes();
        var definedKeys = new HashSet<string>(
            XDocument.Load(ProjectFile("App.axaml"))
                .Descendants()
                .Where(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Key"))
                .Select(element => element.Attributes()
                    .Single(attribute => attribute.Name.LocalName == "Key")
                    .Value),
            StringComparer.Ordinal);

        Assert.NotEmpty(ExemptedKeys);
        foreach (var key in ExemptedKeys)
        {
            // Dynamic scheme keys (Primary family, OnPrimary) carry no static hex value
            // and therefore never enter the parsed dictionaries; presence is asserted
            // against the XAML x:Key definitions instead.
            Assert.True(
                definedKeys.Contains(key),
                $"Exempted token '{key}' is not defined in App.axaml; refresh the exemption list in DesignTokenContrastTests.");
        }

        // Scheme roles (Primary family / OnPrimary) must stay runtime-bound (dynamic),
        // otherwise the M3 scheme pipeline cannot override them.
        Assert.False(
            app.Root.ContainsKey("Launcher.Color.Primary"),
            "Primary must stay dynamic (SystemAccentColor), not a static hex value.");
    }

    [Fact]
    public void OnPrimaryContrastRule_WhiteOnDangerFillsMeetsAa_AfterStaticAdjustments()
    {
        var app = LoadTokenBrushes();
        var white = new Color(255, 255, 255, 255);

        foreach (var fillKey in new[] { "Launcher.Color.Danger", "Launcher.Color.Danger.Hover", "Launcher.Color.Danger.Pressed" })
        {
            var fill = app.Root[fillKey];
            var ratio = ColorUtils.GetContrastRatio(
                white,
                CompositeOverBackdrop(fill.Alpha, fill.Argb, backdropIsWhite: true));
            Assert.True(
                ratio >= 4.5,
                $"{fillKey} must keep white labels readable (4.5:1), got {ratio:F2}:1.");
        }
    }

    [Fact]
    public void ColorUtils_ContrastRatio_MatchesWcagReferencePoints()
    {
        var white = new Color(255, 255, 255, 255);
        var black = new Color(255, 0, 0, 0);

        Assert.Equal(21.0, ColorUtils.GetContrastRatio(white, black), 2);
        Assert.Equal(4.54, ColorUtils.GetContrastRatio(white, new Color(255, 0x76, 0x76, 0x76)), 2);
    }

    private static void AssertPair(TokenBrushes app, string theme, TokenPair pair)
    {
        var foreground = Resolve(app, theme, pair.Foreground);
        var background = Resolve(app, theme, pair.Background);

        var foregroundColor = CompositeOverBackdrop(
            foreground.Alpha,
            foreground.Argb,
            backdropIsWhite: theme == "Light");
        var backgroundColor = CompositeOverBackdrop(
            background.Alpha,
            background.Argb,
            backdropIsWhite: theme == "Light");
        var ratio = ColorUtils.GetContrastRatio(foregroundColor, backgroundColor);

        Assert.True(
            ratio >= pair.Minimum,
            $"[{theme}] {pair.Name}: {pair.Foreground} on {pair.Background} is {ratio:F2}:1, expected >= {pair.Minimum}:1.");
    }

    private static TokenColor Resolve(TokenBrushes app, string theme, string key)
    {
        var dictionary = theme == "Light" ? app.Light : app.Dark;
        if (dictionary.TryGetValue(key, out var themed))
        {
            return themed;
        }

        if (app.Root.TryGetValue(key, out var root))
        {
            return root;
        }

        Assert.Fail($"Token '{key}' is not defined in App.axaml.");
        return new TokenColor(1.0, 0, false);
    }

    private static Color CompositeOverBackdrop(double alpha, long argb, bool backdropIsWhite)
    {
        var r = (byte)((argb >> 16) & 0xFF);
        var g = (byte)((argb >> 8) & 0xFF);
        var b = (byte)(argb & 0xFF);
        if (alpha >= 1.0)
        {
            return new Color(255, r, g, b);
        }

        byte backdrop = backdropIsWhite ? (byte)255 : (byte)0;
        static byte Blend(byte value, byte backing, double opacity) =>
            (byte)Math.Round((opacity * value) + ((1.0 - opacity) * backing));

        return new Color(
            255,
            Blend(r, backdrop, alpha),
            Blend(g, backdrop, alpha),
            Blend(b, backdrop, alpha));
    }

    private static TokenBrushes LoadTokenBrushes()
    {
        var document = XDocument.Load(ProjectFile("App.axaml"));
        var result = new TokenBrushes();

        foreach (var dictionary in document.Descendants().Where(element => element.Name.LocalName == "ResourceDictionary"))
        {
            var themeKey = dictionary.Attributes()
                .SingleOrDefault(attribute => attribute.Name.LocalName == "Key")
                ?.Value;
            var target = themeKey switch
            {
                "Light" => result.Light,
                "Dark" => result.Dark,
                null or "Default" => result.Root,
                _ => null
            };
            if (target is null)
            {
                continue;
            }

            foreach (var brush in dictionary.Elements().Where(element =>
                         element.Name.LocalName is "SolidColorBrush" or "LinearGradientBrush"))
            {
                var key = brush.Attributes()
                    .SingleOrDefault(attribute => attribute.Name.LocalName == "Key")
                    ?.Value;
                if (key is null || !key.StartsWith("Launcher.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (brush.Name.LocalName == "LinearGradientBrush")
                {
                    target[key] = new TokenColor(1.0, 0x00000000L, false);
                    continue;
                }

                var colorText = brush.Attribute("Color")?.Value;
                if (colorText is null || !TryParseArgb(colorText, out var argb, out var alpha))
                {
                    continue;
                }

                target[key] = new TokenColor(alpha, argb, alpha < 1.0);
            }
        }

        return result;
    }

    private static bool TryParseArgb(string text, out long argb, out double alpha)
    {
        argb = 0;
        alpha = 1.0;
        var hex = text.TrimStart('#');
        if (hex.Length == 8)
        {
            if (!long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out argb))
            {
                return false;
            }

            alpha = ((argb >> 24) & 0xFF) / 255.0;
            return true;
        }

        if (hex.Length == 6 && long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out argb))
        {
            argb |= 0xFF000000L;
            return true;
        }

        // Dynamic brush (e.g. SystemAccentColor reference) carries no static hex value.
        return false;
    }

    private static string ProjectFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null
               && !File.Exists(Path.Combine(current.FullName, "Cafe.Launcher.Avalonia.slnx")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine(current!.FullName, "src", "Cafe.Launcher.Avalonia", relativePath);
    }
}
