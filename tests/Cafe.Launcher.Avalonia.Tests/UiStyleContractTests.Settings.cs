using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

// Settings workspace contracts: category sections, navigation list, surface
// blueprint, appearance/about sections, and transactional footer actions.
public sealed partial class UiStyleContractTests
{
    [Fact]
    public void SettingsSections_OwnEachBindingExactlyOnceAndUseCompiledBindings()
    {
        Dictionary<string, string[]> expectedBindings = new(StringComparer.Ordinal)
        {
            ["SettingsGeneralSection"] =
            [
                "Settings.Editor.Current.Language",
                "Settings.Editor.Current.CloseBehavior",
                "Settings.Editor.Current.AfterLaunchBehavior",
                "Settings.Editor.Current.MotionMode",
                "Settings.Editor.Current.StatusDetailMode",
                "Settings.Editor.Current.ShowRemoteContentCard",
                "Settings.Editor.Current.UpdateChannel"
            ],
            ["SettingsGameSection"] =
            [
                "Settings.GamePathDisplay",
                "Settings.Editor.Current.LaunchCheckMode",
                "Settings.Editor.Current.GameRuntime.RunnerPath",
                "Settings.Editor.Current.GameRuntime.PrefixPath",
                "Settings.Editor.Current.GameRuntime.ProtonPath",
                "Operations.OpenGameFolderCommand",
                "Operations.RequestRepairCommand",
                "Operations.RequestUninstallCommand"
            ],
            ["SettingsDownloadNetworkSection"] =
            [
                "Settings.Editor.Current.ProxyMode",
                "Settings.Editor.Current.PatchUrlGroup",
                "Settings.Editor.Current.DownloadSpeedLimit"
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
                "Settings.Appearance.IsThemeColorPaletteVisible",
                "Settings.Appearance.IsThemeColorExtractionAlgorithmSettingsVisible",
                "Settings.Appearance.IsCustomThemeColorPickerVisible",
                "Settings.Appearance.IsBackgroundFillColorVisible",
                "Settings.Appearance.IsCustomBackgroundSettingsVisible"
            ],
            ["SettingsAdvancedSection"] =
            [
                "Settings.Editor.Current.LogLevel",
                "LogViewer.OpenCommand",
                "LogExport.OpenCommand",
                "WindowChrome.OpenDataDirectoryCommand"
            ],
            ["SettingsAboutSection"] =
            [
                "Settings.CheckForUpdatesCommand",
                "WindowChrome.OpenAboutOfficialSiteCommand",
                "WindowChrome.OpenHelpDocsCommand",
                "WindowChrome.OpenGitHubRepositoryCommand",
                "WindowChrome.OpenGitHubReleaseRepositoryCommand",
                "WindowChrome.OpenIssueTrackerCommand",
                "WindowChrome.OpenPrivacyPolicyCommand",
                "WindowChrome.OpenDefaultBackgroundArtworkCommand"
            ]
        };

        var allSectionText = string.Join(
            Environment.NewLine,
            expectedBindings.Keys.Select(name => File.ReadAllText(TestRepository.FromApplicationRoot($"Views/{name}.axaml"))));

        foreach (var (sectionName, bindings) in expectedBindings)
        {
            var text = File.ReadAllText(TestRepository.FromApplicationRoot($"Views/{sectionName}.axaml"));
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
    public void SettingsOverlay_ReferencesSixCategorySectionsWithoutOwningSettingsRows()
    {
        var overlay = File.ReadAllText(TestRepository.FromApplicationRoot("Views/MainWindowSettingsOverlay.axaml"));
        var document = XDocument.Parse(overlay);
        Dictionary<string, string> sectionVisibility = new(StringComparer.Ordinal)
        {
            ["SettingsGeneralSection"] = "Settings.IsGeneralCategorySelected",
            ["SettingsGameSection"] = "Settings.IsGameCategorySelected",
            ["SettingsDownloadNetworkSection"] = "Settings.IsDownloadNetworkCategorySelected",
            ["SettingsAppearanceSection"] = "Settings.IsAppearanceCategorySelected",
            ["SettingsAdvancedSection"] = "Settings.IsAdvancedCategorySelected",
            ["SettingsAboutSection"] = "Settings.IsAboutCategorySelected"
        };

        foreach (var (sectionName, visibility) in sectionVisibility)
        {
            Assert.Single(
                document.Descendants(),
                element =>
                    element.Name.LocalName == sectionName
                    && element.Attribute("IsVisible")?.Value == $"{{Binding {visibility}}}");
        }

        Assert.DoesNotContain("Settings.Editor.Current", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("settings-row", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsOverlay_UsesResponsiveTwoColumnCategoryWorkspace()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindowSettingsOverlay.axaml"));
        // ADR-015：外壳为无头带 DialogSurface（身份区折叠），token 仅作 Max 封顶。
        var surface = document
            .Descendants()
            .Single(element => element.Name.LocalName == "DialogSurface");
        Assert.Equal("Panel", surface.Attribute("Form")?.Value);
        Assert.True(HasClass(surface, "settings-surface"));
        Assert.Null(surface.Attribute("Width"));
        Assert.Null(surface.Attribute("Height"));
        Assert.Null(surface.Attribute("Title"));
        Assert.Null(surface.Attribute("HeaderIcon"));
        Assert.Equal("{StaticResource Launcher.Layout.Settings.MaxWidth}", surface.Attribute("MaxWidth")?.Value);
        Assert.Equal("{StaticResource Launcher.Layout.Settings.MaxHeight}", surface.Attribute("MaxHeight")?.Value);

        var workspace = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "settings-workspace"));
        Assert.Equal("Auto,*", workspace.Attribute("ColumnDefinitions")?.Value);

        var navigationPane = workspace
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "settings-navigation-pane"));
        Assert.Equal(
            "{StaticResource Launcher.Component.Settings.Navigation.Width}",
            navigationPane.Attribute("Width")?.Value);

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
        Assert.Equal("{x:Null}", navigation.Attribute("FocusAdorner")?.Value);
        Assert.Equal(
            "{Binding Code}",
            navigation.Attribute("SelectedValueBinding")?.Value);
        Assert.Equal(
            "Hidden",
            navigation.Attribute("ScrollViewer.VerticalScrollBarVisibility")?.Value);
        Assert.Equal(
            "Disabled",
            navigation.Attribute("ScrollViewer.HorizontalScrollBarVisibility")?.Value);
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
        var categoryHeadingCopy = content
            .Descendants()
            .Single(element => HasClass(element, "settings-content-heading-copy"));
        Assert.Equal(
            "{Binding SelectedItem, ElementName=SettingsNavigation}",
            categoryHeadingCopy.Attribute("DataContext")?.Value);
        Assert.Equal(
            "models:SettingOption",
            categoryHeadingCopy.Attributes()
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
        var categorySubtitle = content
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBlock"
                && HasClass(element, "category-subtitle"));
        Assert.Equal("{Binding Description}", categorySubtitle.Attribute("Text")?.Value);
        Assert.Single(
            content.Descendants(),
            element => element.Name.LocalName == "ScrollViewer");
        Assert.DoesNotContain(
            content.Descendants(),
            element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "settings-status-summary"));
    }

    [Fact]
    public void SettingsOverlay_UsesFinalM3SurfaceBlueprint()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindowSettingsOverlay.axaml"));
        // ADR-015：外壳表面自带滑移动效类；内容层不再拆分。
        var surface = document.Descendants().Single(element =>
            element.Name.LocalName == "DialogSurface");
        Assert.True(HasClass(surface, "motion-surface"));
        Assert.Single(document.Descendants(), element => HasClass(element, "settings-navigation-header"));

        var unselectedNavigationIcon = document
            .Descendants()
            .Single(element => element.Name.LocalName == "MaterialIcon" && element.Attribute("Kind")?.Value == "{Binding IconKind}");
        Assert.Equal("{StaticResource Launcher.Icon.Md}", unselectedNavigationIcon.Attribute("Width")?.Value);
        Assert.Equal("{StaticResource Launcher.Icon.Md}", unselectedNavigationIcon.Attribute("Height")?.Value);
        Assert.Equal(
            "settings-navigation-icon-outline",
            unselectedNavigationIcon.Attribute("Classes")?.Value);

        var selectedNavigationIcon = document
            .Descendants()
            .Single(element => element.Name.LocalName == "MaterialIcon" && element.Attribute("Kind")?.Value == "{Binding SelectedIconKind}");
        Assert.Equal("{StaticResource Launcher.Icon.Md}", selectedNavigationIcon.Attribute("Width")?.Value);
        Assert.Equal("{StaticResource Launcher.Icon.Md}", selectedNavigationIcon.Attribute("Height")?.Value);
        Assert.Equal(
            "settings-navigation-icon-filled",
            selectedNavigationIcon.Attribute("Classes")?.Value);

        var navigationHeader = document.Descendants().Single(element => HasClass(element, "settings-navigation-header"));
        Assert.DoesNotContain(
            navigationHeader.Descendants(),
            element => element.Name.LocalName == "MaterialIcon");

        var contentHeading = document.Descendants().Single(element => HasClass(element, "settings-content-heading"));
        Assert.Single(contentHeading.Descendants(), element =>
            element.Name.LocalName == "Button" && HasClass(element, "content-header-action"));
        Assert.DoesNotContain(document.Descendants(), element => HasClass(element, "settings-content-header"));
        Assert.DoesNotContain(document.Descendants(), element => HasClass(element, "dialog-header"));
    }

    [Fact]
    public void SettingsWorkspaceStyles_UseSemanticBrushesAndDesignTokens()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindow.Styles.axaml"));
        var dialogSurfaceStyles = XDocument.Load(TestRepository.FromApplicationRoot("Views/Styles/DialogSurface.axaml"));

        Assert.Equal(
            "{StaticResource Launcher.Spacing.None}",
            GetStyleSetters(document, "Grid.settings-workspace")["ColumnSpacing"]);
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Thickness.None}",
            GetStyleSetters(document, "Grid.settings-workspace")["Margin"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Surface}",
            GetStyleSetters(document, "ListBox.settings-navigation")["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Surface}",
            GetStyleSetters(document, "Grid.settings-navigation-pane")["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Surface}",
            GetStyleSetters(document, "Border.settings-navigation-header")["Background"]);
        Assert.Equal(
            "{StaticResource Launcher.Color.Transparent}",
            GetStyleSetters(document, "ListBox.settings-navigation")["BorderBrush"]);
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Thickness.None}",
            GetStyleSetters(document, "ListBox.settings-navigation")["BorderThickness"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.Settings.Navigation.List.Padding}",
            GetStyleSetters(document, "ListBox.settings-navigation")["Padding"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.Settings.Navigation.Header.Padding}",
            GetStyleSetters(document, "Border.settings-navigation-header")["Padding"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.Settings.Content.Padding}",
            GetStyleSetters(document, "Border.settings-content-padding")["Padding"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.Settings.Footer.Padding}",
            GetStyleSetters(document, "Border.settings-content-actions")["Padding"]);
        Assert.Equal(
            "False",
            GetStyleSetters(
                dialogSurfaceStyles,
                "controls|DialogSurface.settings-surface:panel /template/ Border#PART_FooterBand")["IsVisible"]);
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Thickness.None}",
            GetStyleSetters(document, "Button.dialog-close.content-header-action")["Margin"]);
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Thickness.None}",
            GetStyleSetters(document, "Border.settings-navigation-header > StackPanel.dialog-title-row")["Margin"]);
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Thickness.None}",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem")["BorderThickness"]);
        Assert.Equal(
            "{StaticResource Launcher.Color.Transparent}",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem")["BorderBrush"]);
        Assert.Equal(
            "{StaticResource Launcher.Radius.Md}",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem")["CornerRadius"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.Settings.Navigation.Item.Margin}",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem")["Margin"]);
        Assert.Equal(
            "{StaticResource Launcher.Control.Height.Setting}",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem")["MinHeight"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.Settings.Navigation.Item.Padding}",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem")["Padding"]);
        Assert.Equal(
            "False",
            GetStyleSetters(
                document,
                "ListBox.settings-navigation > ListBoxItem materialIcons|MaterialIcon.settings-navigation-icon-filled")["IsVisible"]);
        Assert.Equal(
            "False",
            GetStyleSetters(
                document,
                "ListBox.settings-navigation > ListBoxItem:selected materialIcons|MaterialIcon.settings-navigation-icon-outline")["IsVisible"]);
        Assert.Equal(
            "True",
            GetStyleSetters(
                document,
                "ListBox.settings-navigation > ListBoxItem:selected materialIcons|MaterialIcon.settings-navigation-icon-filled")["IsVisible"]);
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Thickness.None}",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem:selected")["BorderThickness"]);
        Assert.Equal(
            "{StaticResource Launcher.Color.Transparent}",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem:selected")["BorderBrush"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.SecondaryContainer}",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem:selected:not(:focus)")["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.OnSecondaryContainer}",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem:selected:not(:focus)")["Foreground"]);
        Assert.Equal(
            "{StaticResource Launcher.Color.Transparent}",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem:selected:not(:focus)")["BorderBrush"]);
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Thickness.None}",
            GetStyleSetters(document, "ListBox.settings-navigation > ListBoxItem:selected:not(:focus)")["BorderThickness"]);
        Assert.False(GetStyleSetters(document, "Grid.settings-content").ContainsKey("RowSpacing"));
        Assert.Equal(
            "{DynamicResource Launcher.Color.Dialog.Background}",
            GetStyleSetters(document, "Grid.settings-content")["Background"]);
        Assert.Equal(
            "{StaticResource Launcher.Color.ControlPanel.Gradient}",
            GetStyleSetters(document, "Border.control-panel")["Background"]);
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Xs}",
            GetStyleSetters(document, "StackPanel.settings-content-heading-copy")["Spacing"]);
        Assert.Equal(
            "{DynamicResource Launcher.Text.Secondary}",
            GetStyleSetters(document, "TextBlock.category-subtitle")["Foreground"]);
        Assert.DoesNotContain(
            document.Descendants(),
            element => HasClass(element, "settings-content-divider"));
        Assert.Equal(
            "{StaticResource Launcher.Spacing.None}",
            GetStyleSetters(document, "StackPanel.settings-sections")["Spacing"]);
        Assert.Equal(
            "{StaticResource Launcher.Typography.FontSize.Body.Md}",
            GetStyleSetters(document, "TextBlock.group-title")["FontSize"]);
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Thickness.None}",
            GetStyleSetters(document, "Grid.settings-row")["Margin"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.Settings.Row.MinHeight}",
            GetStyleSetters(document, "Grid.settings-row")["MinHeight"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Card.Border}",
            GetStyleSetters(document, "Border.settings-row-divider")["BorderBrush"]);
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Thickness.None}",
            GetStyleSetters(document, "Border.settings-row-divider")["Margin"]);

        var application = XDocument.Load(TestRepository.FromApplicationRoot("App.axaml"));
        var navigationHeaderPadding = application
            .Descendants()
            .Single(element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "Launcher.Component.Settings.Navigation.Header.Padding"));
        Assert.Equal("16,20", navigationHeaderPadding.Value);
        var contentPadding = application
            .Descendants()
            .Single(element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "Launcher.Component.Settings.Content.Padding"));
        Assert.Equal("16,24,0,16", contentPadding.Value);
        var contentScrollPadding = application
            .Descendants()
            .Single(element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "Launcher.Component.Settings.Content.Scroll.Padding"));
        Assert.Equal("0,0,16,0", contentScrollPadding.Value);
        var contentActionsPadding = application
            .Descendants()
            .Single(element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "Launcher.Component.Settings.Footer.Padding"));
        Assert.Equal("24,8,16,20", contentActionsPadding.Value);

        var overlayDocument = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindowSettingsOverlay.axaml"));
        var scrollViewer = overlayDocument
            .Descendants()
            .Single(element => element.Name.LocalName == "ScrollViewer");
        Assert.True(HasClass(scrollViewer, "settings-content-scroll"));
        Assert.Equal("Auto", scrollViewer.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("Disabled", scrollViewer.Attribute("HorizontalScrollBarVisibility")?.Value);
    }

    [Fact]
    public void GeneralSection_UsesThreeSettingsGroupsInFixedOrder()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/SettingsGeneralSection.axaml"));
        var groups = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "StackPanel"
                && HasClass(element, "settings-group"))
            .ToList();

        Assert.Equal(3, groups.Count);

        Assert.Equal(
            [
                "{Binding Shell.I18n[settingsGroupAppPreferences]}",
                "{Binding Shell.I18n[settingsGroupDisplay]}",
                "{Binding Shell.I18n[settingsGroupUpdates]}"
            ],
            groups.Select(group => group
                .Elements()
                .First(element => element.Name.LocalName == "TextBlock")
                .Attribute("Text")?.Value));
    }

    [Fact]
    public void AppearanceSection_UsesTwoSettingsGroupsForConsistentVerticalRhythm()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/SettingsAppearanceSection.axaml"));
        var groups = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "StackPanel"
                && HasClass(element, "settings-group"))
            .ToList();

        Assert.Equal(2, groups.Count);

        Assert.Equal(
            "{Binding Shell.I18n[settingsGroupThemeColor]}",
            groups[0]
                .Elements()
                .First(element => element.Name.LocalName == "TextBlock")
                .Attribute("Text")?.Value);
        Assert.Equal(
            "{Binding Shell.I18n[settingsGroupBackground]}",
            groups[1]
                .Elements()
                .First(element => element.Name.LocalName == "TextBlock")
                .Attribute("Text")?.Value);
        var neutralStrategyRow = groups[0].Descendants().Single(element =>
            element.Name.LocalName == "SettingSelect"
            && element.Attribute("Title")?.Value
                == "{Binding Shell.I18n[neutralColorStrategy]}");
        Assert.Equal(
            "{Binding Settings.Options.NeutralColorStrategy}",
            neutralStrategyRow.Attribute("ItemsSource")?.Value);
    }

    [Fact]
    public void AppearancePalette_ReservesWidthForFiveInteractiveSwatches()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/SettingsAppearanceSection.axaml"));
        var palette = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ItemsControl"
                && element.Attribute("ItemsSource")?.Value
                    == "{Binding Settings.Appearance.ThemeColorPaletteItems}");

        Assert.Equal(
            "{StaticResource Launcher.Layout.Appearance.Preview.Width}",
            palette.Attribute("Width")?.Value);
    }

    [Fact]
    public void AboutSection_UsesSettingsGroupForTopLevelRhythm()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/SettingsAboutSection.axaml"));
        var root = document.Root?.Elements().Single(element => element.Name.LocalName == "StackPanel");

        Assert.NotNull(root);
        Assert.True(HasClass(root!, "settings-group"));
    }

    [Fact]
    public void AboutSection_LegalLinks_ShareRowsWithTheirCopyrightText()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/SettingsAboutSection.axaml"));
        var links = document
            .Descendants()
            .Where(element => element.Name.LocalName == "HyperlinkButton")
            .ToList();

        Assert.Equal(2, links.Count);
        Assert.All(links, link =>
        {
            Assert.True(HasClass(link, "inline-legal-link"));
            Assert.False(HasClass(link, "text-link"));
            Assert.Equal("Grid", link.Parent?.Name.LocalName);
            Assert.Contains(link.Parent!.Elements(), element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value.Contains("Shell.I18n[", StringComparison.Ordinal) == true);
        });
        Assert.Equal(
            [
                "{Binding WindowChrome.OpenPrivacyPolicyCommand}",
                "{Binding WindowChrome.OpenDefaultBackgroundArtworkCommand}"
            ],
            links.Select(link => link.Attribute("Command")?.Value));
    }

    [Fact]
    public void AboutSection_WithRedesignedLayout_RendersIdentityCardAndKeyValueRows()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/SettingsAboutSection.axaml"));

        // ADR-018 融合变体：身份卡 = 产品名 + 版本 caption + 副标题，操作行内收。
        Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Border" && HasClass(element, "about-identity-card"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Image" || HasClass(element, "about-app-icon"));
        Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && HasClass(element, "about-product-name"));

        // 版本详细信息 = 5 个 key-value 行（版本/构建时间已在身份卡 caption，不重复）。
        var kvRows = document.Descendants()
            .Where(element => element.Name.LocalName == "Grid" && HasClass(element, "about-kv-row"))
            .ToList();
        Assert.Equal(5, kvRows.Count);
        Assert.All(kvRows, row => Assert.Single(
            row.Elements(),
            element => HasClass(element, "about-kv-value")));
        Assert.Equal(
            [
                "{Binding Shell.CommitShaValue}",
                "{Binding Shell.BuildConfigValue}",
                "{Binding Shell.FrameworkVersionText}",
                "{Binding Shell.AvaloniaVersionText}",
                "{Binding Shell.PlatformValue}"
            ],
            kvRows.SelectMany(row => row.Elements())
                .Where(element => HasClass(element, "about-kv-value"))
                .Select(element => element.Attribute("Text")?.Value));

    }

    [Fact]
    public void SettingsRuntimePaths_OnLinux_LiveOnlyInGameSection()
    {
        var gameDocument = XDocument.Load(TestRepository.FromApplicationRoot("Views/SettingsGameSection.axaml"));
        var advancedDocument = XDocument.Load(TestRepository.FromApplicationRoot("Views/SettingsAdvancedSection.axaml"));
        var runtimeBindings = new[]
        {
            "{Binding Settings.Editor.Current.GameRuntime.RunnerPath, Mode=TwoWay}",
            "{Binding Settings.Editor.Current.GameRuntime.PrefixPath, Mode=TwoWay}",
            "{Binding Settings.Editor.Current.GameRuntime.ProtonPath, Mode=TwoWay}"
        };

        foreach (var binding in runtimeBindings)
        {
            Assert.DoesNotContain(
                advancedDocument.Descendants(),
                element => element.Attribute("Text")?.Value == binding);
            Assert.Contains(
                gameDocument.Descendants(),
                element => element.Attribute("Text")?.Value == binding);
        }

        var runnerPathInput = gameDocument
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBox"
                && element.Attribute("Text")?.Value == runtimeBindings[0]);
        Assert.Equal(
            "{Binding Settings.IsGameRuntimeRunnerPathEnabled}",
            runnerPathInput.Attribute("IsEnabled")?.Value);

        var runtimeInputs = gameDocument
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "TextBox"
                && runtimeBindings.Contains(element.Attribute("Text")?.Value))
            .ToArray();
        Assert.Equal(3, runtimeInputs.Length);
        Assert.All(
            runtimeInputs,
            input => Assert.Equal(
                "{StaticResource Launcher.Component.Settings.Control.MinWidth}",
                input.Attribute("Width")?.Value));
        // 路径字段是普通文本输入：字段外观经 field-input；等宽（uid-input 叠层）
        // 只属于 UID 字段，不得随字段外观捆绑回归。
        Assert.All(
            runtimeInputs,
            input =>
            {
                Assert.True(HasClass(input, "field-input"));
                Assert.False(HasClass(input, "uid-input"));
            });

        // 运行环境路径与运行器选择同组（兼容运行环境），组内不再依赖诊断分区的位置。
        var runtimeGroup = runnerPathInput
            .Ancestors()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && HasClass(element, "settings-group"));
        Assert.Contains(
            runtimeGroup.Descendants(),
            element => element.Name.LocalName == "SettingSelect"
                && element.Attribute("Title")?.Value == "{Binding Shell.I18n[gameRuntimeRunner]}");

        var styles = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindow.Styles.axaml"));
        var fieldInputStyle = GetStyleSetters(styles, "TextBox.field-input");
        Assert.Equal(
            "{StaticResource Launcher.Control.Height.Field}",
            fieldInputStyle["MinHeight"]);
        Assert.Equal("Center", fieldInputStyle["VerticalAlignment"]);
        var uidInputStyle = GetStyleSetters(styles, "TextBox.uid-input");
        Assert.Equal(
            "{StaticResource Launcher.Typography.FontFamily.Monospace}",
            uidInputStyle["FontFamily"]);
        Assert.Equal(
            "{StaticResource Launcher.Typography.FontSize.Title.Md}",
            uidInputStyle["FontSize"]);
    }

    [Fact]
    public void SettingsNavigation_TokenizeFocusVisibleRings() // spec §8 visible focus ring
    {
        var styles = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindow.Styles.axaml"));
        var selector = "ListBox.settings-navigation > ListBoxItem:focus-visible";
        Assert.Equal(
            "{DynamicResource Launcher.Color.FocusRing}",
            GetStyleSetters(styles, selector)["BorderBrush"]);
        Assert.Equal(
            "{StaticResource Launcher.Border.Thickness.Focus}",
            GetStyleSetters(styles, selector)["BorderThickness"]);
    }

    [Fact]
    public void SettingsNavigation_HoverAndSelectionOverrideFluentDefaultColors()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindow.Styles.axaml"));
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
            "{DynamicResource Launcher.Color.Button.Flat.Hover}",
            hoverSetters["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Text.Primary}",
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

            var expectedBackground = selector.Contains(":selected:pointerover", StringComparison.Ordinal)
                ? "{DynamicResource Launcher.Color.SecondaryContainer.Hover}"
                : selector.Contains(":selected:pressed", StringComparison.Ordinal)
                    ? "{DynamicResource Launcher.Color.SecondaryContainer.Pressed}"
                    : selector.Contains(":selected", StringComparison.Ordinal)
                        ? "{DynamicResource Launcher.Color.SecondaryContainer}"
                        : "{DynamicResource Launcher.Color.Button.Flat.Pressed}";
            var expectedForeground = selector.Contains(":selected", StringComparison.Ordinal)
                ? "{DynamicResource Launcher.Color.OnSecondaryContainer}"
                : "{DynamicResource Launcher.Text.Primary}";
            Assert.Equal(expectedBackground, setters["Background"]);
            Assert.Equal(expectedForeground, setters["Foreground"]);
        }
    }

    [Fact]
    public void AppearanceSection_NeutralStrategyHint_IsLocalizedAndConditionallyVisible() // ADR-010
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/SettingsAppearanceSection.axaml"));
        // The hint renders inside the neutral-strategy row (SettingRow.Hint) so
        // multi-line copy keeps the row's internal spacing instead of an
        // out-of-row margin stacking on the row's min-height padding.
        var neutralRow = document
            .Descendants()
            .Single(element => element.Name.LocalName == "SettingSelect"
                && element.Attribute("AutomationName")?.Value
                    == "{Binding Shell.I18n[neutralColorStrategy]}");
        Assert.Equal(
            "{Binding Shell.I18n[neutralColorStrategySeedFollowingHint]}",
            neutralRow.Attribute("Hint")?.Value);
        Assert.Equal(
            "{Binding Settings.Appearance.IsSeedFollowingNeutralStrategySelected}",
            neutralRow.Attribute("IsHintVisible")?.Value);
        Assert.DoesNotContain(document.Descendants(), element => HasClass(element, "settings-neutral-hint"));
    }

    [Fact]
    public void SettingRow_LongCopyWrapsInFlexibleContentColumn()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Controls/SettingRow.axaml"));
        var layout = document
            .Descendants()
            .Single(element => element.Name.LocalName == "Grid" && HasClass(element, "settings-row"));
        var copy = layout
            .Elements()
            .Single(element => element.Name.LocalName == "StackPanel");
        var textBlocks = copy
            .Elements()
            .Where(element => element.Name.LocalName == "TextBlock")
            .ToList();
        var action = layout
            .Elements()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == "ActionPresenter"));
        var application = XDocument.Load(TestRepository.FromApplicationRoot("App.axaml"));
        var minWidthToken = application
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key"
                && attribute.Value == "Launcher.Component.Settings.Row.Content.MinWidth"));

        Assert.Equal("*,Auto", layout.Attribute("ColumnDefinitions")?.Value);
        Assert.Null(copy.Attribute("Grid.Column"));
        Assert.Equal(0, double.Parse(minWidthToken.Value, CultureInfo.InvariantCulture));
        Assert.Equal(
            "{StaticResource Launcher.Component.Settings.Row.Content.MinWidth}",
            copy.Attribute("MinWidth")?.Value);
        Assert.Equal("Stretch", copy.Attribute("HorizontalAlignment")?.Value);
        // 行内容垂直内边距保证多行文案（如 ADR-010 提示）不贴住上下分隔线。
        Assert.Equal(
            "{StaticResource Launcher.Component.Settings.Row.Content.Padding}",
            copy.Attribute("Margin")?.Value);
        Assert.Equal(3, textBlocks.Count);
        Assert.All(textBlocks, text => Assert.Equal("Wrap", text.Attribute("TextWrapping")?.Value));
        Assert.Equal("1", action.Attribute("Grid.Column")?.Value);
        Assert.DoesNotContain(
            layout.Descendants(),
            element => element.Name.LocalName == "MaterialIcon");
    }

    [Fact]
    public void SettingsPanel_UsesTransactionalSaveAndCancelActions()
    {
        var settingsOverlay = File.ReadAllText(TestRepository.FromApplicationRoot("Views/MainWindowSettingsOverlay.axaml"));
        var gameSection = File.ReadAllText(TestRepository.FromApplicationRoot("Views/SettingsGameSection.axaml"));
        var mainWindowCodeBehind = File.ReadAllText(TestRepository.FromApplicationRoot("Views/MainWindow.axaml.cs"));

        Assert.Contains(
            "Description=\"{Binding Settings.GamePathDisplay}\"",
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
        var settingsDocument = XDocument.Parse(settingsOverlay);
        var settingsFooterButtons = settingsDocument
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Button"
                && HasClass(element, "dialog-action")
                && element.Attribute("Command")?.Value
                    is "{Binding WindowChrome.ShowSettingsCommand}"
                    or "{Binding Settings.SaveSettingsCommand}")
            .ToList();
        Assert.Equal(2, settingsFooterButtons.Count);
        Assert.DoesNotContain(
            settingsDocument.Descendants(),
            element => element.Name.LocalName == "DialogSurface.Footer");
        var settingsContentActions = settingsDocument
            .Descendants()
            .Single(element => element.Name.LocalName == "Border" && HasClass(element, "settings-content-actions"));
        Assert.Equal("1", settingsContentActions.Attribute("Grid.Row")?.Value);
        Assert.DoesNotContain(
            settingsContentActions.Ancestors(),
            element => HasClass(element, "settings-content-padding"));
        Assert.Equal(
            2,
            settingsContentActions.Descendants()
                .Count(element =>
                    element.Name.LocalName == "Button"
                    && HasClass(element, "dialog-action")));
        Assert.DoesNotContain(
            "Kind=\"ContentSave\" Width=\"{StaticResource Launcher.Icon.Md}\" Height=\"{StaticResource Launcher.Icon.Md}\" Foreground=",
            settingsOverlay,
            StringComparison.Ordinal);
        // The escape-key resolution for the settings modal is a ModalRegistration
        // (ADR-023): kind, visibility source, content, and escape command colocated
        // in ShellLifecycle's RegisterModals.
        var shellLifecycle = File.ReadAllText(TestRepository.FromApplicationRoot("Features/Shell/ShellLifecycle.cs"));
        Assert.Contains(
            "ModalKind.Settings,",
            shellLifecycle,
            StringComparison.Ordinal);
        Assert.Contains(
            "windowChrome.ShowSettingsCommand",
            shellLifecycle,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "vm.WindowChrome.IsSettingsVisible",
            mainWindowCodeBehind,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Views/SettingsGameSection.axaml", "{Binding Operations.RequestUninstallCommand}")]
    [InlineData("Views/SettingsAdvancedSection.axaml", "{Binding Settings.RequestResetSettingsCommand}")]
    public void SettingsDangerActions_WithDestructiveCommands_UseDangerActionStyle(string sectionPath, string command)
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot(sectionPath));
        var button = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == command);

        Assert.True(HasClass(button, "flat-action"), $"{command} must inherit flat-action geometry.");
        Assert.True(HasClass(button, "danger-action"), $"{command} must use danger-action.");
    }

    [Fact]
    public void SettingsDangerActionStyle_WithDangerOverrides_RestoresFlatActionGeometry()
    {
        var styles = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindow.Styles.axaml"));
        var settingDanger = GetStyleSetters(styles, "Button.flat-action.danger-action");

        Assert.Equal("{DynamicResource Launcher.Color.Error}", settingDanger["BorderBrush"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Default}", settingDanger["BorderThickness"]);
        Assert.Equal("{StaticResource Launcher.Control.Height.Setting}", settingDanger["MinHeight"]);
        Assert.Equal("{StaticResource Launcher.Component.Action.Outlined.Padding}", settingDanger["Padding"]);
    }

    [Fact]
    public void SettingsOverlay_RemovesTopStatusSummaryAndUsesInlineContentHeading()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindowSettingsOverlay.axaml"));
        var settingsContent = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "settings-content"));

        Assert.Equal("*,Auto", settingsContent.Attribute("RowDefinitions")?.Value);
        var contentHeading = settingsContent.Descendants().Single(element =>
            HasClass(element, "settings-content-heading"));
        // 标题行含关闭按钮，必须固定在滚动区之外，滚动到末尾也不消失。
        Assert.Equal(
            "Auto,*",
            contentHeading.Parent!.Attribute("RowDefinitions")?.Value);
        var contentScrollViewer = settingsContent.Descendants().Single(element =>
            element.Name.LocalName == "ScrollViewer");
        Assert.DoesNotContain(contentScrollViewer.Descendants(), element =>
            HasClass(element, "settings-content-heading"));
        Assert.DoesNotContain(
            contentScrollViewer.Descendants(),
            element => element.Name.LocalName == "Button" && HasClass(element, "dialog-close"));
        Assert.Single(contentScrollViewer.Descendants(), element =>
            HasClass(element, "settings-sections"));
        Assert.Single(settingsContent.Descendants(), element => HasClass(element, "settings-content-heading"));
        Assert.DoesNotContain(
            document.Descendants(),
            element => HasClass(element, "settings-status-summary"));
    }

    [Fact]
    public void SettingsNavigation_SelectedItemUsesSemiboldText()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindow.Styles.axaml"));
        var selected = GetStyleSetters(
            document,
            "ListBox.settings-navigation > ListBoxItem:selected");

        Assert.Equal(
            "{StaticResource Launcher.Typography.FontWeight.Strong}",
            selected["FontWeight"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.SecondaryContainer}",
            selected["Background"]);
    }

    [Fact]
    public void SettingsGroups_UseAvailableContentWidth()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindow.Styles.axaml"));
        var settingsGroup = GetStyleSetters(document, "StackPanel.settings-group");

        Assert.DoesNotContain("MaxWidth", settingsGroup.Keys);
        Assert.DoesNotContain("HorizontalAlignment", settingsGroup.Keys);
    }

    [Fact]
    public void SettingsSections_InteractiveControlsHaveLocalizedAutomationNames()
    {
        var sectionPaths = new[]
        {
            "Views/SettingsGeneralSection.axaml",
            "Views/SettingsGameSection.axaml",
            "Views/SettingsDownloadNetworkSection.axaml",
            "Views/SettingsAppearanceSection.axaml",
            "Views/SettingsAdvancedSection.axaml",
            "Views/SettingsAboutSection.axaml"
        };
        var interactiveControlNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Button",
            "ColorPicker",
            "ComboBox",
            "SettingSelect",
            "TextBox",
            "ToggleSwitch"
        };

        foreach (var sectionPath in sectionPaths)
        {
            var document = XDocument.Load(TestRepository.FromApplicationRoot(sectionPath));
            var controls = document
                .Descendants()
                .Where(element => interactiveControlNames.Contains(element.Name.LocalName))
                .ToList();

            Assert.NotEmpty(controls);
            Assert.All(
                controls,
                control =>
                {
                    var automationName = control
                        .Attributes()
                        .SingleOrDefault(attribute =>
                            attribute.Name.LocalName == (control.Name.LocalName == "SettingSelect"
                                ? "AutomationName"
                                : "AutomationProperties.Name"))
                        ?.Value;

                    Assert.False(
                        string.IsNullOrWhiteSpace(automationName),
                        $"{sectionPath}: {control.Name.LocalName} is missing AutomationProperties.Name.");
                    // Item-templated controls (e.g. palette swatches) bind a unique,
                    // language-neutral name from the item model instead of a resource key.
                    Assert.True(
                        automationName.Contains("Shell.I18n[", StringComparison.Ordinal)
                        || automationName.StartsWith("{Binding ", StringComparison.Ordinal),
                        $"{sectionPath}: {control.Name.LocalName} AutomationProperties.Name is neither localized nor item-bound.");
                });
        }
    }

    [Fact]
    public void AdvancedSettings_KeepsLogActionsInDiagnosticsRow()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/SettingsAdvancedSection.axaml"));
        var group = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && HasClass(element, "settings-group")
                && element.Elements().Any(child =>
                    child.Name.LocalName == "TextBlock"
                    && child.Attribute("Text")?.Value == "{Binding Shell.I18n[settingsGroupDiagnostics]}"));
        var rows = group
            .Elements()
            .Where(element => element.Name.LocalName is "SettingRow" or "SettingSelect")
            .ToList();

        // 重置设置行加入后共三行；日志行动作按标题定位，避免对行顺序的脆弱依赖。
        Assert.Equal(3, rows.Count);
        var logFilesRow = Assert.Single(
            rows,
            row => row.Attribute("Title")?.Value == "{Binding Shell.I18n[logFiles]}");
        Assert.Equal(
            "{Binding Shell.I18n[logFilesDescription]}",
            logFilesRow.Attribute("Description")?.Value);

        var action = logFilesRow
            .Elements()
            .Single(element => element.Name.LocalName == "SettingRow.Action");
        var actionPanel = action
            .Elements()
            .Single(element => element.Name.LocalName == "WrapPanel");
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Sm}",
            actionPanel.Attribute("ItemSpacing")?.Value);
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Sm}",
            actionPanel.Attribute("LineSpacing")?.Value);
        Assert.Equal(
            "{StaticResource Launcher.Component.Settings.Row.Action.MaxWidth}",
            actionPanel.Attribute("MaxWidth")?.Value);

        var app = XDocument.Load(TestRepository.FromApplicationRoot("App.axaml"));
        var actionMaxWidth = app
            .Descendants()
            .Single(element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "Launcher.Component.Settings.Row.Action.MaxWidth"));
        Assert.Equal("440", actionMaxWidth.Value);

        var commands = actionPanel
            .Elements()
            .Where(element => element.Name.LocalName == "Button")
            .Select(element =>
                element.Attribute("Command")?.Value
                ?? throw new InvalidDataException("Advanced log action is missing Command."))
            .ToArray();
        Assert.Equal(
            [
                "{Binding LogViewer.OpenCommand}",
                "{Binding LogExport.OpenCommand}",
                "{Binding WindowChrome.OpenDataDirectoryCommand}"
            ],
            commands);
        Assert.DoesNotContain(
            group.Elements(),
            element => element.Name.LocalName == "WrapPanel");
    }

    [Fact]
    public void SettingsAboutAndAdvancedActions_UsePurposeBasedOrderAndExclusiveOwnership()
    {
        var aboutText = File.ReadAllText(TestRepository.FromApplicationRoot("Views/SettingsAboutSection.axaml"));
        var advancedText = File.ReadAllText(TestRepository.FromApplicationRoot("Views/SettingsAdvancedSection.axaml"));
        var overlay = File.ReadAllText(TestRepository.FromApplicationRoot("Views/MainWindowSettingsOverlay.axaml"));

        // ADR-018 融合变体顺序：身份卡（版本 caption + 操作行）→ 版本信息 kv（5 行详情）
        // → 链接行（仓库/问题反馈）→ 法律信息（版权/署名/免责声明）。
        AssertOrdered(
            aboutText,
            "Shell.VersionCaptionText",
            "Settings.CheckForUpdatesCommand",
            "WindowChrome.OpenAboutOfficialSiteCommand",
            "WindowChrome.OpenHelpDocsCommand",
            "Shell.I18n[versionInfo]",
            "Shell.CommitShaValue",
            "Shell.BuildConfigValue",
            "Shell.FrameworkVersionText",
            "Shell.AvaloniaVersionText",
            "Shell.PlatformValue");
        AssertOrdered(
            aboutText,
            "Settings.CheckForUpdatesCommand",
            "WindowChrome.OpenAboutOfficialSiteCommand",
            "WindowChrome.OpenHelpDocsCommand",
            "WindowChrome.OpenGitHubRepositoryCommand",
            "WindowChrome.OpenGitHubReleaseRepositoryCommand",
            "WindowChrome.OpenIssueTrackerCommand",
            "Shell.I18n[aboutCopyrightText]",
            "WindowChrome.OpenPrivacyPolicyCommand",
            "Shell.I18n[defaultBackgroundCopyrightText]",
            "WindowChrome.OpenDefaultBackgroundArtworkCommand",
            "Shell.I18n[aboutDisclaimerText]");
        AssertOrdered(
            advancedText,
            "LogViewer.OpenCommand",
            "LogExport.OpenCommand",
            "WindowChrome.OpenDataDirectoryCommand");

        Assert.DoesNotContain("LogViewer.OpenCommand", aboutText, StringComparison.Ordinal);
        Assert.DoesNotContain("LogExport.OpenCommand", aboutText, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowChrome.OpenDataDirectoryCommand", aboutText, StringComparison.Ordinal);
        Assert.Contains("Shell.I18n[settingsGroupDiagnostics]", advancedText, StringComparison.Ordinal);

        var document = XDocument.Parse(overlay);
        var footerButtons = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Button"
                && HasClass(element, "dialog-action")
                && element.Attribute("Command")?.Value
                    is "{Binding WindowChrome.ShowSettingsCommand}"
                    or "{Binding Settings.SaveSettingsCommand}")
            .ToList();
        Assert.Equal(2, footerButtons.Count);
        Assert.Equal(
            "{Binding WindowChrome.ShowSettingsCommand}",
            footerButtons[0].Attribute("Command")?.Value);
        Assert.Equal(
            "{Binding Settings.SaveSettingsCommand}",
            footerButtons[1].Attribute("Command")?.Value);
    }
}
