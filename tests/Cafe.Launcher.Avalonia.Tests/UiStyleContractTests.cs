using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

// Core partial: shared scan targets (style/view file lists, icon tokens) and
// the cross-cutting assertion helpers used by every UiStyleContractTests volume.
public sealed partial class UiStyleContractTests
{
    private static readonly string[] StyleFiles =
    [
        "Views/MainWindow.Styles.axaml",
        "Views/Styles/Diagnostics.axaml",
        "Views/Styles/RemoteContent.axaml",
        "Views/Styles/SetupWizard.axaml",
        "Views/Styles/Toast.axaml",
        "Views/Styles/DialogSurface.axaml"
    ];

    private static readonly string[] ViewFiles =
    [
        "Views/MainWindow.axaml",
        "Views/MainWindowSettingsOverlay.axaml",
        "Views/DesignGalleryOverlay.axaml",
        "Views/SettingsGeneralSection.axaml",
        "Views/SettingsGameSection.axaml",
        "Views/SettingsDownloadNetworkSection.axaml",
        "Views/SettingsAppearanceSection.axaml",
        "Views/SettingsAdvancedSection.axaml",
        "Views/SettingsAboutSection.axaml",
        "Views/MainWindowDialogsOverlay.axaml",
        "Views/MainWindowLogViewerOverlay.axaml",
        "Views/MainWindowToastOverlay.axaml",
        "Views/SetupWizardOverlay.axaml"
    ];

    private static readonly HashSet<string> IconTokens =
    [
        "{StaticResource Launcher.Icon.Sm}",
        "{StaticResource Launcher.Icon.Md}",
        "{StaticResource Launcher.Icon.Lg}",
        "{StaticResource Launcher.Icon.Xl}",
        "{StaticResource Launcher.Icon.Xxl}"
    ];

    private static IReadOnlyList<string> FindFixedEnglishLiterals(
        XDocument document,
        string source)
    {
        HashSet<string> userFacingAttributes = new(StringComparer.Ordinal)
        {
            "AutomationProperties.Name",
            "CancelText",
            "CloseToolTip",
            "Content",
            "ConfirmText",
            "Description",
            "Header",
            "Message",
            "OffContent",
            "OnContent",
            "PlaceholderText",
            "Text",
            "Title",
            "ToolTip.Tip"
        };
        XNamespace designNamespace = "http://schemas.microsoft.com/expression/blend/2008";

        return (document.Root?.DescendantsAndSelf() ?? [])
            .Where(element => element.Name.Namespace != designNamespace)
            .SelectMany(element =>
                element.Attributes()
                    .Where(attribute => attribute.Name.Namespace != designNamespace)
                    .Where(attribute => userFacingAttributes.Contains(attribute.Name.LocalName))
                    .Where(attribute => !attribute.Value.TrimStart().StartsWith('{'))
                    .Where(attribute => attribute.Value.Any(char.IsAsciiLetter))
                    .Select(attribute =>
                        $"{source}:{((IXmlLineInfo)attribute).LineNumber} "
                        + $"{attribute.Name.LocalName}=\"{attribute.Value}\"")
                    .Concat(
                        element.Name.LocalName is "TextBlock" or "Button" or "MenuItem"
                            ? element.Nodes()
                                .OfType<XText>()
                                .Where(node => !string.IsNullOrWhiteSpace(node.Value))
                                .Where(node => node.Value.Any(char.IsAsciiLetter))
                                .Select(node =>
                                    $"{source}:{((IXmlLineInfo)node).LineNumber} "
                                    + node.Value.Trim())
                            : []))
            .ToList();
    }

    private static bool HasClass(XElement element, string className) =>
        element.Attribute("Classes")?.Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(className, StringComparer.Ordinal) == true;

    private static string ReadThemeBrushColor(XDocument document, string theme, string key)
    {
        const string avaloniaNamespace = "https://github.com/avaloniaui";
        var xKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
        var brush = document
            .Descendants(XName.Get("SolidColorBrush", avaloniaNamespace))
            .Single(element =>
                element.Attribute(xKey)?.Value == key
                && element
                    .Ancestors(XName.Get("ResourceDictionary", avaloniaNamespace))
                    .Any(ancestor => ancestor.Attribute(xKey)?.Value == theme));
        return brush.Attribute("Color")?.Value
            ?? throw new InvalidOperationException($"'{key}' declares no Color in the {theme} theme.");
    }

