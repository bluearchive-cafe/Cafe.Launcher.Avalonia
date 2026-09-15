using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

// Core partial: shared scan targets (style/view file lists, icon tokens) and
// the cross-cutting assertion helpers used by every UiStyleContractTests volume.
public sealed partial class UiStyleContractTests
{
    // CrashReportWindow is hosted by the isolated crash-reporter process. Its visual
    // language has its own Crash.* token family, so it deliberately stays outside the
    // main-window style contracts. Every other top-level View is discovered below.
    private static readonly HashSet<string> ExemptViewFiles =
    [
        "Views/CrashReportWindow.axaml"
    ];

    private static readonly string[] StyleFiles =
        ["Views/MainWindow.Styles.axaml", .. FindXamlFiles("Views/Styles", SearchOption.AllDirectories)];

    private static readonly string[] ViewFiles = FindXamlFiles("Views", SearchOption.TopDirectoryOnly)
        .Except(StyleFiles, StringComparer.Ordinal)
        .Except(ExemptViewFiles, StringComparer.Ordinal)
        .ToArray();

    [Fact]
    public void ScanTargets_CoverEveryTopLevelViewFile()
    {
        // AUD-TEST-006：新增顶层 View 必须自动进入扫描域；只有专用崩溃窗口可
        // 显式豁免。扫描目标从磁盘发现，避免手写白名单随拆分漂移。
        var actualViews = FindXamlFiles("Views", SearchOption.TopDirectoryOnly);
        var declared = ViewFiles
            .Concat(StyleFiles)
            .ToHashSet(StringComparer.Ordinal);
        var undeclared = actualViews
            .Where(relative => !declared.Contains(relative) && !ExemptViewFiles.Contains(relative))
            .ToArray();

        Assert.Empty(undeclared);
        Assert.Contains("Views/MainWindow.axaml", ViewFiles);
        Assert.Contains("Views/MainWindow.Styles.axaml", StyleFiles);
    }

    private static string[] FindXamlFiles(string relativeDirectory, SearchOption searchOption)
    {
        var projectRoot = TestLocalizationHelper.FindProjectRoot();
        return Directory
            .GetFiles(ProjectFile(relativeDirectory), "*.axaml", searchOption)
            .Select(path => Path.GetRelativePath(projectRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The complete product-markup scan domain for contracts that apply equally to
    /// views and reusable controls. Callers receive project-relative, ordinally
    /// ordered paths so diagnostics remain stable across machines.
    /// </summary>
    private static string[] ProjectMarkupFiles() =>
    [
        .. FindXamlFiles("Views", SearchOption.AllDirectories),
        .. FindXamlFiles("Controls", SearchOption.AllDirectories)
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
