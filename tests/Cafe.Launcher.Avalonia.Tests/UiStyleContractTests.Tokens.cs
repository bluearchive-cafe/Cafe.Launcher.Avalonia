using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

// Design-token and global style hygiene contracts: exact token values,
// typography rules, semantic color/icon scans, and shared button templates.
public sealed partial class UiStyleContractTests
{
    [Fact]
    public void DesignTokens_ContainExactSpacingRadiusIconAndControlHeightValues()
    {
        var document = XDocument.Load(ProjectFile("App.axaml"));
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
        Assert.Equal("12", resources["Launcher.Radius.Md"]);
        Assert.Equal("16", resources["Launcher.Radius.Lg"]);
        Assert.Equal("16", resources["Launcher.Icon.Sm"]);
        Assert.Equal("18", resources["Launcher.Icon.Md"]);
        Assert.Equal("20", resources["Launcher.Icon.Lg"]);
        Assert.Equal("22", resources["Launcher.Icon.Xl"]);
        Assert.Equal("24", resources["Launcher.Icon.Xxl"]);
        Assert.Equal("0", resources["Launcher.Control.Size.None"]);
        Assert.Equal("36", resources["Launcher.Control.Height.Setting"]);
        Assert.Equal("42", resources["Launcher.Control.Height.Dialog"]);
        Assert.Equal("48", resources["Launcher.Control.Height.Bottom"]);
        Assert.Equal("58", resources["Launcher.Control.Height.Launch"]);
    }

    [Fact]
    public void TypographyTokens_ContainExactScaleWeightAndFamilyValues()
    {
        var appDocument = XDocument.Load(ProjectFile("App.axaml"));
        var resources = appDocument
            .Descendants()
            .Where(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Key"))
            .GroupBy(
                element => element.Attributes().Single(attribute => attribute.Name.LocalName == "Key").Value,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().Value.Trim(),
                StringComparer.Ordinal);

        Assert.Equal("11", resources["Launcher.Typography.FontSize.Label.Sm"]);
        Assert.Equal("12", resources["Launcher.Typography.FontSize.Label.Md"]);
        Assert.Equal("13", resources["Launcher.Typography.FontSize.Body.Sm"]);
        Assert.Equal("14", resources["Launcher.Typography.FontSize.Body.Md"]);
        Assert.Equal("15", resources["Launcher.Typography.FontSize.Body.Lg"]);
        Assert.Equal("16", resources["Launcher.Typography.FontSize.Title.Md"]);
        Assert.Equal("17", resources["Launcher.Typography.FontSize.Title.Lg"]);
        Assert.Equal("18", resources["Launcher.Typography.FontSize.Headline.Md"]);
        Assert.Equal("19", resources["Launcher.Typography.FontSize.Headline.Lg"]);
        Assert.Equal("22", resources["Launcher.Typography.FontSize.Display"]);
        Assert.Equal("Normal", resources["Launcher.Typography.FontWeight.Normal"]);
        Assert.Equal("SemiBold", resources["Launcher.Typography.FontWeight.Strong"]);
        Assert.Equal("Consolas", resources["Launcher.Typography.FontFamily.Monospace"]);

        var stylesDocument = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var typographySetters = stylesDocument
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value is "FontSize" or "FontWeight")
            .ToArray();

        Assert.NotEmpty(typographySetters);
        Assert.All(
            typographySetters,
            setter => Assert.StartsWith(
                "{StaticResource Launcher.Typography",
                setter.Attribute("Value")?.Value,
                StringComparison.Ordinal));
    }

    [Fact]
    public void FontConfiguration_UsesLanguageFontsWithoutInterDefault()
    {
        var program = File.ReadAllText(ProjectFile("Program.cs"));
        var project = XDocument.Load(ProjectFile("Cafe.Launcher.Avalonia.csproj"));
        var packageNames = project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .ToArray();

        Assert.DoesNotContain(".WithInterFont()", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Avalonia.Fonts.Inter", packageNames);

        var appDocument = XDocument.Load(ProjectFile("App.axaml"));
        var monospace = appDocument
            .Descendants()
            .Single(element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "Launcher.Typography.FontFamily.Monospace"));
        Assert.Equal("Consolas", monospace.Value.Trim());
    }