    private static XElement FindMotionOverlay(XDocument document, string isOpenBinding)
    {
        var controlsNamespace = document.Root?.GetNamespaceOfPrefix("controls")
            ?? throw new InvalidOperationException("The controls XML namespace is missing.");
        return document
            .Descendants()
            .Single(element =>
                HasClass(element, "motion-overlay")
                && element.Attribute(controlsNamespace + "MotionVisibility.IsOpen")?.Value
                    == isOpenBinding);
    }

    private static void AssertHasLocalTranslateTransform(XElement element)
    {
        var renderTransform = Assert.Single(
            element.Elements(),
            child => child.Name.LocalName.EndsWith(".RenderTransform", StringComparison.Ordinal));
        Assert.Single(
            renderTransform.Elements(),
            child => child.Name.LocalName == "TranslateTransform");
    }

    private static void AssertOverlayBrushAnimation(
        XDocument document,
        string selector,
        string expectedDuration)
    {
        var animation = GetMotionAnimation(document, selector);
        Assert.Equal(expectedDuration, animation.Attribute("Duration")?.Value);
        Assert.Equal("Forward", animation.Attribute("FillMode")?.Value);
        Assert.Equal("{StaticResource Launcher.Motion.Easing.Enter}", animation.Attribute("Easing")?.Value);

        var keyFrames = GetAnimationKeyFrames(animation);
        AssertAnimationProperty(
            keyFrames,
            "Background",
            "{StaticResource Launcher.Color.Transparent}",
            "{StaticResource Launcher.Color.Overlay.Scrim.Md}");
        AssertAnimationProperty(keyFrames, "Opacity", null, null);
    }

    private static void AssertOverlayBrushExitAnimation(XDocument document, string selector)
    {
        var animation = GetMotionAnimation(document, selector);
        Assert.Equal("{StaticResource Launcher.Motion.Duration.Fast}", animation.Attribute("Duration")?.Value);
        Assert.Equal("Forward", animation.Attribute("FillMode")?.Value);
        Assert.Equal("{StaticResource Launcher.Motion.Easing.Exit}", animation.Attribute("Easing")?.Value);

        var keyFrames = GetAnimationKeyFrames(animation);
        AssertAnimationProperty(
            keyFrames,
            "Background",
            "{StaticResource Launcher.Color.Overlay.Scrim.Md}",
            "{StaticResource Launcher.Color.Transparent}");
        AssertAnimationProperty(keyFrames, "Opacity", null, null);
    }

    private static void AssertMotionAnimation(
        XDocument document,
        string selector,
        string expectedDuration,
        string? expectedStartOffset,
        string expectedStartAxis = "TranslateTransform.Y",
        bool expectsOpacity = true)
    {
        var animation = GetMotionAnimation(document, selector);
        Assert.Equal(expectedDuration, animation.Attribute("Duration")?.Value);
        Assert.Equal("Forward", animation.Attribute("FillMode")?.Value);
        Assert.Equal("{StaticResource Launcher.Motion.Easing.Enter}", animation.Attribute("Easing")?.Value);
        Assert.Null(animation.Attribute("Delay"));

        var keyFrames = GetAnimationKeyFrames(animation);
        AssertAnimationProperty(
            keyFrames,
            "Opacity",
            expectsOpacity ? "0" : null,
            expectsOpacity ? "1" : null);

        if (expectedStartOffset is null)
        {
            Assert.DoesNotContain(
                keyFrames.SelectMany(pair => pair.Value.Elements()),
                element => element.Attribute("Property")?.Value == expectedStartAxis);
            return;
        }

        Assert.Equal(
            expectedStartOffset,
            keyFrames["0%"]
                .Elements()
                .Single(element => element.Attribute("Property")?.Value == expectedStartAxis)
                .Attribute("Value")?.Value);
        Assert.Equal(
            "0",
            keyFrames["100%"]
                .Elements()
                .Single(element => element.Attribute("Property")?.Value == expectedStartAxis)
                .Attribute("Value")?.Value);
    }

