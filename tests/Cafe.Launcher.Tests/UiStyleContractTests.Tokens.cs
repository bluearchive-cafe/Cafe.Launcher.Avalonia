using System.Xml.Linq;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

// Design-token and global style hygiene contracts: exact token values,
// typography rules, semantic color/icon scans, and shared button templates.
public sealed partial class UiStyleContractTests
{
    [Fact]
    public void DesignTokens_ContainExactSpacingRadiusIconAndControlHeightValues()
    {
        var document = XDocument.Load(TestRepository.FromHostRoot("App.axaml"));
        var resources = document
            .Descendants()
            .Where(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Key"))
            .GroupBy(
                element => element.Attributes().Single(attribute => attribute.Name.LocalName == "Key").Value,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().Value.Trim(),
                StringComparer.Ordinal);

        Assert.Equal("40", resources["Launcher.Spacing.Section"]);
        Assert.Equal("0", resources["Launcher.Spacing.None"]);
        Assert.Equal("16,0,4,0", resources["Launcher.Component.PathField.Padding"]);
        var pathFieldPadding = document
            .Descendants()
            .Single(element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "Launcher.Component.PathField.Padding"));
        Assert.Equal("Thickness", pathFieldPadding.Name.LocalName);
        Assert.Equal("8", resources["Launcher.Spacing.Thickness.Sm"]);
        Assert.Equal("12", resources["Launcher.Spacing.Thickness.Md"]);
        Assert.Equal("16", resources["Launcher.Spacing.Thickness.Lg"]);
        Assert.All(
            document
                .Descendants()
                .Where(element =>
                    element.Attributes().Any(attribute =>
                        attribute.Name.LocalName == "Key"
                        && (attribute.Value == "Launcher.Spacing.Thickness.Sm"
                            || attribute.Value == "Launcher.Spacing.Thickness.Md"
                            || attribute.Value == "Launcher.Spacing.Thickness.Lg"))),
            element => Assert.Equal("Thickness", element.Name.LocalName));
        Assert.Equal("8", resources["Launcher.Radius.Sm"]);
        Assert.Equal("8", resources["Launcher.Radius.Md"]);
        Assert.Equal("8", resources["Launcher.Radius.Lg"]);
        Assert.Equal("16", resources["Launcher.Icon.Sm"]);
        Assert.Equal("18", resources["Launcher.Icon.Md"]);
        Assert.Equal("20", resources["Launcher.Icon.Lg"]);
        Assert.Equal("22", resources["Launcher.Icon.Xl"]);
        Assert.Equal("24", resources["Launcher.Icon.Xxl"]);
        Assert.Equal("0", resources["Launcher.Control.Size.None"]);
        Assert.Equal("32", resources["Launcher.Control.Height.Setting"]);
        Assert.Equal("32", resources["Launcher.Control.Height.Dialog"]);
        Assert.Equal("48", resources["Launcher.Control.Height.Bottom"]);
        Assert.Equal("58", resources["Launcher.Control.Height.Launch"]);
    }

    [Fact]
    public void FontConfiguration_UsesLanguageFontsWithoutInterDefault()
    {
        var program = File.ReadAllText(TestRepository.FromHostRoot("Program.cs"));
        var project = XDocument.Load(TestRepository.FromHostRoot("Cafe.Launcher.csproj"));
        var packageNames = project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .ToArray();

        Assert.DoesNotContain(".WithInterFont()", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Avalonia.Fonts.Inter", packageNames);

        var appDocument = XDocument.Load(TestRepository.FromHostRoot("App.axaml"));
        var monospace = appDocument
            .Descendants()
            .Single(element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "Launcher.Typography.FontFamily.Monospace"));
        Assert.Equal("Consolas", monospace.Value.Trim());
    }

