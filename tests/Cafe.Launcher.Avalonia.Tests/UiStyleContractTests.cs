using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed partial class UiStyleContractTests
{
    private static readonly string[] ViewFiles =
    [
        "Views/MainWindow.axaml",
        "Views/MainWindowSettingsOverlay.axaml",
        "Views/SettingsGeneralSection.axaml",
        "Views/SettingsGameSection.axaml",
        "Views/SettingsDownloadNetworkSection.axaml",
        "Views/SettingsAppearanceSection.axaml",
        "Views/SettingsNotificationsContentSection.axaml",
        "Views/SettingsAdvancedSection.axaml",
        "Views/SettingsAboutSection.axaml",
        "Views/MainWindowDialogsOverlay.axaml",
        "Views/MainWindowLogViewerOverlay.axaml",
        "Views/MainWindowToastOverlay.axaml"
    ];

    [Fact]
    public void SettingsSections_OwnEachBindingExactlyOnceAndUseCompiledBindings()
    {
        Dictionary<string, string[]> expectedBindings = new(StringComparer.Ordinal)
        {
            ["SettingsGeneralSection"] =
            [
                "Settings.Editor.Current.Language",
                "Settings.Editor.Current.CloseBehavior",
                "Settings.Editor.Current.MotionMode"
            ],
            ["SettingsGameSection"] =
            [
                "Settings.Editor.Current.GamePath",
                "Settings.Editor.Current.LaunchCheckMode",
                "Operations.RequestRepairCommand",
                "Operations.RequestUninstallCommand"
            ],
            ["SettingsDownloadNetworkSection"] =
            [
                "Settings.Editor.Current.ProxyMode",
                "Settings.Editor.Current.PatchUrlGroup",
                "Settings.Editor.Current.DownloadSpeedLimit",
                "Settings.Editor.Current.UpdateChannel"
            ],
            ["SettingsAppearanceSection"] =
            [
                "Settings.Editor.Current.ThemeMode",
                "Settings.Editor.Current.ThemeColorMode",
                "Settings.Editor.Current.BackgroundSource",
                "Settings.Editor.Current.BackgroundFit",
                "Settings.Appearance.ThemeColorPaletteItems",
                "Settings.Appearance.SelectedCustomThemeColor",
                "Settings.Appearance.SelectedBackgroundFillColor",
                "Settings.ChooseBackgroundImageCommand",
                "Settings.ChooseBackgroundFolderCommand",
                "Settings.ClearBackgroundCommand",
                "Settings.Appearance.IsWallpaperThemeColorSelected",
                "Settings.Appearance.IsCustomThemeColorSelected",
                "Settings.Appearance.IsBackgroundFitSelected",
                "Settings.Appearance.IsCustomBackgroundSelected"
            ],
            ["SettingsNotificationsContentSection"] =
            [
                "Settings.Editor.Current.ToastNotificationsEnabled",
                "Settings.Editor.Current.ShowRemoteContentCard"
            ],
            ["SettingsAdvancedSection"] =
            [
                "Settings.Editor.Current.LogLevel"
            ],
            ["SettingsAboutSection"] =
            [
                "Settings.CheckForUpdatesCommand",
                "WindowChrome.OpenOfficialSiteCommand",
                "WindowChrome.OpenGitHubRepositoryCommand",
                "LogViewer.OpenCommand",
                "LogViewer.ExportCommand",
                "WindowChrome.OpenDataDirectoryCommand"
            ]
        };

        var allSectionText = string.Join(
            Environment.NewLine,
            expectedBindings.Keys.Select(name => File.ReadAllText(ProjectFile($"Views/{name}.axaml"))));

        foreach (var (sectionName, bindings) in expectedBindings)
        {
            var text = File.ReadAllText(ProjectFile($"Views/{sectionName}.axaml"));
            Assert.Contains("x:DataType=\"vm:MainWindowViewModel\"", text, StringComparison.Ordinal);

            foreach (var binding in bindings)
            {
                Assert.Contains(binding, text, StringComparison.Ordinal);
                Assert.Equal(
                    1,
                    Regex.Count(allSectionText, Regex.Escape(binding), RegexOptions.CultureInvariant));
            }
        }
    }

    [Fact]
    public void SettingsOverlay_ReferencesSevenCategorySectionsWithoutOwningSettingsRows()
    {
        var overlay = File.ReadAllText(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
        Dictionary<string, string> sectionVisibility = new(StringComparer.Ordinal)
        {
            ["SettingsGeneralSection"] = "Settings.IsGeneralCategorySelected",
            ["SettingsGameSection"] = "Settings.IsGameCategorySelected",
            ["SettingsDownloadNetworkSection"] = "Settings.IsDownloadNetworkCategorySelected",
            ["SettingsAppearanceSection"] = "Settings.IsAppearanceCategorySelected",
            ["SettingsNotificationsContentSection"] = "Settings.IsNotificationsContentCategorySelected",
            ["SettingsAdvancedSection"] = "Settings.IsAdvancedCategorySelected",
            ["SettingsAboutSection"] = "Settings.IsAboutCategorySelected"
        };

        foreach (var (sectionName, visibility) in sectionVisibility)
        {
            Assert.Equal(
                1,
                Regex.Count(overlay, $"<views:{sectionName} IsVisible=\"{{Binding {Regex.Escape(visibility)}}}\"/>"));
        }

        Assert.DoesNotContain("Settings.Editor.Current", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("settings-row", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsOverlay_UsesFixedTwoColumnCategoryWorkspace()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
        var dialog = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "overlay-dialog"));
        Assert.Equal("960", dialog.Attribute("Width")?.Value);
        Assert.Equal("620", dialog.Attribute("Height")?.Value);
        var dialogLayout = dialog.Elements().Single(element => element.Name.LocalName == "Grid");
        Assert.Equal("Auto,*,Auto", dialogLayout.Attribute("RowDefinitions")?.Value);

        var workspace = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "settings-workspace"));
        Assert.Equal("184,*", workspace.Attribute("ColumnDefinitions")?.Value);

        var navigation = workspace
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ListBox"
                && HasClass(element, "settings-navigation"));
        Assert.Equal(
            "{Binding Settings.Options.SettingsCategories}",
            navigation.Attribute("ItemsSource")?.Value);
        Assert.Equal(
            "{Binding Settings.SelectedCategory, Mode=TwoWay}",
            navigation.Attribute("SelectedValue")?.Value);
        Assert.Equal(
            "{Binding Code}",
            navigation.Attribute("SelectedValueBinding")?.Value);
        Assert.Equal(
            "{Binding Settings.IsSaving, Converter={x:Static BoolConverters.Not}}",
            navigation.Attribute("IsEnabled")?.Value);

        var itemTemplate = navigation
            .Descendants()
            .Single(element => element.Name.LocalName == "DataTemplate");
        var categoryName = itemTemplate
            .Descendants()
            .Single(element => element.Name.LocalName == "TextBlock");
        Assert.Equal("{Binding DisplayName}", categoryName.Attribute("Text")?.Value);
        Assert.Equal(
            "{Binding DisplayName}",
            categoryName.Attributes()
                .Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                .Value);

        var content = workspace
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "settings-content"));
        var categoryTitle = content
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBlock"
                && HasClass(element, "category-title"));
        Assert.Equal(
            "{Binding SelectedItem, ElementName=SettingsNavigation}",
            categoryTitle.Attribute("DataContext")?.Value);
        Assert.Equal(
            "models:SettingOption",
            categoryTitle.Attributes()
                .Single(attribute => attribute.Name.LocalName == "DataType")
                .Value);
        Assert.Equal(
            "{Binding DisplayName}",
            categoryTitle.Attribute("Text")?.Value);
        Assert.Equal(
            "{Binding DisplayName}",
            categoryTitle.Attributes()
                .Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                .Value);
        Assert.Single(
            content.Descendants(),
            element => element.Name.LocalName == "ScrollViewer");
        Assert.Single(
            content.Descendants(),
            element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "status-summary"));
    }

    [Fact]
    public void SettingsWorkspaceStyles_UseSemanticBrushesAndDesignTokens()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));

        Assert.Equal(
            "0",
            GetStyleSetters(document, "Grid.settings-workspace")["ColumnSpacing"]);
        Assert.Equal(
            "0",
            GetStyleSetters(document, "Grid.settings-workspace")["Margin"]);
        Assert.Equal(
            "{DynamicResource LauncherContentRowBrush}",
            GetStyleSetters(document, "ListBox.settings-navigation")["Background"]);
        Assert.Equal(
            "0",
            GetStyleSetters(document, "ListBox.settings-navigation")["BorderThickness"]);
        Assert.Equal(
            "16,8,8,16",
            GetStyleSetters(document, "ListBox.settings-navigation")["Padding"]);
        Assert.Equal(
            "0",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem")["BorderThickness"]);
        Assert.Equal(
            "{DynamicResource LauncherTransparentBrush}",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem")["BorderBrush"]);
        Assert.Equal(
            "0",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem")["CornerRadius"]);
        Assert.Equal(
            "8,12,12,12",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem")["Padding"]);
        Assert.Equal(
            "3,0,0,0",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem:selected")["BorderThickness"]);
        Assert.Equal(
            "{DynamicResource LauncherAccentBrush}",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem:selected")["BorderBrush"]);
        Assert.Equal(
            "{DynamicResource LauncherAccentSoftBrush}",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem:selected:not(:focus)")["Background"]);
        Assert.Equal(
            "{DynamicResource LauncherTextPrimaryBrush}",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem:selected:not(:focus)")["Foreground"]);
        Assert.Equal(
            "{DynamicResource LauncherAccentBrush}",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem:selected:not(:focus)")["BorderBrush"]);
        Assert.Equal(
            "3,0,0,0",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem:selected:not(:focus)")["BorderThickness"]);
        Assert.Equal(
            "{StaticResource LauncherSpacingMd}",
            GetStyleSetters(document, "Grid.settings-content")["RowSpacing"]);
        Assert.Equal(
            "0",
            GetStyleSetters(document, "StackPanel.settings-category-header")["Spacing"]);
        Assert.Equal(
            "0,0,0,8",
            GetStyleSetters(document, "Border.settings-status-summary")["Padding"]);
        Assert.Equal(
            "14",
            GetStyleSetters(document, "TextBlock.group-title")["FontSize"]);
        Assert.Equal(
            "0,12,0,0",
            GetStyleSetters(document, "Grid.settings-row")["Margin"]);

        var overlayDocument = XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
        var scrollViewer = overlayDocument
            .Descendants()
            .Single(element => element.Name.LocalName == "ScrollViewer");
        Assert.Equal("Auto", scrollViewer.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("Disabled", scrollViewer.Attribute("HorizontalScrollBarVisibility")?.Value);
    }

    [Fact]
    public void AppearanceSection_UsesTwoSettingsGroupsForConsistentVerticalRhythm()
    {
        var document = XDocument.Load(ProjectFile("Views/SettingsAppearanceSection.axaml"));
        var groups = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "StackPanel"
                && HasClass(element, "settings-group"))
            .ToList();

        Assert.Equal(2, groups.Count);

        Assert.Equal(
            "{Binding Shell.I18n.SettingsThemeColorGroup}",
            groups[0]
                .Elements()
                .First(element => element.Name.LocalName == "TextBlock")
                .Attribute("Text")?.Value);
        Assert.Equal(
            "{Binding Shell.I18n.SettingsBackgroundGroup}",
            groups[1]
                .Elements()
                .First(element => element.Name.LocalName == "TextBlock")
                .Attribute("Text")?.Value);
    }

    [Fact]
    public void AboutSection_UsesSettingsGroupForTopLevelRhythm()
    {
        var document = XDocument.Load(ProjectFile("Views/SettingsAboutSection.axaml"));
        var root = document.Root?.Elements().Single(element => element.Name.LocalName == "StackPanel");

        Assert.NotNull(root);
        Assert.True(HasClass(root!, "settings-group"));
    }

    private static readonly HashSet<string> IconTokens =
    [
        "{StaticResource LauncherIconSm}",
        "{StaticResource LauncherIconMd}",
        "{StaticResource LauncherIconLg}",
        "{StaticResource LauncherIconXl}",
        "{StaticResource LauncherIconXxl}"
    ];

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

        Assert.Equal("40", resources["LauncherSpacingSection"]);
        Assert.Equal("8", resources["LauncherThicknessSm"]);
        Assert.Equal("12", resources["LauncherThicknessMd"]);
        Assert.Equal("16", resources["LauncherThicknessLg"]);
        Assert.All(
            document
                .Descendants()
                .Where(element =>
                    element.Attributes().Any(attribute =>
                        attribute.Name.LocalName == "Key"
                        && (attribute.Value == "LauncherThicknessSm"
                            || attribute.Value == "LauncherThicknessMd"
                            || attribute.Value == "LauncherThicknessLg"))),
            element => Assert.Equal("Thickness", element.Name.LocalName));
        Assert.Equal("4", resources["LauncherRadiusSm"]);
        Assert.Equal("6", resources["LauncherRadiusMd"]);
        Assert.Equal("8", resources["LauncherRadiusLg"]);
        Assert.Equal("16", resources["LauncherIconSm"]);
        Assert.Equal("18", resources["LauncherIconMd"]);
        Assert.Equal("20", resources["LauncherIconLg"]);
        Assert.Equal("22", resources["LauncherIconXl"]);
        Assert.Equal("24", resources["LauncherIconXxl"]);
        Assert.Equal("36", resources["LauncherControlHeightSetting"]);
        Assert.Equal("42", resources["LauncherControlHeightDialog"]);
        Assert.Equal("48", resources["LauncherControlHeightBottom"]);
        Assert.Equal("58", resources["LauncherControlHeightLaunch"]);
    }

    [Fact]
    public void SemanticComponents_UseBalancedDensityTokens()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));

        var settingsSection = GetStyleSetters(document, "Border.settings-section");
        Assert.Equal("{StaticResource LauncherThicknessLg}", settingsSection["Padding"]);
        Assert.Equal("{StaticResource LauncherRadiusMd}", settingsSection["CornerRadius"]);

        var contentRow = GetStyleSetters(document, "Border.content-row");
        Assert.Equal("12", contentRow["Padding"]);
        Assert.Equal("{StaticResource LauncherRadiusSm}", contentRow["CornerRadius"]);

        var dialog = GetStyleSetters(document, "Border.dialog");
        Assert.Equal("{StaticResource LauncherRadiusLg}", dialog["CornerRadius"]);

        var settingControl = GetStyleSetters(document, "ComboBox.setting-control");
        Assert.False(settingControl.ContainsKey("Width"));
        Assert.Equal("220", settingControl["MinWidth"]);
        Assert.Equal(
            "{StaticResource LauncherControlHeightSetting}",
            settingControl["MinHeight"]);
        Assert.Equal("Center", settingControl["VerticalAlignment"]);

        var colorPickerControl = GetStyleSetters(document, "ColorPicker.setting-control");
        Assert.False(colorPickerControl.ContainsKey("Width"));
        Assert.Equal("220", colorPickerControl["MinWidth"]);
        Assert.Equal(
            "{StaticResource LauncherControlHeightSetting}",
            colorPickerControl["MinHeight"]);

        var dialogAction = GetStyleSetters(document, "Button.dialog-action");
        Assert.Equal(
            "{StaticResource LauncherControlHeightDialog}",
            dialogAction["Height"]);

        var bottomAction = GetStyleSetters(document, "Button.bottom-action");
        Assert.Equal(
            "{StaticResource LauncherControlHeightBottom}",
            bottomAction["MinHeight"]);

        var launchAction = GetStyleSetters(document, "Button.launcher-control.start");
        Assert.Equal(
            "{StaticResource LauncherControlHeightLaunch}",
            launchAction["MinHeight"]);
    }

    [Fact]
    public void Views_UseSemanticColorsAndTokenizedMaterialIconSizes()
    {
        foreach (var relativePath in ViewFiles)
        {
            var text = File.ReadAllText(ProjectFile(relativePath));
            Assert.DoesNotMatch(DirectColorRegex(), text);
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
                    value.StartsWith("{StaticResource LauncherSpacing", StringComparison.Ordinal),
                    $"Spacing value must use a LauncherSpacing token: {value}"));
        }
    }

    [Fact]
    public void UpdateFileList_HoverAndSelectionKeepReadableItemColors()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var styles = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .ToDictionary(
                element => element.Attribute("Selector")?.Value ?? "",
                element => element,
                StringComparer.Ordinal);

        foreach (var selector in new[]
                 {
                     "ListBox.update-file-list > ListBoxItem:pointerover /template/ ContentPresenter#PART_ContentPresenter",
                     "ListBox.update-file-list > ListBoxItem:pressed /template/ ContentPresenter#PART_ContentPresenter",
                     "ListBox.update-file-list > ListBoxItem:selected /template/ ContentPresenter#PART_ContentPresenter",
                     "ListBox.update-file-list > ListBoxItem:selected:not(:focus) /template/ ContentPresenter#PART_ContentPresenter",
                     "ListBox.update-file-list > ListBoxItem:selected:pointerover /template/ ContentPresenter#PART_ContentPresenter",
                     "ListBox.update-file-list > ListBoxItem:selected:pressed /template/ ContentPresenter#PART_ContentPresenter"
                 })
        {
            var setters = styles[selector]
                .Elements()
                .Where(element => element.Name.LocalName == "Setter")
                .ToDictionary(
                    element => element.Attribute("Property")?.Value ?? "",
                    element => element.Attribute("Value")?.Value ?? "",
                    StringComparer.Ordinal);

            Assert.Equal(
                "{DynamicResource LauncherCardBackgroundBrush}",
                setters["Background"]);
            Assert.Equal(
                "{DynamicResource LauncherTextPrimaryBrush}",
                setters["Foreground"]);
        }
    }

    [Fact]
    public void SettingsNavigation_HoverAndSelectionOverrideFluentDefaultColors()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var styles = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .ToDictionary(
                element => element.Attribute("Selector")?.Value ?? "",
                element => element,
                StringComparer.Ordinal);

        foreach (var selector in new[]
                 {
                     "ListBox.settings-navigation > ListBoxItem:pointerover /template/ ContentPresenter#PART_ContentPresenter",
                     "ListBox.settings-navigation > ListBoxItem:pressed /template/ ContentPresenter#PART_ContentPresenter",
                     "ListBox.settings-navigation > ListBoxItem:selected /template/ ContentPresenter#PART_ContentPresenter",
                     "ListBox.settings-navigation > ListBoxItem:selected:not(:focus) /template/ ContentPresenter#PART_ContentPresenter",
                     "ListBox.settings-navigation > ListBoxItem:selected:pointerover /template/ ContentPresenter#PART_ContentPresenter",
                     "ListBox.settings-navigation > ListBoxItem:selected:pressed /template/ ContentPresenter#PART_ContentPresenter"
                 })
        {
            var setters = styles[selector]
                .Elements()
                .Where(element => element.Name.LocalName == "Setter")
                .ToDictionary(
                    element => element.Attribute("Property")?.Value ?? "",
                    element => element.Attribute("Value")?.Value ?? "",
                    StringComparer.Ordinal);

            Assert.Contains("Background", setters.Keys);
            Assert.Contains("Foreground", setters.Keys);
        }

        var hoverSetters = styles["ListBox.settings-navigation > ListBoxItem:pointerover /template/ ContentPresenter#PART_ContentPresenter"]
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => element.Attribute("Property")?.Value ?? "",
                element => element.Attribute("Value")?.Value ?? "",
                StringComparer.Ordinal);
        Assert.Equal(
            "{DynamicResource LauncherTransparentBrush}",
            hoverSetters["Background"]);
        Assert.Equal(
            "{DynamicResource LauncherAccentBrush}",
            hoverSetters["Foreground"]);

        foreach (var selector in new[]
                 {
                     "ListBox.settings-navigation > ListBoxItem:pressed /template/ ContentPresenter#PART_ContentPresenter",
                     "ListBox.settings-navigation > ListBoxItem:selected /template/ ContentPresenter#PART_ContentPresenter",
                     "ListBox.settings-navigation > ListBoxItem:selected:not(:focus) /template/ ContentPresenter#PART_ContentPresenter",
                     "ListBox.settings-navigation > ListBoxItem:selected:pointerover /template/ ContentPresenter#PART_ContentPresenter",
                     "ListBox.settings-navigation > ListBoxItem:selected:pressed /template/ ContentPresenter#PART_ContentPresenter"
                 })
        {
            var setters = styles[selector]
                .Elements()
                .Where(element => element.Name.LocalName == "Setter")
                .ToDictionary(
                    element => element.Attribute("Property")?.Value ?? "",
                    element => element.Attribute("Value")?.Value ?? "",
                    StringComparer.Ordinal);

            Assert.Equal(
                "{DynamicResource LauncherAccentSoftBrush}",
                setters["Background"]);
            Assert.Equal(
                "{DynamicResource LauncherTextPrimaryBrush}",
                setters["Foreground"]);
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
    public void CornerRadii_UseTheThreeDeclaredHierarchyTokens()
    {
        var allowedTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "0",
            "{StaticResource LauncherRadiusSm}",
            "{StaticResource LauncherRadiusMd}",
            "{StaticResource LauncherRadiusLg}",
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
    public void OverlayOrder_IsBaseThenSettingsThenDialogsThenToast()
    {
        var mainWindow = File.ReadAllText(ProjectFile("Views/MainWindow.axaml"));
        var settingsIndex = mainWindow.IndexOf("<views:MainWindowSettingsOverlay/>", StringComparison.Ordinal);
        var logViewerIndex = mainWindow.IndexOf("<views:MainWindowLogViewerOverlay/>", StringComparison.Ordinal);
        var dialogsIndex = mainWindow.IndexOf("<views:MainWindowDialogsOverlay/>", StringComparison.Ordinal);
        var toastIndex = mainWindow.IndexOf("<views:MainWindowToastOverlay/>", StringComparison.Ordinal);

        Assert.True(settingsIndex >= 0);
        Assert.True(logViewerIndex > settingsIndex);
        Assert.True(dialogsIndex > logViewerIndex);
        Assert.True(toastIndex > dialogsIndex);
    }

    [Fact]
    public void DialogOverlays_UseSharedDialogLayerWithoutExplicitZIndex()
    {
        foreach (var relativePath in new[]
                 {
                     "Views/MainWindowDialogsOverlay.axaml",
                     "Views/MainWindowLogViewerOverlay.axaml"
                 })
        {
            var text = File.ReadAllText(ProjectFile(relativePath));
            Assert.DoesNotContain("ZIndex=\"500\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ZIndex=\"1001\"", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LogViewer_UserFacingTextUsesLocalizationBindings()
    {
        var logViewer = File.ReadAllText(ProjectFile("Views/MainWindowLogViewerOverlay.axaml"));
        var settings = File.ReadAllText(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));

        foreach (var literal in new[]
                 {
                     "Log Viewer",
                     "Search...",
                     "No matching log entries.",
                     "Export Logs",
                     "View Log",
                     "Open Data Directory"
                 })
        {
            Assert.DoesNotContain($"Text=\"{literal}\"", logViewer, StringComparison.Ordinal);
            Assert.DoesNotContain($"Text=\"{literal}\"", settings, StringComparison.Ordinal);
            Assert.DoesNotContain($"PlaceholderText=\"{literal}\"", logViewer, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LogViewer_EmptyStateUsesExplicitViewModelStateAndContainer()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowLogViewerOverlay.axaml"));
        var emptyState = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "log-empty-state"));

        Assert.Equal(
            "{Binding LogViewer.IsEmpty}",
            emptyState.Attribute("IsVisible")?.Value);
        Assert.Contains(
            emptyState.Descendants(),
            element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding Shell.I18n.LogNoMatchingEntries}");

        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var emptyStateTextStyle = styles
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value
                    == "Border.log-empty-state TextBlock.media-placeholder-text");
        Assert.Contains(
            emptyStateTextStyle.Elements(),
            element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "Foreground"
                && element.Attribute("Value")?.Value
                    == "{DynamicResource LauncherTextSecondaryBrush}");
    }

    [Fact]
    public void LogViewer_UsesFixedDialogDimensions()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowLogViewerOverlay.axaml"));
        var dialog = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "overlay-dialog"));

        Assert.Equal(
            "{StaticResource LauncherLogViewerWidth}",
            dialog.Attribute("Width")?.Value);
        Assert.Equal(
            "{StaticResource LauncherLogViewerHeight}",
            dialog.Attribute("Height")?.Value);
        Assert.Null(dialog.Attribute("MaxWidth"));
        Assert.Null(dialog.Attribute("MaxHeight"));
    }

    [Fact]
    public void LocalizationManagement_UsesFixedDialogDimensions()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowDialogsOverlay.axaml"));
        var dialog = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && element.Attribute("IsVisible")?.Value
                    == "{Binding ResourcePanel.IsResourcePanelVisible}")
            .Elements()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "overlay-dialog"));

        Assert.Equal("720", dialog.Attribute("Width")?.Value);
        Assert.Equal("592", dialog.Attribute("Height")?.Value);
        Assert.Null(dialog.Attribute("MaxWidth"));
        Assert.Null(dialog.Attribute("MaxHeight"));
    }

    [Fact]
    public void LogViewer_UsesVirtualizedListBox()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowLogViewerOverlay.axaml"));
        var list = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ListBox"
                && HasClass(element, "log-entry-list"));

        Assert.Equal(
            "{Binding LogViewer.FilteredEntries}",
            list.Attribute("ItemsSource")?.Value);
        Assert.DoesNotContain(
            list.Elements(),
            element => element.Name.LocalName == "ListBox.ItemsPanel");
    }

    [Fact]
    public void ToastLayer_UsesLauncherConstantsZIndex()
    {
        var toastOverlay = File.ReadAllText(ProjectFile("Views/MainWindowToastOverlay.axaml"));

        Assert.Contains(
            "ZIndex=\"{x:Static constants:LauncherConstants.ZIndexToast}\"",
            toastOverlay,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ZIndex=\"1000\"", toastOverlay, StringComparison.Ordinal);
    }

    [Fact]
    public void ToastHost_AllowsHitTestingSoDismissButtonCanReceiveClicks()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));

        Assert.Equal(
            "True",
            GetStyleSetters(document, "Grid.toast-host")["IsHitTestVisible"]);
    }

    [Fact]
    public void BannerImage_UsesDistinctLoadingAndFailureStates()
    {
        var mainWindow = File.ReadAllText(ProjectFile("Views/MainWindow.axaml"));

        Assert.Contains(
            "IsVisible=\"{Binding IsImageLoading}\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsVisible=\"{Binding IsImageLoadFailed}\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Shell.I18n.BannerLoading",
            mainWindow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "BannerBitmap, Converter={x:Static ObjectConverters.IsNull}",
            mainWindow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteContentPanel_UsesExplicitLoadingState()
    {
        var mainWindow = File.ReadAllText(ProjectFile("Views/MainWindow.axaml"));

        Assert.Contains(
            "IsVisible=\"{Binding RemoteContent.IsPanelVisible}\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsVisible=\"{Binding RemoteContent.IsLoading}\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Shell.I18n.RemoteContentLoading",
            mainWindow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_IsResizableWithMinimumViewportConstraints()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var window = document.Root;

        Assert.NotNull(window);
        Assert.Equal("True", window.Attribute("CanResize")?.Value);
        Assert.Equal("1024", window.Attribute("MinWidth")?.Value);
        Assert.Equal("640", window.Attribute("MinHeight")?.Value);
    }

    [Fact]
    public void SettingsPanel_UsesTransactionalSaveAndCancelActions()
    {
        var settingsOverlay = File.ReadAllText(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
        var gameSection = File.ReadAllText(ProjectFile("Views/SettingsGameSection.axaml"));
        var mainWindowCodeBehind = File.ReadAllText(ProjectFile("Views/MainWindow.axaml.cs"));

        Assert.Contains(
            "Text=\"{Binding Settings.Editor.Current.GamePath}\"",
            gameSection,
            StringComparison.Ordinal);
        Assert.Contains(
            "Command=\"{Binding WindowChrome.ShowSettingsCommand}\"",
            settingsOverlay,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsEnabled=\"{Binding Settings.CanSaveSettings}\"",
            settingsOverlay,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            Regex.Count(
                settingsOverlay,
                "settings-footer-action",
                RegexOptions.CultureInvariant));
        Assert.DoesNotContain(
            "Kind=\"ContentSave\" Width=\"{StaticResource LauncherIconMd}\" Height=\"{StaticResource LauncherIconMd}\" Foreground=",
            settingsOverlay,
            StringComparison.Ordinal);
        var mainWindowViewModel = File.ReadAllText(ProjectFile("ViewModels/MainWindowViewModel.cs"));
        Assert.Contains(
            "WindowEscapeAction.ToggleSettings",
            mainWindowViewModel,
            StringComparison.Ordinal);
        Assert.Contains(
            "WindowChrome.ShowSettingsCommand.Execute(null)",
            mainWindowViewModel,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "vm.WindowChrome.IsSettingsVisible",
            mainWindowCodeBehind,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsAboutActionsAndVersionChips_UsePurposeBasedOrder()
    {
        var text = File.ReadAllText(ProjectFile("Views/SettingsAboutSection.axaml"));
        var overlay = File.ReadAllText(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));

        AssertOrdered(
            text,
            "Shell.LauncherVersionText",
            "Shell.BuildTimeText",
            "Shell.CommitShaText",
            "Shell.BuildConfigText",
            "Shell.FrameworkVersionText",
            "Shell.AvaloniaVersionText",
            "Shell.PlatformText");
        AssertOrdered(
            text,
            "Settings.CheckForUpdatesCommand",
            "WindowChrome.OpenOfficialSiteCommand",
            "WindowChrome.OpenGitHubRepositoryCommand",
            "LogViewer.OpenCommand",
            "LogViewer.ExportCommand",
            "WindowChrome.OpenDataDirectoryCommand");

        var document = XDocument.Parse(overlay);
        var footerButtons = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Button"
                && HasClass(element, "settings-footer-action"))
            .ToList();
        Assert.Equal(2, footerButtons.Count);
        Assert.Equal(
            "{Binding WindowChrome.ShowSettingsCommand}",
            footerButtons[0].Attribute("Command")?.Value);
        Assert.Equal(
            "{Binding Settings.SaveSettingsCommand}",
            footerButtons[1].Attribute("Command")?.Value);
    }

    [Fact]
    public void ToastCards_DoNotUseOverlappingBoxShadows()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var toastCardStyle = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Border.toast-card");

        Assert.DoesNotContain(
            toastCardStyle.Elements(),
            element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "BoxShadow");
    }

    [Fact]
    public void ToastMotionAnimation_IsEnabledOnlyByRootMotionPreference()
    {
        var overlay = File.ReadAllText(ProjectFile("Views/MainWindowToastOverlay.axaml"));
        Assert.Contains(
            "Classes.motion-enabled=\"{Binding DataContext.IsMotionEnabled, ElementName=ToastOverlayRoot}\"",
            overlay,
            StringComparison.Ordinal);

        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var toastStyles = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value?.StartsWith(
                    "Border.toast-card",
                    StringComparison.Ordinal) == true)
            .ToList();
        var baseStyle = Assert.Single(
            toastStyles,
            style => style.Attribute("Selector")?.Value == "Border.toast-card");
        var motionStyle = Assert.Single(
            toastStyles,
            style => style.Attribute("Selector")?.Value == "Border.toast-card.motion-enabled");

        Assert.DoesNotContain(
            baseStyle.Elements(),
            element => element.Name.LocalName == "Style.Animations");
        Assert.Contains(
            motionStyle.Elements(),
            element => element.Name.LocalName == "Style.Animations");
    }

    [Fact]
    public void OverlayStyles_DefineSettingsAndDialogLayerOrder()
    {
        var styles = File.ReadAllText(ProjectFile("Views/MainWindow.Styles.axaml"));

        Assert.Matches(
            """(?s)<Style Selector="Grid\.settings-overlay">.*?<Setter Property="ZIndex" Value="100"/>.*?</Style>""",
            styles);
        Assert.Matches(
            """(?s)<Style Selector="Grid\.dialog-overlay">.*?<Setter Property="ZIndex" Value="200"/>.*?</Style>""",
            styles);
    }

    [Fact]
    public void OverlayStyles_TrapAndRestoreKeyboardFocus()
    {
        var styles = File.ReadAllText(ProjectFile("Views/MainWindow.Styles.axaml"));
        var behavior = File.ReadAllText(ProjectFile("Views/OverlayFocusBehavior.cs"));

        Assert.Equal(
            2,
            Regex.Count(
                styles,
                "KeyboardNavigation.TabNavigation\" Value=\"Cycle",
                RegexOptions.CultureInvariant));
        Assert.Equal(
            2,
            Regex.Count(
                styles,
                "OverlayFocusBehavior.IsEnabled\" Value=\"True",
                RegexOptions.CultureInvariant));
        Assert.Contains("previousFocus = focusManager.GetFocusedElement()", behavior, StringComparison.Ordinal);
        Assert.Contains("focus?.Focus(NavigationMethod.Tab)", behavior, StringComparison.Ordinal);
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
    public void SettingsStatusPanel_UsesSummaryBindingsWithoutDuplicateStatusOrBrand()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
        var statusPanel = document
            .Descendants()
            .First(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "settings-status-summary"));
        var markup = statusPanel.ToString(SaveOptions.DisableFormatting);
        var detailsGrid = statusPanel
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "status-details"));
        var detailCards = statusPanel
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "status-detail"))
            .ToList();
        var statusNote = statusPanel
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "status-note"));

        Assert.Contains("Shell.CurrentViewTitle", markup, StringComparison.Ordinal);
        Assert.Contains("Shell.VersionText", markup, StringComparison.Ordinal);
        Assert.Contains("Shell.NetworkStatusValueText", markup, StringComparison.Ordinal);
        Assert.Contains("Shell.DiskSpaceText", markup, StringComparison.Ordinal);
        Assert.Contains("Shell.OperationNote", markup, StringComparison.Ordinal);
        Assert.Contains("Kind=\"{Binding Shell.StatusIconKind}\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Shell.ExecutableText", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Shell.StatusText", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Shell.ProductName", markup, StringComparison.Ordinal);
        Assert.Equal("Auto,Auto", detailsGrid.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("Auto", detailsGrid.Attribute("RowDefinitions")?.Value);
        Assert.Equal(3, detailCards.Count);
        Assert.Contains("Grid.Row=\"1\"", statusNote.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
        Assert.Contains("Grid.ColumnSpan=\"3\"", statusNote.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"160\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DynamicAccent_DoesNotReplaceThemeSpecificInformationTextBrush()
    {
        var settingsViewModel = File.ReadAllText(ProjectFile("ViewModels/SettingsViewModel.cs"));

        Assert.DoesNotContain(
            "SetBrush(application, \"LauncherInfoTextBrush\"",
            settingsViewModel,
            StringComparison.Ordinal);
    }

    private static bool HasClass(XElement element, string className) =>
        element.Attribute("Classes")?.Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(className, StringComparer.Ordinal) == true;

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
        Path.Combine(FindProjectRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cafe.Launcher.Avalonia.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cafe.Launcher.Avalonia.csproj was not found.");
    }

    [GeneratedRegex("#[0-9A-Fa-f]{6,8}", RegexOptions.CultureInvariant)]
    private static partial Regex DirectColorRegex();

    private static IReadOnlyDictionary<string, string> GetStyleSetters(
        XDocument document,
        string selector)
    {
        return document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == selector)
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