    private static void AssertExitMotionAnimation(
        XDocument document,
        string selector,
        string? expectedEndOffset,
        string expectedEndAxis = "TranslateTransform.Y",
        bool expectsOpacity = true)
    {
        var animation = GetMotionAnimation(document, selector);
        Assert.Equal("{StaticResource Launcher.Motion.Duration.Fast}", animation.Attribute("Duration")?.Value);
        Assert.Equal("Forward", animation.Attribute("FillMode")?.Value);
        Assert.Equal("{StaticResource Launcher.Motion.Easing.Exit}", animation.Attribute("Easing")?.Value);

        var keyFrames = GetAnimationKeyFrames(animation);
        AssertAnimationProperty(
            keyFrames,
            "Opacity",
            expectsOpacity ? "1" : null,
            expectsOpacity ? "0" : null);

        if (expectedEndOffset is null)
        {
            Assert.DoesNotContain(
                keyFrames.SelectMany(pair => pair.Value.Elements()),
                element => element.Attribute("Property")?.Value == expectedEndAxis);
            return;
        }

        Assert.Equal(
            "0",
            keyFrames["0%"]
                .Elements()
                .Single(element => element.Attribute("Property")?.Value == expectedEndAxis)
                .Attribute("Value")?.Value);
        Assert.Equal(
            expectedEndOffset,
            keyFrames["100%"]
                .Elements()
                .Single(element => element.Attribute("Property")?.Value == expectedEndAxis)
                .Attribute("Value")?.Value);
    }

    private static XElement GetMotionAnimation(XDocument document, string selector)
    {
        var style = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == selector);
        return style
            .Descendants()
            .Single(element => element.Name.LocalName == "Animation");
    }

    private static Dictionary<string, XElement> GetAnimationKeyFrames(XElement animation)
    {
        var keyFrames = animation
            .Elements()
            .Where(element => element.Name.LocalName == "KeyFrame")
            .ToDictionary(
                element => element.Attribute("Cue")?.Value ?? "",
                element => element,
                StringComparer.Ordinal);
        Assert.Equal(2, keyFrames.Count);
        return keyFrames;
    }

    private static void AssertAnimationProperty(
        IReadOnlyDictionary<string, XElement> keyFrames,
        string property,
        string? expectedStartValue,
        string? expectedEndValue)
    {
        var setters = keyFrames
            .SelectMany(pair => pair.Value.Elements())
            .Where(element => element.Attribute("Property")?.Value == property)
            .ToList();
        if (expectedStartValue is null || expectedEndValue is null)
        {
            Assert.Empty(setters);
            return;
        }

        Assert.Equal(
            expectedStartValue,
            keyFrames["0%"]
                .Elements()
                .Single(element => element.Attribute("Property")?.Value == property)
                .Attribute("Value")?.Value);
        Assert.Equal(
            expectedEndValue,
            keyFrames["100%"]
                .Elements()
                .Single(element => element.Attribute("Property")?.Value == property)
                .Attribute("Value")?.Value);
    }

    private static void AssertOrdered(string text, params string[] values)
    {
        var previousIndex = -1;
        foreach (var value in values)
        {
            var index = text.IndexOf(value, StringComparison.Ordinal);
            Assert.True(index > previousIndex, $"{value} must appear after the previous item.");
            previousIndex = index;
        }
    }

    private static string ProjectFile(string relativePath) =>
        Path.Combine(TestLocalizationHelper.FindProjectRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    [GeneratedRegex("#[0-9A-Fa-f]{6,8}", RegexOptions.CultureInvariant)]
    private static partial Regex DirectColorRegex();

    private static IReadOnlyDictionary<string, string> GetStyleSetters(
        XDocument document,
        string selector)
    {
        var matchingStyle = document
            .Descendants()
            .SingleOrDefault(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == selector);
        matchingStyle ??= StyleFiles
            .Select(path => XDocument.Load(ProjectFile(path)))
            .SelectMany(styleDocument => styleDocument.Descendants())
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == selector);

        return matchingStyle
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => element.Attribute("Property")?.Value
                    ?? throw new InvalidOperationException($"Setter in {selector} has no Property."),
                element => element.Attribute("Value")?.Value
                    ?? throw new InvalidOperationException($"Setter in {selector} has no Value."),
                StringComparer.Ordinal);
    }
}