    [Fact]
    public void DesignGallery_EveryTokenFamilyHasLocalizedGroupName()
    {
        // Gallery group titles are composed at runtime as "designGroup" + family segment,
        // so static localization scans cannot see them; this locks the mapping instead.
        var application = XDocument.Load(TestRepository.FromHostRoot("App.axaml"));
        var tokenFamilies = application
            .Descendants()
            .Select(element => element.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "Key")?.Value)
            .Where(key => key is not null && key.StartsWith("Launcher.", StringComparison.Ordinal))
            .Select(key => key!.Split('.')[1])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var neutral = XDocument.Load(TestRepository.FromPresentationRoot("Resources/LauncherStrings.resx"));
        var groupKeys = neutral
            .Descendants()
            .Select(element => element.Attribute("name")?.Value)
            .Where(name => name is not null && name.StartsWith("designGroup", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var family in tokenFamilies)
        {
            Assert.True(
                groupKeys.Contains($"designGroup{family}"),
                $"Missing designGroup{family} resource for token family '{family}'.");
        }

        foreach (var family in Cafe.Launcher.UI.Helpers.DesignTokenGrouping.FamilyOrder)
        {
            Assert.Contains($"designGroup{family}", groupKeys);
        }
    }

    [Fact]
    public void DesignGallery_PreviewsDefaultButtonVariantsWithoutPaintingFakeStates()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/DesignGalleryOverlay.axaml"));
        var samples = document.Descendants().Where(element => HasClass(element, "gallery-button")).ToArray();
        Assert.Contains(samples, element => HasClass(element, "accent"));
        Assert.Contains(samples, element => (string?)element.Attribute("Theme") == "{StaticResource TransparentButton}");
        Assert.Contains(samples, element => (string?)element.Attribute("IsEnabled") == "False");
        Assert.DoesNotContain(samples, element => ((string?)element.Attribute("Classes"))?.Contains("state-", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Views_UseSemanticColorsAndTokenizedFluentIconSizes()
    {
        foreach (var relativePath in ViewFiles
                     .Concat(StyleFiles)
                     .Append("Views/MainWindowDebugOverlay.axaml"))
        {
            var text = File.ReadAllText(TestRepository.FromPresentationRoot(relativePath));

            // BoxShadow literals (Avalonia 12.1.1 has no TypeConverter) are
            // contract-exempt; see StyleControlAndDebugFiles_DoNotDefineRawColorValues.
            var colorText =
                relativePath.StartsWith("Views/Styles/", StringComparison.Ordinal)
                || relativePath == "Views/MainWindow.Styles.axaml"
                    ? string.Join(
                        Environment.NewLine,
                        text.Replace("\r", "", StringComparison.Ordinal)
                            .Split('\n')
                            .Where(line => !line.Contains("BoxShadow", StringComparison.Ordinal)))
                    : text;
            Assert.DoesNotMatch(DirectColorRegex(), colorText);
            Assert.DoesNotContain("\"Transparent\"", text, StringComparison.Ordinal);

            var document = XDocument.Parse(text);
            foreach (var icon in document.Descendants().Where(element => element.Name.LocalName == "FluentIcon"))
            {
                // 字形大小走 FontSize 语义令牌；盒尺寸由字形本身决定，不再写 Width/Height。
                Assert.Null(icon.Attribute("Width"));
                Assert.Null(icon.Attribute("Height"));
                var fontSize = icon.Attribute("FontSize")?.Value;
                if (fontSize is not null)
                {
                    Assert.Contains(fontSize, IconTokens);
                }
            }

            var spacingValues = document
                .Descendants()
                .SelectMany(element => element.Attributes())
                .Where(attribute => attribute.Name.LocalName is "Spacing" or "ColumnSpacing" or "RowSpacing")
                .Select(attribute => attribute.Value);

            Assert.All(
                spacingValues,
                value => Assert.True(
                    value.StartsWith("{StaticResource Launcher.Spacing", StringComparison.Ordinal),
                    $"Spacing value must use a LauncherSpacing token: {value}"));
        }
    }

    [Fact]
    public void StyleControlAndDebugFiles_DoNotDefineRawColorValues()
    {
        // Attribute-scoped scan: raw hex colors may only be defined in App.axaml
        // (design-system spec §2.1). BoxShadow literals are exempt — they carry no
        // TypeConverter in Avalonia 12.1.1 and are locked by token contract instead.
        var colorAttributeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Background",
            "Foreground",
            "BorderBrush",
            "Fill",
            "Stroke"
        };

        var controlFiles = Directory.GetFiles(
            TestRepository.FromPresentationRoot("Controls"),
            "*.axaml",
            SearchOption.TopDirectoryOnly);
        var colorScannedFiles = StyleFiles
            .Append("Views/MainWindowDebugOverlay.axaml")
            .Concat(controlFiles)
            .ToArray();

        foreach (var relativePath in colorScannedFiles)
        {
            var document = XDocument.Load(TestRepository.FromPresentationRoot(relativePath));
            var rawValues = document
                .Descendants()
                .SelectMany(element => element.Attributes())
                .Where(attribute =>
                    colorAttributeNames.Contains(attribute.Name.LocalName)
                    || attribute.Name.LocalName == "Color"
                        && attribute.Parent?.Name.LocalName == "GradientStop")
                .Select(attribute => attribute.Value)
                .Where(value => !value.StartsWith('{'))
                .ToArray();

            Assert.DoesNotContain(rawValues, value => DirectColorRegex().IsMatch(value));
        }
    }

    [Fact]
    public void Views_DoNotInlineReusableTypographyPaddingOrHeaderOffsets()
    {
        foreach (var relativePath in ViewFiles)
        {
            var document = XDocument.Load(TestRepository.FromPresentationRoot(relativePath));
            var attributes = document
                .Descendants()
                .SelectMany(element => element.Attributes())
                .ToArray();

            // 图标字形大小是图形属性而不是排版字号：FluentIcon 的 FontSize 由
            // Views_UseSemanticColorsAndTokenizedFluentIconSizes 单独约束为语义令牌。
            Assert.DoesNotContain(
                attributes,
                attribute => (attribute.Name.LocalName is "FontSize" or "FontWeight")
                    && attribute.Parent?.Name.LocalName != "FluentIcon");
            Assert.DoesNotContain(
                attributes,
                attribute => attribute.Name.LocalName == "Padding");
            Assert.DoesNotContain(
                attributes,
                attribute =>
                    attribute.Name.LocalName == "Margin"
                    && attribute.Value is "0,0,16,0" or "0,4,0,0");
        }
    }

    [Fact]
    public void IconGlyphs_ComeFromTheFluentGlyphsCatalog()
    {
        const string catalogPrefix = "{x:Static glyphs:FluentGlyphs.";
        const string bindingPrefix = "{Binding ";
        var scanned = new List<(string Path, string Value)>();

        // ADR-064：字形码点只在 FluentGlyphs 定义；XAML 引用常量或绑定视图模型的
        // 字形属性，不写 \uXXXX 字面量，也不混入第二套图标包。
        foreach (var relativePath in ProjectMarkupFiles())
        {
            var document = XDocument.Load(TestRepository.FromPresentationRoot(relativePath));
            var attributeForm = document
                .Descendants()
                .SelectMany(element => element.Attributes())
                .Where(attribute => attribute.Name.LocalName == "Glyph")
                .Select(attribute => (relativePath, attribute.Value));
            var setterForm = document
                .Descendants()
                .Where(element => element.Name.LocalName == "Setter")
                .SelectMany(element => element.Attributes()
                    .Where(attribute => attribute.Name.LocalName == "Property"
                        && attribute.Value == "Glyph")
                    .Select(attribute => element.Attribute("Value")?.Value))
                .Where(value => value is not null)
                .Select(value => (relativePath, value!));
            scanned.AddRange(attributeForm.Concat(setterForm));
        }

        // 反空转基线：全部业务图标都经此扫描；命中数掉到零即说明扫描域漂移。
        Assert.True(scanned.Count >= 100, $"Glyph scan lost its coverage: only {scanned.Count} occurrences.");

        var offenders = scanned
            .Where(item => !item.Value.StartsWith(catalogPrefix, StringComparison.Ordinal)
                && !item.Value.StartsWith(bindingPrefix, StringComparison.Ordinal))
            .Select(item => $"{item.Path}: Glyph=\"{item.Value}\"")
            .ToArray();

        Assert.Equal([], offenders);
    }

    [Fact]
    public void StyleFiles_UseStaticTokensForVisualValues()
    {
        var visualProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "Padding",
            "Margin",
            "BorderThickness",
            "Spacing",
            "ColumnSpacing",
            "RowSpacing",
            "Width",
            "Height",
            "MinWidth",
            "MaxWidth",
            "MinHeight",
            "MaxHeight",
            "CornerRadius",
            "FontSize",
            "FontWeight"
        };

        foreach (var relativePath in StyleFiles)
        {
            var document = XDocument.Load(TestRepository.FromPresentationRoot(relativePath));
            var setterValues = document
                .Descendants()
                .Where(element => element.Name.LocalName == "Setter")
                .SelectMany(element => element.Attributes()
                    .Where(attribute => attribute.Name.LocalName == "Property"
                        && visualProperties.Contains(attribute.Value))
                    .Select(attribute => (Property: attribute.Value, Value: element.Attribute("Value")?.Value)));
            // ControlTemplate 部件上的直接属性同样是视觉值；只扫 Setter 会让
            // 模板内裸间距/圆角漏网（TabItem 选中指示条曾在此漂移）。
            var attributeValues = document
                .Descendants()
                .SelectMany(element => element.Attributes())
                .Where(attribute => attribute.Name.Namespace != DesignNamespace
                    && visualProperties.Contains(attribute.Name.LocalName))
                .Select(attribute => (Property: attribute.Name.LocalName, Value: (string?)attribute.Value));
            var rawValues = setterValues
                .Concat(attributeValues)
                .Where(item => item.Value is not null
                    && !item.Value.StartsWith('{'))
                .ToArray();

            Assert.Empty(rawValues);
        }
    }