    [Fact]
    public void FontWeight_StrongIsLimitedToConfirmedEmphasisScenarios()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var strongSelectors = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Style"
                && element.Elements().Any(setter =>
                    setter.Name.LocalName == "Setter"
                    && setter.Attribute("Property")?.Value == "FontWeight"
                    && setter.Attribute("Value")?.Value
                        == "{StaticResource Launcher.Typography.FontWeight.Strong}"))
            .Select(element => element.Attribute("Selector")?.Value)
            .ToHashSet(StringComparer.Ordinal);

        var expectedSelectors = new HashSet<string>(StringComparer.Ordinal)
        {
            "TextBlock.heading",
            "TextBlock.dialog-title",
            "TextBlock.dialog-alert-title",
            "TextBlock.titlebar-brand",
            "TextBlock.progress-title",
            "TextBlock.panel-title",
            "TextBlock.section-title",
            "TextBlock.group-title",
            "TextBlock.category-title",
            "TextBlock.about-product-name",
            "TextBlock.operation-status-title",
            "ListBox.settings-navigation > ListBoxItem:selected",
            "Button.primary-action",
            "Button.danger-action",
            "Button.confirm-dialog-action",
            "Button.launcher-control.start"
        };

        Assert.True(
            strongSelectors.SetEquals(expectedSelectors),
            $"Strong font weight selectors: {string.Join(", ", strongSelectors.Order())}");

        Assert.DoesNotContain(
            document.Descendants(),
            element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Window"
                && element.Elements().Any(setter =>
                    setter.Name.LocalName == "Setter"
                    && setter.Attribute("Property")?.Value == "FontWeight"));
    }

    [Fact]
    public void SemanticComponents_UseBalancedDensityTokens()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));

        var settingsSection = GetStyleSetters(document, "Border.settings-section");
        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.Lg}", settingsSection["Padding"]);
        Assert.Equal("{StaticResource Launcher.Radius.Md}", settingsSection["CornerRadius"]);

        var contentRow = GetStyleSetters(document, "Border.content-row");
        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.Md}", contentRow["Padding"]);
        Assert.Equal("{StaticResource Launcher.Component.ContentRow.Margin}", contentRow["Margin"]);
        Assert.Equal("{StaticResource Launcher.Radius.Sm}", contentRow["CornerRadius"]);

        // ADR-015：Border.dialog 退役，表面档案（圆角/底色）由 DialogSurface 主题承载。
        var dialogTheme = File.ReadAllText(ProjectFile("Views/Styles/DialogSurface.axaml"));
        Assert.Contains(
            "{StaticResource Launcher.Component.Dialog.CornerRadius}",
            dialogTheme,
            StringComparison.Ordinal);
        Assert.Contains(
            "{DynamicResource Launcher.Color.Dialog.Background}",
            dialogTheme,
            StringComparison.Ordinal);

        var settingControl = GetStyleSetters(document, "ComboBox.setting-control");
        Assert.False(settingControl.ContainsKey("Width"));
        Assert.Equal("{StaticResource Launcher.Component.Settings.Control.MinWidth}", settingControl["MinWidth"]);
        Assert.Equal(
            "{StaticResource Launcher.Control.Height.Field}",
            settingControl["MinHeight"]);
        Assert.Equal("Center", settingControl["VerticalAlignment"]);

        var colorPickerControl = GetStyleSetters(document, "ColorPicker.setting-control");
        Assert.Equal("{StaticResource Launcher.Component.Settings.Control.MinWidth}", colorPickerControl["Width"]);
        Assert.Equal("{StaticResource Launcher.Component.Settings.Control.MinWidth}", colorPickerControl["MinWidth"]);
        Assert.Equal(
            "{StaticResource Launcher.Control.Height.Field}",
            colorPickerControl["MinHeight"]);

        var dialogAction = GetStyleSetters(document, "Button.dialog-action");
        Assert.Equal(
            "{StaticResource Launcher.Control.Height.Dialog}",
            dialogAction["Height"]);

        var bottomAction = GetStyleSetters(document, "Button.bottom-action");
        Assert.Equal(
            "{StaticResource Launcher.Control.Height.Bottom}",
            bottomAction["MinHeight"]);

        var launchAction = GetStyleSetters(document, "Button.launcher-control.start");
        Assert.Equal(
            "{StaticResource Launcher.Control.Height.Launch}",
            launchAction["MinHeight"]);
    }

    [Fact]
    public void InteractiveControlStyles_UseSharedFocusAndHeightTokens()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));

        var iconLink = GetStyleSetters(document, "Button.icon-link");
        Assert.Equal("{StaticResource Launcher.Radius.Sm}", iconLink["CornerRadius"]);
        Assert.Equal("{StaticResource Launcher.Typography.FontSize.Body.Md}", iconLink["FontSize"]);
        Assert.Equal("Center", iconLink["HorizontalContentAlignment"]);
        Assert.Equal("Center", iconLink["VerticalContentAlignment"]);

        // ADR-008 batch A: every button family routes through the shared template (ADR-004).
        foreach (var selector in new[]
                 {
                     "Button.text-link",
                     "Button.icon-button",
                     "Button.banner-link",
                     "Button.primary-action",
                     "Button.flat-action",
                     "Button.danger-action",
                     "Button.dialog-close",
                     "Button.chrome"
                 })
        {
            Assert.Equal(
                "{StaticResource LauncherBorderButtonTemplate}",
                GetStyleSetters(document, selector)["Template"]);
        }
        var toastStyles = XDocument.Load(ProjectFile("Views/Styles/Toast.axaml"));
        Assert.Equal(
            "{StaticResource LauncherBorderButtonTemplate}",
            GetStyleSetters(toastStyles, "Button.toast-close")["Template"]);

        var flatAction = GetStyleSetters(document, "Button.flat-action");
        Assert.Equal(
            "{StaticResource Launcher.Control.Height.Setting}",
            flatAction["MinHeight"]);
        Assert.Equal("{StaticResource Launcher.Radius.Sm}", flatAction["CornerRadius"]);
        Assert.Equal("Center", flatAction["HorizontalContentAlignment"]);
        Assert.Equal("Center", flatAction["VerticalContentAlignment"]);

        var sharedButtonFocus = GetStyleSetters(document, "Button:focus-visible");
        Assert.Equal(
            "{DynamicResource Launcher.Color.FocusRing}",
            sharedButtonFocus["BorderBrush"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Focus}", sharedButtonFocus["BorderThickness"]);

        var pathField = GetStyleSetters(document, "Border.path-field");
        Assert.Equal(
            "{StaticResource Launcher.Control.Height.Field}",
            pathField["Height"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.PathField.Padding}",
            pathField["Padding"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.Dialog.Title.Height}",
            GetStyleSetters(document, "Grid.dialog-header")["Height"]);
    }

    [Fact]
    public void ButtonVariants_CoverM3InteractiveAndDisabledStates()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));

        var textLinkHover = GetStyleSetters(document, "Button.text-link:pointerover");
        Assert.Equal(
            "{DynamicResource Launcher.Color.Button.Flat.Hover}",
            textLinkHover["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Primary}",
            textLinkHover["Foreground"]);

        var textLinkDisabled = GetStyleSetters(document, "Button.text-link:disabled");
        Assert.Equal(
            "{DynamicResource Launcher.Text.Secondary}",
            textLinkDisabled["Foreground"]);
        Assert.Equal(
            "{StaticResource Launcher.StateLayer.Disabled.Content}",
            textLinkDisabled["Opacity"]);

        var iconButtonDisabled = GetStyleSetters(document, "Button.icon-button:disabled");
        Assert.Equal(
            "{StaticResource Launcher.Color.Transparent}",
            iconButtonDisabled["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Text.Secondary}",
            iconButtonDisabled["Foreground"]);

        var dangerDisabled = GetStyleSetters(document, "Button.danger-action:disabled");
        Assert.Equal(
            "{DynamicResource Launcher.Color.OnError}",
            GetStyleSetters(document, "Button.danger-action")["Foreground"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.OnError}",
            GetStyleSetters(document, "Button.danger-action:pointerover")["Foreground"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.OnError}",
            GetStyleSetters(document, "Button.danger-action:pressed")["Foreground"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Content.Row}",
            dangerDisabled["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Card.Border}",
            dangerDisabled["BorderBrush"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Default}", dangerDisabled["BorderThickness"]);

        var closeDisabled = GetStyleSetters(document, "Button.dialog-close:disabled");
        Assert.Equal(
            "{DynamicResource Launcher.Text.Secondary}",
            closeDisabled["Foreground"]);
    }

    [Fact]
    public void ExistingM3Components_CoverPressedFocusAndDisabledStates()
    {
        var remoteStyles = XDocument.Load(ProjectFile("Views/Styles/RemoteContent.axaml"));

        var socialPressed = GetStyleSetters(remoteStyles, "Button.social-chip:pressed");
        Assert.Equal(
            "{DynamicResource Launcher.Color.Button.Flat.Pressed}",
            socialPressed["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Primary.Pressed}",
            socialPressed["BorderBrush"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.FocusRing}",
            GetStyleSetters(remoteStyles, "Button.social-chip:focus-visible")["BorderBrush"]);

        var filterTabStyles = XDocument.Load(ProjectFile("Views/Styles/Diagnostics.axaml"));
        var filterTab = GetStyleSetters(filterTabStyles, "Button.filter-tab");
        Assert.Equal("{StaticResource LauncherBorderButtonTemplate}", filterTab["Template"]);
        Assert.Equal("{StaticResource Launcher.Radius.Sm}", filterTab["CornerRadius"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Primary.Pressed}",
            GetStyleSetters(filterTabStyles, "Button.filter-tab:pressed")["Background"]);
        Assert.Equal(
            "{StaticResource Launcher.StateLayer.Disabled.Content}",
            GetStyleSetters(filterTabStyles, "Button.filter-tab:disabled")["Opacity"]);

        var mainStyles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var settingRow = GetStyleSetters(mainStyles, "Grid.settings-row");
        Assert.Equal("Center", settingRow["VerticalAlignment"]);
    }

    [Fact]
    public void SelectControls_UseOutlinedFieldTokensAcrossStates()
    {
        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));

        var select = GetStyleSetters(styles, "ComboBox.setting-control");
        Assert.Equal(
            "{DynamicResource Launcher.Color.Field.Background}",
            select["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Field.Border}",
            select["BorderBrush"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Default}", select["BorderThickness"]);
        Assert.Equal(
            "{StaticResource Launcher.Radius.Md}",
            select["CornerRadius"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.Select.Padding}",
            select["Padding"]);

        Assert.Equal(
            "{DynamicResource Launcher.Color.Primary.Hover}",
            GetStyleSetters(styles, "ComboBox.setting-control:pointerover")["BorderBrush"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Primary.Pressed}",
            GetStyleSetters(styles, "ComboBox.setting-control:pressed")["BorderBrush"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.FocusRing}",
            GetStyleSetters(styles, "ComboBox.setting-control:focus-visible")["BorderBrush"]);
        Assert.Equal(
            "{StaticResource Launcher.StateLayer.Disabled.Content}",
            GetStyleSetters(styles, "ComboBox.setting-control:disabled")["Opacity"]);
    }

    [Fact]
    public void DesignGallery_EveryTokenFamilyHasLocalizedGroupName()
    {
        // Gallery group titles are composed at runtime as "designGroup" + family segment,
        // so static localization scans cannot see them; this locks the mapping instead.
        var application = XDocument.Load(ProjectFile("App.axaml"));
        var tokenFamilies = application
            .Descendants()
            .Select(element => element.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "Key")?.Value)
            .Where(key => key is not null && key.StartsWith("Launcher.", StringComparison.Ordinal))
            .Select(key => key!.Split('.')[1])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var neutral = XDocument.Load(ProjectFile("Resources/LauncherStrings.resx"));
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

        foreach (var family in Cafe.Launcher.Avalonia.Helpers.DesignTokenGrouping.FamilyOrder)
        {
            Assert.Contains($"designGroup{family}", groupKeys);
        }
    }

    [Fact]
    public void DesignGallery_ProvidesFourButtonTypesCardAndSettingsRowAcrossSixStates()
    {
        var document = XDocument.Load(ProjectFile("Views/DesignGalleryOverlay.axaml"));
        var dialogContent = document
            .Descendants()
            .Single(element => element.Name.LocalName == "DialogSurface.Content");
        var contentStack = dialogContent.Elements().Single();
        Assert.Null(contentStack.Attribute("Margin"));
        var matrix = document.Descendants().Single(element => HasClass(element, "design-state-matrix"));

        Assert.Equal("Auto,*,*,*,*,*,*", matrix.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal(
            "Auto,Auto,Auto,Auto,Auto,Auto,Auto",
            matrix.Attribute("RowDefinitions")?.Value);
        Assert.Equal(7, matrix.Descendants().Count(element => HasClass(element, "design-state-header")));
        Assert.Equal(20, matrix.Descendants().Count(element => HasClass(element, "gallery-button")));
        Assert.Equal(6, matrix.Descendants().Count(element => HasClass(element, "gallery-select")));
        Assert.Equal(5, matrix.Descendants().Count(element => HasClass(element, "gallery-card")));

        // ADR-007: the matrix shows the four button types, a card (Toast) and a settings row.
        Assert.Equal(5, matrix.Descendants().Count(element =>
            HasClass(element, "gallery-button") && HasClass(element, "outlined")));
        Assert.Equal(5, matrix.Descendants().Count(element =>
            HasClass(element, "gallery-button") && HasClass(element, "text")));
        Assert.Equal(5, matrix.Descendants().Count(element =>
            HasClass(element, "gallery-button") && HasClass(element, "error")));
        Assert.Equal(5, matrix.Descendants().Count(element =>
            HasClass(element, "gallery-button") && !HasClass(element, "outlined")
            && !HasClass(element, "text") && !HasClass(element, "error")));
        Assert.Equal(5, matrix.Descendants().Count(element =>
            HasClass(element, "gallery-card") && HasClass(element, "toast")));

        // Invalid state applies only to the settings row / input classes (ADR-007);
        // buttons and cards render an explicit unused placeholder instead.
        Assert.Equal(5, matrix.Descendants().Count(element => HasClass(element, "gallery-invalid-unused")));
        Assert.DoesNotContain(matrix.Descendants(), element =>
            HasClass(element, "gallery-button") && HasClass(element, "state-invalid"));
        Assert.DoesNotContain(matrix.Descendants(), element =>
            HasClass(element, "gallery-card") && HasClass(element, "state-invalid"));
        Assert.Single(matrix.Descendants(), element =>
            HasClass(element, "gallery-select") && HasClass(element, "state-invalid"));

        var disabledButtons = matrix.Descendants()
            .Where(element =>
                element.Name.LocalName == "Button" && HasClass(element, "state-disabled"))
            .ToArray();
        Assert.Equal(4, disabledButtons.Length);
        Assert.All(disabledButtons, button =>
            Assert.Equal("False", button.Attribute("IsEnabled")?.Value));
        var disabledSelect = matrix.Descendants().Single(element =>
            element.Name.LocalName == "ComboBox" && HasClass(element, "state-disabled"));
        Assert.Equal("False", disabledSelect.Attribute("IsEnabled")?.Value);
    }

    [Fact]
    public void Views_UseSemanticColorsAndTokenizedMaterialIconSizes()
    {
        foreach (var relativePath in ViewFiles
                     .Concat(StyleFiles)
                     .Append("Views/MainWindowDebugOverlay.axaml"))
        {
            var text = File.ReadAllText(ProjectFile(relativePath));

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
            foreach (var icon in document.Descendants().Where(element => element.Name.LocalName == "MaterialIcon"))
            {
                var width = icon.Attribute("Width")?.Value;
                var height = icon.Attribute("Height")?.Value;

                Assert.NotNull(width);
                Assert.NotNull(height);
                Assert.Contains(width, IconTokens);
                Assert.Equal(width, height);
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
        // (design-system spec §3.1). BoxShadow literals are exempt — they carry no
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
            ProjectFile("Controls"),
            "*.axaml",
            SearchOption.TopDirectoryOnly);
        var colorScannedFiles = StyleFiles
            .Append("Views/MainWindowDebugOverlay.axaml")
            .Concat(controlFiles)
            .ToArray();

        foreach (var relativePath in colorScannedFiles)
        {
            var document = XDocument.Load(ProjectFile(relativePath));
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
            var document = XDocument.Load(ProjectFile(relativePath));
            var attributes = document
                .Descendants()
                .SelectMany(element => element.Attributes())
                .ToArray();

            Assert.DoesNotContain(
                attributes,
                attribute => attribute.Name.LocalName is "FontSize" or "FontWeight");
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
    public void StyleFiles_UseStaticTokensForVisualValues()
    {
        var visualProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "Padding",
            "Margin",
            "BorderThickness",
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
            var document = XDocument.Load(ProjectFile(relativePath));
            var rawValues = document
                .Descendants()
                .Where(element => element.Name.LocalName == "Setter")
                .SelectMany(element => element.Attributes()
                    .Where(attribute => attribute.Name.LocalName == "Property"
                        && visualProperties.Contains(attribute.Value))
                    .Select(attribute => (Property: attribute.Value, Value: element.Attribute("Value")?.Value)))
                .Where(item => item.Value is not null
                    && !item.Value.StartsWith('{'))
                .ToArray();

            Assert.Empty(rawValues);
        }
    }

    [Fact]
    public void CornerRadii_UseTheFourDeclaredHierarchyTokens()
    {
        var allowedTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "0",
            "{StaticResource Launcher.Radius.Xs}",
            "{StaticResource Launcher.Radius.Sm}",
            "{StaticResource Launcher.Radius.Md}",
            "{StaticResource Launcher.Radius.Lg}",
            "{TemplateBinding CornerRadius}"
        };

        foreach (var relativePath in ViewFiles.Append("Views/MainWindow.Styles.axaml"))
        {
            var document = XDocument.Load(ProjectFile(relativePath));
            var radiusValues = document
                .Descendants()
                .SelectMany(element => element.Attributes())
                .Where(attribute =>
                    attribute.Name.LocalName == "CornerRadius"
                    || attribute.Parent?.Attribute("Property")?.Value == "CornerRadius" && attribute.Name.LocalName == "Value")
                .Select(attribute => attribute.Value);

            Assert.All(radiusValues, value => Assert.Contains(value, allowedTokens));
        }
    }

    [Fact]
    public void PrimaryActionButtons_NormalStateUsesNoBorderWhileFocusAndDisabledStatesKeepTheirBorders()
    {
        var mainStyles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var toastStyles = XDocument.Load(ProjectFile("Views/Styles/Toast.axaml"));

        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.None}", GetStyleSetters(mainStyles, "Button.primary-action")["BorderThickness"]);
        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.None}", GetStyleSetters(toastStyles, "Button.toast-primary-action")["BorderThickness"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Focus}", GetStyleSetters(mainStyles, "Button:focus-visible")["BorderThickness"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Default}", GetStyleSetters(mainStyles, "Button.primary-action.dialog-action:disabled")["BorderThickness"]);
    }

    [Fact]
    public void LogFilterTabs_UseNoThemeBorderForAccentStates()
    {
        var styles = XDocument.Load(ProjectFile("Views/Styles/Diagnostics.axaml"));

        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.None}", GetStyleSetters(styles, "Button.filter-tab")["BorderThickness"]);
        Assert.Equal("{DynamicResource Launcher.Color.Primary}", GetStyleSetters(styles, "Button.filter-tab.active")["Background"]);
    }

    [Fact]
    public void SocialChips_UseCrispBorderTemplateAcrossInteractiveStates()
    {
        var styles = XDocument.Load(ProjectFile("Views/Styles/RemoteContent.axaml"));

        var socialChip = GetStyleSetters(styles, "Button.social-chip");
        Assert.Equal("{StaticResource LauncherBorderButtonTemplate}", socialChip["Template"]);
        Assert.Equal("Center", socialChip["HorizontalContentAlignment"]);
        Assert.Equal("Center", socialChip["VerticalContentAlignment"]);

        var disabled = GetStyleSetters(styles, "Button.social-chip:disabled");
        Assert.Equal("1", disabled["Opacity"]);
        Assert.Equal("{DynamicResource Launcher.Color.Button.Border}", disabled["BorderBrush"]);
    }

    [Fact]
    public void StyleFiles_AreExplicitAndParseable()
    {
        var discoveredFiles = Directory
            .GetFiles(ProjectFile("Views"), "*.axaml", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".Styles.axaml", StringComparison.Ordinal)
                || path.Contains(
                    $"{Path.DirectorySeparatorChar}Styles{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(TestLocalizationHelper.FindProjectRoot(), path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(StyleFiles.Order(StringComparer.Ordinal), discoveredFiles);
        Assert.All(StyleFiles, path => XDocument.Load(ProjectFile(path)));
    }

    [Fact]
    public void IconOnlyButtons_ExposeAutomationNames()
    {
        foreach (var relativePath in ViewFiles)
        {
            var document = XDocument.Load(ProjectFile(relativePath));
            var iconOnlyButtons = document
                .Descendants()
                .Where(element => element.Name.LocalName == "Button")
                .Where(element =>
                {
                    var children = element.Elements().ToList();
                    return children.Count == 1
                        && children[0].Name.LocalName == "MaterialIcon";
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
        var settingsViewModel = File.ReadAllText(ProjectFile("Features/Settings/SettingsViewModel.cs"));

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
            var document = XDocument.Load(ProjectFile(relativePath));
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