    [Fact]
    public void StyleFiles_AreExplicitAndParseable()
    {
        var discoveredFiles = Directory
            .GetFiles(TestRepository.FromPresentationRoot("Views"), "*.axaml", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".Styles.axaml", StringComparison.Ordinal)
                || path.Contains(
                    $"{Path.DirectorySeparatorChar}Styles{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(TestRepository.PresentationPath, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(StyleFiles.Order(StringComparer.Ordinal), discoveredFiles);
        Assert.All(StyleFiles, path => XDocument.Load(TestRepository.FromPresentationRoot(path)));
    }

    [Fact]
    public void IconOnlyButtons_ExposeAutomationNames()
    {
        foreach (var relativePath in ViewFiles)
        {
            var document = XDocument.Load(TestRepository.FromPresentationRoot(relativePath));
            var iconOnlyButtons = document
                .Descendants()
                .Where(element => element.Name.LocalName == "Button")
                .Where(element =>
                {
                    var children = element.Elements().ToList();
                    return children.Count == 1
                        && children[0].Name.LocalName == "FluentIcon";
                });

            Assert.All(
                iconOnlyButtons,
                button => Assert.Contains(
                    button.Attributes(),
                    attribute => attribute.Name.LocalName == "AutomationProperties.Name"));
        }
    }

    [Fact]
    public void DynamicAccent_DoesNotReplaceThemeSpecificInformationTextBrush()
    {
        var settingsViewModel = File.ReadAllText(TestRepository.FromPresentationRoot("Features/Settings/SettingsViewModel.cs"));

        Assert.DoesNotContain(
            "SetBrush(application, \"Launcher.Text.Info\"",
            settingsViewModel,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BoxShadowSetters_ConsumeElevationTokensInsteadOfLiterals()
    {
        var literalShadowValues = new List<string>();
        foreach (var relativePath in StyleFiles)
        {
            var document = XDocument.Load(TestRepository.FromPresentationRoot(relativePath));
            literalShadowValues.AddRange(document
                .Descendants()
                .Where(element => element.Name.LocalName == "Setter"
                    && element.Attribute("Property")?.Value == "BoxShadow")
                .Select(element => element.Attribute("Value")?.Value ?? string.Empty)
                .Where(value => value.Contains('#')));
        }

        Assert.Empty(literalShadowValues);
    }
}
