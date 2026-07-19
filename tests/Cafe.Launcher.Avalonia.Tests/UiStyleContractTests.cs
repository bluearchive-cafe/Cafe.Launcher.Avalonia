using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed partial class UiStyleContractTests
{
    [Fact]
    public void LauncherIcons_UserFacingActionsAndSettings_UseApprovedSemanticMappings()
    {
        var mainWindow = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var generalSettings = XDocument.Load(ProjectFile("Views/SettingsGeneralSection.axaml"));
        var downloadNetworkSettings = XDocument.Load(ProjectFile("Views/SettingsDownloadNetworkSection.axaml"));
        var dialogs = XDocument.Load(ProjectFile("Views/MainWindowDialogsOverlay.axaml"));
        var settingsOverlay = XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));

        var detectButton = mainWindow
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding Settings.SelectInstalledGameCommand}");
        Assert.Equal(
            "FolderSearchOutline",
            detectButton.Descendants().Single(element => element.Name.LocalName == "MaterialIcon").Attribute("Kind")?.Value);

        AssertSettingRowIcon(generalSettings, "{Binding Shell.I18n.CloseBehavior}", "WindowClose");
        AssertSettingRowIcon(downloadNetworkSettings, "{Binding Shell.I18n.Proxy}", "serverNetworkOutline");
        AssertSettingRowIcon(downloadNetworkSettings, "{Binding Shell.I18n.LauncherUpdateChannel}", "SourceBranch");

        var resourcePanelButton = mainWindow
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding ResourcePanel.OpenResourcePanelCommand}");
        Assert.Equal(
            "Web",
            resourcePanelButton.Descendants().Single(element => element.Name.LocalName == "MaterialIcon").Attribute("Kind")?.Value);
        AssertSettingRowIcon(downloadNetworkSettings, "{Binding Shell.I18n.DownloadSource}", "Web");

        var resourcePanelHeadingIcon = dialogs
            .Descendants()
            .First(element => element.Name.LocalName == "MaterialIcon");
        Assert.Equal("Web", resourcePanelHeadingIcon.Attribute("Kind")?.Value);

        var settingsFooterIcons = settingsOverlay
            .Descendants()
            .Where(element => element.Name.LocalName == "MaterialIcon")
            .Select(element => element.Attribute("Kind")?.Value)
            .ToArray();
        Assert.Contains("CloseCircleOutline", settingsFooterIcons);
        Assert.Contains("ContentSave", settingsFooterIcons);
    }

    [Fact]
    public void MainWindow_OperationPanels_UseStableStatusAndActionColumns()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var operationLayouts = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "operation-layout"))
            .ToArray();

        Assert.Equal(3, operationLayouts.Length);
        Assert.All(operationLayouts, layout =>
        {
            Assert.Equal("*,Auto", layout.Attribute("ColumnDefinitions")?.Value);
            Assert.Equal(
                "{StaticResource LauncherSpacingXl}",
                layout.Attribute("ColumnSpacing")?.Value);
            Assert.True(HasClass(layout.Parent!, "bottom-panel"));

            var status = layout.Elements().Single(element => HasClass(element, "operation-status"));
            Assert.Equal("Grid", status.Name.LocalName);
            var statusColumns = status
                .Elements()
                .Single(element => element.Name.LocalName == "Grid.ColumnDefinitions")
                .Elements()
                .ToArray();
            Assert.Equal(2, statusColumns.Length);
            Assert.Equal("Auto", statusColumns[0].Attribute("Width")?.Value);
            Assert.Equal(
                "{StaticResource LauncherIconXxl}",
                statusColumns[0].Attribute("MinWidth")?.Value);
            Assert.Equal("*", statusColumns[1].Attribute("Width")?.Value);
            Assert.Equal(
                "{StaticResource LauncherSpacingMd}",
                status.Attribute("ColumnSpacing")?.Value);
            Assert.Contains(
                status.Descendants(),
                element => element.Name.LocalName == "TextBlock"
                    && HasClass(element, "operation-status-title"));
            var statusIcon = status.Elements().Single(element => element.Name.LocalName == "MaterialIcon");
            Assert.Null(statusIcon.Attribute("Grid.Column"));
            Assert.All(
                status.Elements().Where(element =>
                    element != statusIcon
                    && element.Name.LocalName != "Grid.ColumnDefinitions"),
                element => Assert.Equal("1", element.Attribute("Grid.Column")?.Value));

            var actions = layout.Elements().Single(element => HasClass(element, "operation-actions"));
            Assert.Equal("1", actions.Attribute("Grid.Column")?.Value);
        });
    }

    [Fact]
    public void MainWindow_InstallPanel_AlignsPathWithPrimaryActionAndKeepsRefreshInStatusHeader()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var installLayout = document
            .Descendants()
            .First(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "operation-layout"));
        var status = installLayout.Elements().Single(element => HasClass(element, "operation-status"));
        var actions = installLayout.Elements().Single(element => HasClass(element, "operation-actions"));

        var refreshButton = status
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding RefreshCommand}");
        var pathRow = status
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "install-path-row"));
        var pathField = pathRow
            .Elements()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "path-field"));
        var pathLayout = pathField
            .Elements()
            .Single(element => element.Name.LocalName == "Grid");
        var pathContent = pathLayout
            .Elements()
            .Single(element => element.Name.LocalName == "Grid");
        var changePathButton = pathContent
            .Elements()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding Settings.ChangePersistedGamePathCommand}");
        var pathButtons = pathRow
            .Elements()
            .Where(element => element.Name.LocalName == "Button")
            .ToArray();
        var detectButton = pathButtons
            .Single(element =>
                element.Attribute("Command")?.Value == "{Binding Settings.SelectInstalledGameCommand}");
        var installButton = pathButtons
            .Single(element =>
                element.Attribute("Command")?.Value == "{Binding Operations.InstallOrUpdateCommand}");

        Assert.Equal("1", refreshButton.Attribute("Grid.Column")?.Value);
        Assert.Equal("*,Auto,Auto", pathRow.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("{StaticResource LauncherSpacingSm}", pathRow.Attribute("ColumnSpacing")?.Value);
        Assert.Equal(2, pathButtons.Length);
        Assert.Equal("Auto,*", pathLayout.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("{StaticResource LauncherSpacingSm}", pathLayout.Attribute("ColumnSpacing")?.Value);
        Assert.Equal("*,Auto", pathContent.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("{StaticResource LauncherSpacingMd}", pathContent.Attribute("ColumnSpacing")?.Value);
        Assert.Equal("1", changePathButton.Attribute("Grid.Column")?.Value);
        Assert.DoesNotContain(
            pathField.Descendants(),
            element => element.Name.LocalName == "Border");
        Assert.Equal("1", detectButton.Attribute("Grid.Column")?.Value);
        Assert.Equal("2", installButton.Attribute("Grid.Column")?.Value);
        Assert.True(HasClass(installButton, "primary-operation"));
        Assert.True(HasClass(installButton, "path-operation"));
        Assert.DoesNotContain(
            installLayout.DescendantsAndSelf().Attributes(),
            attribute => attribute.Name.LocalName == "Margin"
                && !attribute.Value.StartsWith("{StaticResource Launcher", StringComparison.Ordinal));
        Assert.DoesNotContain(
            actions.Descendants(),
            element => element.Name.LocalName == "Button");
    }

    [Fact]
    public void MainWindow_OperationButtons_ExposeLocalizedNamesAndActionPriority()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        Dictionary<string, (string Name, string Priority)> expectedButtons = new(StringComparer.Ordinal)
        {
            ["{Binding RefreshCommand}"] = ("{Binding Shell.I18n.Refresh}", "secondary-operation"),
            ["{Binding Operations.InstallOrUpdateCommand}"] = ("{Binding Operations.InstallButtonText}", "primary-operation"),
            ["{Binding Settings.ChangePersistedGamePathCommand}"] = ("{Binding Shell.I18n.ChangePath}", "secondary-operation"),
            ["{Binding Settings.SelectInstalledGameCommand}"] = ("{Binding Shell.I18n.SelectInstalledGame}", "secondary-operation"),
            ["{Binding WindowChrome.OpenOfficialSiteCommand}"] = ("{Binding Shell.I18n.OfficialSite}", "secondary-operation"),
            ["{Binding Operations.StartGameCommand}"] = ("{Binding Shell.I18n.StartGame}", "primary-operation"),
            ["{Binding Operations.PauseResumeCommand}"] = ("{Binding Operations.PauseResumeText}", "secondary-operation"),
            ["{Binding Operations.StopOperationCommand}"] = ("{Binding Shell.I18n.Stop}", "secondary-operation")
        };

        foreach (var (command, expected) in expectedButtons)
        {
            var button = document
                .Descendants()
                .Single(element =>
                    element.Name.LocalName == "Button"
                    && element.Attribute("Command")?.Value == command);

            Assert.True(HasClass(button, expected.Priority), $"{command} must be {expected.Priority}.");
            Assert.Equal(
                expected.Name,
                button.Attributes()
                    .Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                    .Value);
            Assert.NotNull(button.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == "ToolTip.Tip"));
        }
    }

    [Fact]
    public void MainWindow_ControlPanel_ExplainsTheStartAction()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var controlPanel = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "control-panel"));
        var status = controlPanel
            .Descendants()
            .Single(element => HasClass(element, "operation-status"));

        Assert.Contains(
            status.Descendants(),
            element => element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding Shell.I18n.LaunchCheckDescription}");
    }

    [Fact]
    public void MainWindow_ProgressPanel_BindsOperationSpecificIcon()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var progressPanel = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && element.Attribute("IsVisible")?.Value
                    == "{Binding Operations.IsProgressPanelVisible}");
        var status = progressPanel
            .Descendants()
            .Single(element => HasClass(element, "operation-status"));
        var statusIcon = status
            .Descendants()
            .Single(element => element.Name.LocalName == "MaterialIcon");

        Assert.Equal(
            "{Binding Operations.ProgressIconKind}",
            statusIcon.Attribute("Kind")?.Value);
    }

    [Fact]
    public void MainWindow_TitleBarActions_ExposeAccessibleTokenizedPointerAndKeyboardFeedback()
    {
        var view = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        Dictionary<string, string> expectedNames = new(StringComparer.Ordinal)
        {
            ["{Binding ResourcePanel.OpenResourcePanelCommand}"] = "{Binding Shell.I18n.ResourcePanel}",
            ["{Binding WindowChrome.ShowSettingsCommand}"] = "{Binding Shell.I18n.Settings}",
            ["{Binding WindowChrome.MinimizeCommand}"] = "{Binding Shell.I18n.Minimize}",
            ["{Binding WindowChrome.CloseCommand}"] = "{Binding Shell.I18n.Close}"
        };

        var brandRow = view
            .Descendants()
            .Single(element => HasClass(element, "titlebar-brand-row"));
        var titleBar = brandRow.Parent!;
        Assert.Equal(
            ["{Binding Shell.ProductName}"],
            brandRow
                .Descendants()
                .Where(element => element.Name.LocalName == "TextBlock")
                .Select(element => element.Attribute("Text")?.Value ?? "")
                .ToArray());
        Assert.DoesNotContain(
            brandRow.Descendants(),
            element => element.Name.LocalName == "Image");

        var actionButtons = titleBar
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Button"
                && HasClass(element, "chrome"))
            .ToArray();
        Assert.Equal(expectedNames.Count, actionButtons.Length);

        foreach (var (command, expectedName) in expectedNames)
        {
            var button = actionButtons
                .Single(element =>
                    element.Attribute("Command")?.Value == command);

            Assert.True(HasClass(button, "chrome"));
            Assert.Equal(expectedName, button.Attribute("ToolTip.Tip")?.Value);
            Assert.Equal(
                expectedName,
                button.Attributes()
                    .Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                    .Value);
        }

        var chrome = GetStyleSetters(styles, "Button.chrome");
        Assert.Equal("{StaticResource LauncherControlHeightSetting}", chrome["Width"]);
        Assert.Equal("{StaticResource LauncherControlHeightSetting}", chrome["Height"]);
        Assert.Equal(
            "{DynamicResource LauncherChromeHoverBrush}",
            GetStyleSetters(styles, "Button.chrome:pointerover")["Background"]);
        Assert.Equal(
            "{DynamicResource LauncherDangerBrush}",
            GetStyleSetters(styles, "Button.chrome.close:pointerover")["Background"]);
        Assert.Equal(
            "{DynamicResource LauncherDangerPressedBrush}",
            GetStyleSetters(styles, "Button.chrome.close:pressed")["Background"]);

        var focus = GetStyleSetters(styles, "Button:focus-visible");
        Assert.Equal("{DynamicResource LauncherFocusRingBrush}", focus["BorderBrush"]);
        Assert.Equal("2", focus["BorderThickness"]);
    }

    [Fact]
    public void MainWindow_NewsList_UsesThreeRowScrollableViewportAndReadableRows()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var viewport = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ScrollViewer"
                && HasClass(element, "news-viewport"));
        var newsList = viewport.Elements().Single(element => element.Name.LocalName == "ItemsControl");
        var itemsPanel = newsList
            .Elements()
            .Single(element => element.Name.LocalName == "ItemsControl.ItemsPanel")
            .Descendants()
            .Where(element => element.Name.LocalName == "StackPanel")
            .Single();
        var rowButton = newsList.Descendants().Single(element =>
            element.Name.LocalName == "Button"
            && HasClass(element, "news-row"));
        var rowBorder = rowButton.Elements().Single(element => element.Name.LocalName == "Border");
        var title = rowButton.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "{Binding Title}");
        var date = rowButton.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "{Binding Subtitle}");

        Assert.Equal("{StaticResource LauncherNewsViewportHeight}", viewport.Attribute("Height")?.Value);
        Assert.Equal("Auto", viewport.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("Disabled", viewport.Attribute("HorizontalScrollBarVisibility")?.Value);
        Assert.Equal("StackPanel", itemsPanel.Name.LocalName);
        Assert.Equal("{StaticResource LauncherSpacingSm}", itemsPanel.Attribute("Spacing")?.Value);
        Assert.Equal("{StaticResource LauncherNewsRowHeight}", rowButton.Attribute("Height")?.Value);
        Assert.True(HasClass(rowButton, "content-link"));
        Assert.True(HasClass(rowBorder, "content-row"));
        Assert.True(HasClass(rowBorder, "news-content-row"));
        Assert.Equal("2", title.Attribute("MaxLines")?.Value);
        Assert.Equal("Wrap", title.Attribute("TextWrapping")?.Value);
        Assert.Equal("CharacterEllipsis", title.Attribute("TextTrimming")?.Value);
        Assert.Equal("{Binding Title}", title.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal("Right", date.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Right", date.Attribute("TextAlignment")?.Value);

        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var newsContentRow = GetStyleSetters(styles, "Border.content-row.news-content-row");
        Assert.Equal("{StaticResource LauncherThicknessSm}", newsContentRow["Padding"]);
        Assert.Equal("{StaticResource LauncherThicknessNone}", newsContentRow["Margin"]);
    }

    [Fact]
    public void MainWindow_CarouselNavigation_UsesTokenizedHitTargetsAndLocalizedNames()
    {
        var view = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        Dictionary<string, string> expectedNames = new(StringComparer.Ordinal)
        {
            ["{Binding RemoteContent.SelectPreviousBannerCommand}"] = "{Binding Shell.I18n.PreviousBanner}",
            ["{Binding RemoteContent.SelectNextBannerCommand}"] = "{Binding Shell.I18n.NextBanner}"
        };

        foreach (var (command, expectedName) in expectedNames)
        {
            var button = view
                .Descendants()
                .Single(element =>
                    element.Name.LocalName == "Button"
                    && element.Attribute("Command")?.Value == command);
            var icon = button.Elements().Single(element => element.Name.LocalName == "MaterialIcon");

            Assert.True(HasClass(button, "carousel-navigation"));
            Assert.Equal(expectedName, button.Attribute("ToolTip.Tip")?.Value);
            Assert.Equal(
                expectedName,
                button.Attributes()
                    .Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                    .Value);
            Assert.Equal("{StaticResource LauncherIconMd}", icon.Attribute("Width")?.Value);
            Assert.Equal("{StaticResource LauncherIconMd}", icon.Attribute("Height")?.Value);
        }

        var navigation = GetStyleSetters(styles, "Button.icon-button.carousel-navigation");
        Assert.Equal("{StaticResource LauncherControlHeightSetting}", navigation["Width"]);
        Assert.Equal("{StaticResource LauncherControlHeightSetting}", navigation["Height"]);
        Assert.Equal(
            "{DynamicResource LauncherChromeHoverBrush}",
            GetStyleSetters(styles, "Button.icon-button.carousel-navigation:pointerover")["Background"]);
        Assert.Equal(
            "{DynamicResource LauncherAccentSoftBrush}",
            GetStyleSetters(styles, "Button.icon-button.carousel-navigation:pressed")["Background"]);
    }

    [Fact]
    public void MainWindow_CarouselPlayback_KeepsPauseIndependentFromPageText()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var pauseButton = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding RemoteContent.ToggleCarouselLoopCommand}");
        var pageText = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding RemoteContent.CarouselPageText}");
        var pauseIcon = pauseButton.Elements().Single(element => element.Name.LocalName == "MaterialIcon");
        var sharedLayout = pageText
            .Ancestors()
            .Intersect(pauseButton.Ancestors())
            .First(element => element.Attributes().Any(attribute =>
                (attribute.Name.LocalName == "ColumnSpacing" || attribute.Name.LocalName == "Spacing")
                && attribute.Value.StartsWith("{StaticResource LauncherSpacing", StringComparison.Ordinal)));

        Assert.NotSame(pageText, pauseButton);
        Assert.NotNull(sharedLayout);
        Assert.Equal("{Binding RemoteContent.CarouselPageText}", pageText.Attribute("Text")?.Value);
        Assert.Equal(
            "{Binding RemoteContent.ToggleCarouselLoopCommand}",
            pauseButton.Attribute("Command")?.Value);
        Assert.Equal("{Binding RemoteContent.CarouselPauseTooltip}", pauseButton.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal(
            "{Binding RemoteContent.CarouselPauseTooltip}",
            pauseButton.Attributes()
                .Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                .Value);
        Assert.Equal("{Binding RemoteContent.CarouselPauseIcon}", pauseIcon.Attribute("Kind")?.Value);
    }

    private static readonly string[] StyleFiles =
    [
        "Views/MainWindow.Styles.axaml",
        "Views/Styles/Diagnostics.axaml",
        "Views/Styles/RemoteContent.axaml",
        "Views/Styles/SetupWizard.axaml",
        "Views/Styles/Toast.axaml"
    ];

    private static readonly string[] ViewFiles =
    [
        "Views/MainWindow.axaml",
        "Views/MainWindowSettingsOverlay.axaml",
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

    [Fact]
    public void SettingsSections_OwnEachBindingExactlyOnceAndUseCompiledBindings()
    {
        Dictionary<string, string[]> expectedBindings = new(StringComparer.Ordinal)
        {
            ["SettingsGeneralSection"] =
            [
                "Settings.Editor.Current.Language",
                "Settings.Editor.Current.CloseBehavior",
                "Settings.Editor.Current.MotionMode",
                "Settings.Editor.Current.ToastNotificationsEnabled"
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
                "Settings.Editor.Current.ShowRemoteContentCard",
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
            ["SettingsAdvancedSection"] =
            [
                "Settings.Editor.Current.LogLevel",
                "LogViewer.OpenCommand",
                "LogViewer.ExportCommand",
                "WindowChrome.OpenDataDirectoryCommand"
            ],
            ["SettingsAboutSection"] =
            [
                "Settings.CheckForUpdatesCommand",
                "WindowChrome.OpenOfficialSiteCommand",
                "WindowChrome.OpenHelpDocsCommand",
                "WindowChrome.OpenGitHubRepositoryCommand"
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
    public void SettingsOverlay_ReferencesSixCategorySectionsWithoutOwningSettingsRows()
    {
        var overlay = File.ReadAllText(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
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
        var document = XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
        var dialog = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "overlay-dialog"));
        Assert.Null(dialog.Attribute("Width"));
        Assert.Null(dialog.Attribute("Height"));
        Assert.Equal("960", dialog.Attribute("MaxWidth")?.Value);
        Assert.Equal("620", dialog.Attribute("MaxHeight")?.Value);
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
            "{DynamicResource LauncherFlatPressedBrush}",
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
            "{StaticResource LauncherFontSizeLg}",
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

        Assert.Equal(3, groups.Count);

        Assert.Equal(
            "{Binding Shell.I18n.SettingsGroupThemeColor}",
            groups[0]
                .Elements()
                .First(element => element.Name.LocalName == "TextBlock")
                .Attribute("Text")?.Value);
        Assert.Equal(
            "{Binding Shell.I18n.SettingsGroupBackground}",
            groups[1]
                .Elements()
                .First(element => element.Name.LocalName == "TextBlock")
                .Attribute("Text")?.Value);
        Assert.Equal(
            "{Binding Shell.I18n.SettingsGroupDisplay}",
            groups[2]
                .Elements()
                .First(element => element.Name.LocalName == "TextBlock")
                .Attribute("Text")?.Value);
    }

    [Fact]
    public void AppearancePalette_ReservesWidthForFiveInteractiveSwatches()
    {
        var document = XDocument.Load(ProjectFile("Views/SettingsAppearanceSection.axaml"));
        var palette = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ItemsControl"
                && element.Attribute("ItemsSource")?.Value
                    == "{Binding Settings.Appearance.ThemeColorPaletteItems}");

        Assert.Equal("212", palette.Attribute("Width")?.Value);
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
        Assert.Equal("16,0,4,0", resources["LauncherPathFieldPadding"]);
        var pathFieldPadding = document
            .Descendants()
            .Single(element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "LauncherPathFieldPadding"));
        Assert.Equal("Thickness", pathFieldPadding.Name.LocalName);
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

        Assert.Equal("11", resources["LauncherFontSizeXs"]);
        Assert.Equal("12", resources["LauncherFontSizeSm"]);
        Assert.Equal("13", resources["LauncherFontSizeMd"]);
        Assert.Equal("14", resources["LauncherFontSizeLg"]);
        Assert.Equal("15", resources["LauncherFontSizeXl"]);
        Assert.Equal("16", resources["LauncherFontSizeXxl"]);
        Assert.Equal("17", resources["LauncherFontSizeHeadingSm"]);
        Assert.Equal("18", resources["LauncherFontSizeHeadingMd"]);
        Assert.Equal("19", resources["LauncherFontSizeHeadingLg"]);
        Assert.Equal("22", resources["LauncherFontSizeDisplay"]);
        Assert.Equal("Normal", resources["LauncherFontWeightNormal"]);
        Assert.Equal("SemiBold", resources["LauncherFontWeightStrong"]);
        Assert.Equal("Consolas", resources["LauncherFontFamilyMonospace"]);

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
                "{StaticResource LauncherFont",
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
                    && attribute.Value == "LauncherFontFamilyMonospace"));
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
                        == "{StaticResource LauncherFontWeightStrong}"))
            .Select(element => element.Attribute("Selector")?.Value)
            .ToHashSet(StringComparer.Ordinal);

        var expectedSelectors = new HashSet<string>(StringComparer.Ordinal)
        {
            "TextBlock.heading",
            "TextBlock.dialog-title",
            "TextBlock.titlebar-brand",
            "TextBlock.progress-title",
            "TextBlock.panel-title",
            "TextBlock.section-title",
            "TextBlock.group-title",
            "TextBlock.category-title",
            "TextBlock.status-summary-title",
            "TextBlock.operation-status-title",
            "ListBox.settings-navigation > ListBoxItem:selected",
            "Button.primary-action",
            "Button.danger-action",
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
        Assert.Equal("{StaticResource LauncherThicknessLg}", settingsSection["Padding"]);
        Assert.Equal("{StaticResource LauncherRadiusMd}", settingsSection["CornerRadius"]);

        var contentRow = GetStyleSetters(document, "Border.content-row");
        Assert.Equal("12", contentRow["Padding"]);
        Assert.Equal("0,0,0,4", contentRow["Margin"]);
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
        Assert.Equal("220", colorPickerControl["Width"]);
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
    public void InteractiveControlStyles_UseSharedFocusAndHeightTokens()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));

        var iconLink = GetStyleSetters(document, "Button.icon-link");
        Assert.Equal("{StaticResource LauncherRadiusSm}", iconLink["CornerRadius"]);
        Assert.Equal("{StaticResource LauncherFontSizeLg}", iconLink["FontSize"]);
        Assert.Equal("Center", iconLink["HorizontalContentAlignment"]);
        Assert.Equal("Center", iconLink["VerticalContentAlignment"]);

        var flatAction = GetStyleSetters(document, "Button.flat-action");
        Assert.Equal(
            "{StaticResource LauncherControlHeightSetting}",
            flatAction["MinHeight"]);
        Assert.Equal("{StaticResource LauncherRadiusSm}", flatAction["CornerRadius"]);
        Assert.Equal("Center", flatAction["HorizontalContentAlignment"]);
        Assert.Equal("Center", flatAction["VerticalContentAlignment"]);

        var sharedButtonFocus = GetStyleSetters(document, "Button:focus-visible");
        Assert.Equal(
            "{DynamicResource LauncherFocusRingBrush}",
            sharedButtonFocus["BorderBrush"]);
        Assert.Equal("2", sharedButtonFocus["BorderThickness"]);

        var pathField = GetStyleSetters(document, "Border.path-field");
        Assert.Equal(
            "{StaticResource LauncherFieldHeight}",
            pathField["Height"]);
        Assert.Equal(
            "{StaticResource LauncherPathFieldPadding}",
            pathField["Padding"]);
        Assert.Equal(
            "{StaticResource LauncherDialogTitleHeight}",
            GetStyleSetters(document, "Grid.dialog-header")["Height"]);
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
                "{DynamicResource LauncherFlatPressedBrush}",
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
                     "Views/MainWindowLogViewerOverlay.axaml",
                     "Views/SetupWizardOverlay.axaml"
                 })
        {
            var text = File.ReadAllText(ProjectFile(relativePath));
            Assert.DoesNotContain("ZIndex=\"500\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ZIndex=\"1001\"", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SetupWizardOverlay_IsDedicatedViewIncludedByDialogsOverlay()
    {
        var dialogsOverlay = File.ReadAllText(ProjectFile("Views/MainWindowDialogsOverlay.axaml"));

        Assert.Contains("<views:SetupWizardOverlay/>", dialogsOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("Dialogs.SetupWizard.NextCommand", dialogsOverlay, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupWizardOverlay_UsesSetupWizardLayerBetweenDialogsAndToast()
    {
        var overlay = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
        var styles = File.ReadAllText(ProjectFile("Views/MainWindow.Styles.axaml"));

        Assert.Contains(
            overlay.Descendants(),
            element => HasClass(element, "setup-wizard-overlay"));
        Assert.Matches(
            """(?s)<Style Selector="Grid\.setup-wizard-overlay">.*?<Setter Property="ZIndex" Value="500"/>.*?</Style>""",
            styles);
    }

    [Fact]
    public void SecondaryOverlays_CriticalActionsExposeLocalizedAutomationNames()
    {
        Dictionary<string, Dictionary<string, string>> expectedActions = new(StringComparer.Ordinal)
        {
            ["Views/MainWindowDialogsOverlay.axaml"] = new(StringComparer.Ordinal)
            {
                ["{Binding ResourcePanel.CloseResourcePanelCommand}"] = "{Binding Shell.I18n.Close}",
                ["{Binding ResourcePanel.SaveManualResourcePanelUidCommand}"] = "{Binding Shell.I18n.ResourcePanelSaveUid}",
                ["{Binding ResourcePanel.CancelEditResourcePanelUidCommand}"] = "{Binding Shell.I18n.Cancel}",
                ["{Binding ResourcePanel.BeginEditResourcePanelUidCommand}"] = "{Binding Shell.I18n.ResourcePanelChangeUid}",
                ["{Binding ResourcePanel.RefreshResourcePanelCommand}"] = "{Binding Shell.I18n.ResourcePanelRefresh}",
                ["{Binding ResourcePanel.SaveResourcePanelCommand}"] = "{Binding Shell.I18n.ResourcePanelSave}"
            },
            ["Views/MainWindowLogViewerOverlay.axaml"] = new(StringComparer.Ordinal)
            {
                ["{Binding LogViewer.CloseCommand}"] = "{Binding Shell.I18n.Close}",
                ["{Binding LogViewer.ExportCommand}"] = "{Binding Shell.I18n.ExportLogs}"
            },
            ["Views/MainWindowToastOverlay.axaml"] = new(StringComparer.Ordinal)
            {
                ["{Binding DataContext.Toasts.DismissToastCommand, ElementName=ToastOverlayRoot}"] =
                    "{Binding DataContext.Shell.I18n.Close, ElementName=ToastOverlayRoot}"
            },
            ["Views/SetupWizardOverlay.axaml"] = new(StringComparer.Ordinal)
            {
                ["{Binding Dialogs.RequestSetupWizardExitCommand}"] = "{Binding Shell.I18n.SetupWizardSkip}",
                ["{Binding Dialogs.SetupWizard.BrowseGamePathCommand}"] = "{Binding Shell.I18n.SetupWizardBrowse}",
                ["{Binding Dialogs.SetupWizard.PreviousCommand}"] = "{Binding Shell.I18n.SetupWizardPrevious}",
                ["{Binding Dialogs.SetupWizard.NextCommand}"] = "{Binding Shell.I18n.SetupWizardNext}",
                ["{Binding Dialogs.SetupWizard.CompleteCommand}"] = "{Binding Shell.I18n.SetupWizardFinish}"
            }
        };

        foreach (var (path, expectedByCommand) in expectedActions)
        {
            var document = XDocument.Load(ProjectFile(path));
            foreach (var (command, expectedName) in expectedByCommand)
            {
                var matchingButtons = document
                    .Descendants()
                    .Where(element =>
                        element.Name.LocalName == "Button"
                        && element.Attribute("Command")?.Value == command)
                    .ToList();

                Assert.NotEmpty(matchingButtons);
                Assert.All(
                    matchingButtons,
                    button => Assert.Equal(
                        expectedName,
                        button.Attributes().SingleOrDefault(attribute =>
                            attribute.Name.LocalName == "AutomationProperties.Name")?.Value));
            }
        }
    }

    [Fact]
    public void ResourcePanel_InputsAndResourceSwitchesExposeMeaningfulAutomationNames()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowDialogsOverlay.axaml"));
        var resourcePanel = FindMotionOverlay(
            document,
            "{Binding ResourcePanel.IsResourcePanelVisible}");
        var uidInputs = resourcePanel
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "TextBox"
                && element.Attribute("Text")?.Value
                    == "{Binding ResourcePanel.ManualResourcePanelUid, Mode=TwoWay}")
            .ToList();
        var uidSource = resourcePanel
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ComboBox"
                && element.Attribute("ItemsSource")?.Value
                    == "{Binding ResourcePanel.ResourcePanelUidSourceOptions}");
        var resourceSwitch = resourcePanel
            .Descendants()
            .Single(element => element.Name.LocalName == "CheckBox");

        Assert.Equal(2, uidInputs.Count);
        Assert.All(uidInputs, input => Assert.Equal(
            "{Binding Shell.I18n.ResourcePanelUid}",
            input.Attributes().SingleOrDefault(attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name")?.Value));
        Assert.Equal(
            "{Binding Shell.I18n.ResourcePanelUidSource}",
            uidSource.Attributes().SingleOrDefault(attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name")?.Value);
        Assert.Equal(
            "{Binding DisplayName}",
            resourceSwitch.Attributes().SingleOrDefault(attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name")?.Value);
    }

    [Fact]
    public void ToastCloseButton_WhenRendered_UsesLocalizedAutomationNameAndToolTip()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowToastOverlay.axaml"));
        var closeButton = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value
                    == "{Binding DataContext.Toasts.DismissToastCommand, ElementName=ToastOverlayRoot}");
        const string expectedBinding =
            "{Binding DataContext.Shell.I18n.Close, ElementName=ToastOverlayRoot}";

        Assert.Equal(
            expectedBinding,
            closeButton.Attributes().Single(attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name").Value);
        Assert.Equal(
            expectedBinding,
            closeButton.Attributes().Single(attribute =>
                attribute.Name.LocalName == "ToolTip.Tip").Value);
    }

    [Fact]
    public void SettingRow_LongCopyWrapsInFlexibleContentColumn()
    {
        var document = XDocument.Load(ProjectFile("Controls/SettingRow.axaml"));
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
        var application = XDocument.Load(ProjectFile("App.axaml"));
        var minWidthToken = application
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key"
                && attribute.Value == "LauncherSettingRowContentMinWidth"));

        Assert.Equal("Auto,*,Auto", layout.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("1", copy.Attribute("Grid.Column")?.Value);
        Assert.True(double.Parse(minWidthToken.Value, CultureInfo.InvariantCulture) > 0);
        Assert.Equal(
            "{StaticResource LauncherSettingRowContentMinWidth}",
            copy.Attribute("MinWidth")?.Value);
        Assert.Equal(2, textBlocks.Count);
        Assert.All(textBlocks, text => Assert.Equal("Wrap", text.Attribute("TextWrapping")?.Value));
        Assert.Equal("2", action.Attribute("Grid.Column")?.Value);
    }

    [Fact]
    public void ConfirmDialog_LongContentScrollsWhileActionsRemainFixed()
    {
        var document = XDocument.Load(ProjectFile("Controls/ConfirmDialog.axaml"));
        var panel = document
            .Descendants()
            .Single(element => element.Name.LocalName == "Border" && HasClass(element, "confirm-panel"));
        var layout = panel
            .Elements()
            .Single(element => element.Name.LocalName == "Grid");
        var messageScroller = layout
            .Elements()
            .Single(element => element.Name.LocalName == "ScrollViewer");
        var actions = layout
            .Elements()
            .Single(element => element.Name.LocalName == "StackPanel" && HasClass(element, "confirm-actions"));

        Assert.Equal(
            "{StaticResource LauncherConfirmDialogMaxHeight}",
            panel.Attribute("MaxHeight")?.Value);
        var application = XDocument.Load(ProjectFile("App.axaml"));
        var maxHeightToken = application
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key"
                && attribute.Value == "LauncherConfirmDialogMaxHeight"));
        Assert.Equal("480", maxHeightToken.Value);
        Assert.Equal("Auto,*,Auto", layout.Attribute("RowDefinitions")?.Value);
        Assert.Equal("1", messageScroller.Attribute("Grid.Row")?.Value);
        Assert.Equal("2", actions.Attribute("Grid.Row")?.Value);
    }

    [Fact]
    public void CriticalDialogActions_ExposeMatchingLocalizedTooltipsAndAutomationNames()
    {
        var confirmDialog = XDocument.Load(ProjectFile("Controls/ConfirmDialog.axaml"));
        Dictionary<string, string> confirmActions = new(StringComparer.Ordinal)
        {
            ["flat-action"] = "{Binding CancelText, ElementName=Root}",
            ["primary-action"] = "{Binding ConfirmText, ElementName=Root}",
            ["danger-action"] = "{Binding ConfirmText, ElementName=Root}"
        };

        foreach (var (buttonClass, expectedBinding) in confirmActions)
        {
            var button = confirmDialog
                .Descendants()
                .Single(element =>
                    element.Name.LocalName == "Button"
                    && HasClass(element, "dialog-action")
                    && HasClass(element, buttonClass));
            Assert.Equal(expectedBinding, button.Attribute("ToolTip.Tip")?.Value);
            Assert.Equal(
                expectedBinding,
                button.Attributes()
                    .Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                    .Value);
        }

        var settingsOverlay = XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
        Dictionary<string, string> settingsActions = new(StringComparer.Ordinal)
        {
            ["{Binding WindowChrome.ShowSettingsCommand}"] = "{Binding Shell.I18n.Cancel}",
            ["{Binding Settings.SaveSettingsCommand}"] = "{Binding Shell.I18n.Save}"
        };

        foreach (var (command, expectedBinding) in settingsActions)
        {
            var button = settingsOverlay
                .Descendants()
                .Single(element =>
                    element.Name.LocalName == "Button"
                    && HasClass(element, "dialog-action")
                    && element.Attribute("Command")?.Value == command);
            Assert.Equal(expectedBinding, button.Attribute("ToolTip.Tip")?.Value);
            Assert.Equal(
                expectedBinding,
                button.Attributes()
                    .Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                    .Value);
        }
    }

    [Theory]
    [InlineData("en.json")]
    [InlineData("ja.json")]
    [InlineData("zh-Hans.json")]
    [InlineData("zh-Hant.json")]
    public void LogSeverityNames_MatchBetweenViewerFiltersAndSettings(string localeFile)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(ProjectFile($"Assets/Locales/{localeFile}")))!;
        Dictionary<string, string> matchingKeys = new(StringComparer.Ordinal)
        {
            ["logFilterVerbose"] = "logLevelVerbose",
            ["logFilterDebug"] = "logLevelDebug",
            ["logFilterInfo"] = "logLevelInformation",
            ["logFilterWarn"] = "logLevelWarning",
            ["logFilterError"] = "logLevelError",
            ["logFilterFatal"] = "logLevelFatal"
        };

        foreach (var (filterKey, settingKey) in matchingKeys)
        {
            Assert.Equal(values[settingKey], values[filterKey]);
        }
    }

    [Fact]
    public void ViewsAndControls_UserFacingTextHasNoFixedEnglishLiterals()
    {
        var violations = Directory
            .GetFiles(ProjectFile("Views"), "*.axaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(ProjectFile("Controls"), "*.axaml", SearchOption.AllDirectories))
            .SelectMany(path => FindFixedEnglishLiterals(
                XDocument.Load(path, LoadOptions.SetLineInfo),
                Path.GetRelativePath(FindProjectRoot(), path)))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("Title")]
    [InlineData("OnContent")]
    [InlineData("OffContent")]
    [InlineData("AutomationProperties.Name")]
    [InlineData("Description")]
    [InlineData("Message")]
    [InlineData("CancelText")]
    [InlineData("ConfirmText")]
    [InlineData("CloseToolTip")]
    public void FixedEnglishScanner_UserFacingAttributeLiteral_IsReported(string attributeName)
    {
        var document = XDocument.Parse(
            $"<Control xmlns=\"https://github.com/avaloniaui\" {attributeName}=\"Hardcoded English\" />",
            LoadOptions.SetLineInfo);

        var violation = Assert.Single(FindFixedEnglishLiterals(document, "fixture.axaml"));
        Assert.Contains($"{attributeName}=\"Hardcoded English\"", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void FixedEnglishScanner_BindingsAndDesignNamespacePreview_AreIgnored()
    {
        var document = XDocument.Parse(
            """
            <Panel xmlns="https://github.com/avaloniaui"
                   xmlns:d="http://schemas.microsoft.com/expression/blend/2008">
                <SettingRow Title="{Binding LocalizedTitle}"
                            Description="{Binding LocalizedDescription}" />
                <TextBlock d:Text="English design preview" />
                <d:Preview Text="English preview text"
                           Title="English preview title" />
            </Panel>
            """,
            LoadOptions.SetLineInfo);

        Assert.Empty(FindFixedEnglishLiterals(document, "fixture.axaml"));
    }

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

        var styles = XDocument.Load(ProjectFile("Views/Styles/Diagnostics.axaml"));
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
        var dialog = FindMotionOverlay(
                document,
                "{Binding ResourcePanel.IsResourcePanelVisible}")
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
        var document = XDocument.Load(ProjectFile("Views/Styles/Toast.axaml"));

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
    public void SetupWizard_UsesFixedFiveStepWorkspaceAndSettingsNavigation()
    {
        var document = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
        var dialog = document
            .Descendants()
            .Single(element => element.Name.LocalName == "Border" && HasClass(element, "overlay-dialog"));
        Assert.Equal("920", dialog.Attribute("Width")?.Value);
        Assert.Equal("560", dialog.Attribute("Height")?.Value);

        var navigation = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ListBox"
                && element.Attribute("ItemsSource")?.Value == "{Binding Dialogs.SetupWizard.Steps}");
        Assert.Contains("settings-navigation", navigation.Attribute("Classes")?.Value, StringComparison.Ordinal);
        Assert.Equal(
            "{Binding Dialogs.SetupWizard.SelectedStep, Mode=TwoWay}",
            navigation.Attribute("SelectedValue")?.Value);
        Assert.Equal("{Binding Index}", navigation.Attribute("SelectedValueBinding")?.Value);
        var template = navigation.Descendants().Single(element => element.Name.LocalName == "DataTemplate");
        Assert.Equal("setup:SetupWizardStepItem", template.Attributes().Single(
            attribute => attribute.Name.LocalName == "DataType").Value);
        Assert.Equal(2, template.Descendants().Count(element => element.Name.LocalName == "TextBlock"));
    }

    [Fact]
    public void SetupWizardNavigation_UsesSingleLineNumberAndTitle()
    {
        var document = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
        var navigation = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ListBox"
                && element.Attribute("ItemsSource")?.Value == "{Binding Dialogs.SetupWizard.Steps}");
        var template = navigation.Descendants().Single(element => element.Name.LocalName == "DataTemplate");
        var title = template
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding Title}");
        var number = template
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding DisplayNumber}");

        Assert.True(HasClass(title, "settings-navigation-item"));
        Assert.Equal("CharacterEllipsis", title.Attribute("TextTrimming")?.Value);
        Assert.Equal("{Binding DisplayNumber}", number.Attribute("Text")?.Value);
    }

    [Fact]
    public void SetupWizardNavigation_ReusesSettingsNavigationVisualStates()
    {
        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var selected = GetStyleSetters(styles, "ListBox.settings-navigation > ListBoxItem:selected");
        var disabled = GetStyleSetters(styles, "ListBox.settings-navigation > ListBoxItem:disabled");

        Assert.Equal("3,0,0,0", selected["BorderThickness"]);
        Assert.Equal("{DynamicResource LauncherAccentBrush}", selected["BorderBrush"]);
        Assert.Equal("{DynamicResource LauncherTextSecondaryBrush}", disabled["Foreground"]);
    }

    [Fact]
    public void SetupWizardNavigation_UsesSymmetricHorizontalPadding()
    {
        var styles = XDocument.Load(ProjectFile("Views/Styles/SetupWizard.axaml"));
        var navigation = GetStyleSetters(
            styles,
            "ListBox.settings-navigation.wizard-navigation");

        Assert.Equal("8,8,8,16", navigation["Padding"]);
    }

    [Fact]
    public void SetupWizardHeader_ShowsCurrentProgressBeforeTaskTitle()
    {
        var document = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
        var headerCopy = document
            .Descendants()
            .Single(element => element.Name.LocalName == "StackPanel" && HasClass(element, "dialog-heading-copy"));
        var textBlocks = headerCopy.Elements()
            .Where(element => element.Name.LocalName == "TextBlock")
            .ToList();

        Assert.Equal("{Binding Dialogs.SetupWizard.StepProgress}", textBlocks[0].Attribute("Text")?.Value);
        Assert.Equal("{Binding Dialogs.SetupWizard.StepTitle}", textBlocks[1].Attribute("Text")?.Value);
        Assert.True(HasClass(textBlocks[1], "dialog-title"));
    }

    [Fact]
    public void SetupWizard_Review_UsesSeparatedCenteredRows()
    {
        var overlay = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
        var reviewStep = overlay
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && element.Attribute("IsVisible")?.Value
                    == "{Binding Dialogs.SetupWizard.IsLastStep}");
        var reviewContent = reviewStep
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "info-strip"))
            .Elements()
            .Single(element => element.Name.LocalName == "StackPanel");
        var reviewRows = reviewContent
            .Elements()
            .Where(element => element.Name.LocalName == "Grid")
            .ToList();
        var dividers = reviewContent
            .Elements()
            .Where(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "wizard-review-divider"))
            .ToList();

        Assert.Equal(4, reviewRows.Count);
        Assert.All(reviewRows, row => Assert.True(HasClass(row, "wizard-review-row")));
        Assert.Equal(3, dividers.Count);
        Assert.Collection(
            reviewContent.Elements(),
            element => Assert.True(
                element.Name.LocalName == "Grid" && HasClass(element, "wizard-review-row")),
            element => Assert.True(
                element.Name.LocalName == "Border" && HasClass(element, "wizard-review-divider")),
            element => Assert.True(
                element.Name.LocalName == "Grid" && HasClass(element, "wizard-review-row")),
            element => Assert.True(
                element.Name.LocalName == "Border" && HasClass(element, "wizard-review-divider")),
            element => Assert.True(
                element.Name.LocalName == "Grid" && HasClass(element, "wizard-review-row")),
            element => Assert.True(
                element.Name.LocalName == "Border" && HasClass(element, "wizard-review-divider")),
            element => Assert.True(
                element.Name.LocalName == "Grid" && HasClass(element, "wizard-review-row")));
        Assert.All(
            reviewRows,
            row => Assert.All(
                row.Elements().Where(element =>
                    element.Name.LocalName is "TextBlock" or "Button"),
                element => Assert.Equal("Center", element.Attribute("VerticalAlignment")?.Value)));

        var styles = XDocument.Load(ProjectFile("Views/Styles/SetupWizard.axaml"));
        var rowStyle = GetStyleSetters(styles, "Grid.wizard-review-row");
        var dividerStyle = GetStyleSetters(styles, "Border.wizard-review-divider");

        Assert.Equal("{StaticResource LauncherControlHeightDialog}", rowStyle["MinHeight"]);
        Assert.Equal("Center", rowStyle["VerticalAlignment"]);
        Assert.Equal("1", dividerStyle["Height"]);
        Assert.Equal("{DynamicResource LauncherCardBorderBrush}", dividerStyle["Background"]);
    }

    [Fact]
    public void SetupWizardGamePath_ShowsStatusWithSemanticStateStyles()
    {
        var overlay = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
        var gamePathInput = overlay
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBox"
                && element.Attribute("Text")?.Value
                    == "{Binding Dialogs.SetupWizard.GamePath, Mode=TwoWay}")
            .Parent;
        Assert.NotNull(gamePathInput);
        var status = gamePathInput
            .ElementsAfterSelf()
            .Single(element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value
                    == "{Binding Dialogs.SetupWizard.GamePathStatusText}");

        Assert.Equal(
            "{Binding Dialogs.SetupWizard.GamePathStatusText}",
            status.Attributes()
                .Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                .Value);
        Assert.Equal(
            "{Binding Dialogs.SetupWizard.IsGamePathEmpty, Converter={x:Static BoolConverters.Not}}",
            status.Attribute("IsVisible")?.Value);
        Assert.True(HasClass(status, "caption"));
        Assert.True(HasClass(status, "wizard-game-path-status"));
        Assert.Equal(
            "{Binding Dialogs.SetupWizard.IsGamePathChecking}",
            status.Attribute("Classes.checking")?.Value);
        Assert.Equal(
            "{Binding Dialogs.SetupWizard.IsGamePathReady}",
            status.Attribute("Classes.ready")?.Value);
        Assert.Equal(
            "{Binding Dialogs.SetupWizard.IsGamePathCorruptedInstallation}",
            status.Attribute("Classes.corrupted")?.Value);
        Assert.Equal(
            "{Binding Dialogs.SetupWizard.IsGamePathInaccessible}",
            status.Attribute("Classes.inaccessible")?.Value);
        Assert.Equal("CharacterEllipsis", status.Attribute("TextTrimming")?.Value);
        Assert.Equal("1", status.Attribute("MaxLines")?.Value);

        var styles = XDocument.Load(ProjectFile("Views/Styles/SetupWizard.axaml"));
        Assert.Equal(
            "{DynamicResource LauncherAccentBrush}",
            GetStyleSetters(styles, "TextBlock.wizard-game-path-status.checking")["Foreground"]);
        Assert.Equal(
            "{DynamicResource LauncherSuccessBrush}",
            GetStyleSetters(styles, "TextBlock.wizard-game-path-status.ready")["Foreground"]);
        Assert.Equal(
            "{DynamicResource LauncherDangerBrush}",
            GetStyleSetters(styles, "TextBlock.wizard-game-path-status.corrupted")["Foreground"]);
        Assert.Equal(
            "{DynamicResource LauncherDangerBrush}",
            GetStyleSetters(styles, "TextBlock.wizard-game-path-status.inaccessible")["Foreground"]);
    }

    [Fact]
    public void SetupWizard_ChoiceSteps_UseGroupedRadioButtons()
    {
        var document = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
        var downloadSourceStep = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && element.Attribute("IsVisible")?.Value
                    == "{Binding Dialogs.SetupWizard.IsStep2}");
        var proxyStep = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && element.Attribute("IsVisible")?.Value
                    == "{Binding Dialogs.SetupWizard.IsStep3}");
        var downloadSourceRadioButtons = downloadSourceStep
            .Descendants()
            .Where(element => element.Name.LocalName == "RadioButton")
            .ToList();
        Assert.Equal(2, downloadSourceRadioButtons.Count);
        Assert.All(
            downloadSourceRadioButtons,
            button => Assert.Equal(
                "SetupWizardDownloadSource",
                button.Attribute("GroupName")?.Value));

        var proxyRadioButtons = proxyStep
            .Descendants()
            .Where(element => element.Name.LocalName == "RadioButton")
            .ToList();
        Assert.Equal(3, proxyRadioButtons.Count);
        Assert.All(
            proxyRadioButtons,
            button => Assert.Equal(
                "SetupWizardProxy",
                button.Attribute("GroupName")?.Value));

        var radioButtons = downloadSourceRadioButtons.Concat(proxyRadioButtons);
        Assert.Equal(
            new[]
            {
                "{Binding Dialogs.SetupWizard.IsPatchUrlGroupCafe, Mode=TwoWay}",
                "{Binding Dialogs.SetupWizard.IsPatchUrlGroupOfficial, Mode=TwoWay}",
                "{Binding Dialogs.SetupWizard.IsProxyAuto, Mode=TwoWay}",
                "{Binding Dialogs.SetupWizard.IsProxyDirect, Mode=TwoWay}",
                "{Binding Dialogs.SetupWizard.IsProxySystem, Mode=TwoWay}"
            },
            radioButtons.Select(button => button.Attribute("IsChecked")?.Value));
        Assert.Equal(
            new[]
            {
                "{Binding Shell.I18n.DownloadSourceCafe}",
                "{Binding Shell.I18n.DownloadSourceOfficial}",
                "{Binding Shell.I18n.ProxyAuto}",
                "{Binding Shell.I18n.ProxyDirect}",
                "{Binding Shell.I18n.ProxySystem}"
            },
            radioButtons.Select(button =>
                button.Attribute("AutomationProperties.Name")?.Value));
        Assert.DoesNotContain(
            downloadSourceStep.Descendants().Concat(proxyStep.Descendants()),
            element =>
                HasClass(element, "wizard-choice")
                || element.Attribute("Classes.active") is not null
                || element.Attribute("Command") is not null);
    }

    [Fact]
    public void MainWindow_GamePathUsesPersistedSnapshotAndImmediateCommand()
    {
        var mainWindow = File.ReadAllText(ProjectFile("Views/MainWindow.axaml"));

        Assert.Contains(
            "Text=\"{Binding Shell.PathText}\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Command=\"{Binding Settings.ChangePersistedGamePathCommand}\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Text=\"{Binding Settings.Editor.Current.GamePath}\"",
            mainWindow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPanel_UsesTransactionalSaveAndCancelActions()
    {
        var settingsOverlay = File.ReadAllText(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
        var gameSection = File.ReadAllText(ProjectFile("Views/SettingsGameSection.axaml"));
        var mainWindowCodeBehind = File.ReadAllText(ProjectFile("Views/MainWindow.axaml.cs"));

        Assert.Contains(
            "Description=\"{Binding Settings.Editor.Current.GamePath}\"",
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
            "Kind=\"ContentSave\" Width=\"{StaticResource LauncherIconMd}\" Height=\"{StaticResource LauncherIconMd}\" Foreground=",
            settingsOverlay,
            StringComparison.Ordinal);
        var mainWindowViewModel = File.ReadAllText(ProjectFile("ViewModels/MainWindowViewModel.cs"));
        Assert.Contains(
            "case ModalKind.Settings:",
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
    public void SettingsOverlay_UsesSingleRowStatusSummary()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
        var summaryGrid = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && element.Parent?.Name.LocalName == "Border"
                && HasClass(element.Parent, "settings-status-summary"));

        Assert.Equal("Auto,*,Auto,Auto", summaryGrid.Attribute("ColumnDefinitions")?.Value);
        Assert.Null(summaryGrid.Attribute("RowDefinitions"));
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Attribute("Text")?.Value == "{Binding Shell.OperationNote}");

        var titleRow = summaryGrid
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && HasClass(element, "status-title-inline"));
        Assert.Equal("Horizontal", titleRow.Attribute("Orientation")?.Value);
        Assert.Equal(
            ["{Binding Shell.CurrentViewTitle}", "{Binding Shell.VersionText}"],
            titleRow
                .Elements()
                .Where(element => element.Name.LocalName == "TextBlock")
                .Select(element => element.Attribute("Text")?.Value ?? "")
                .ToArray());
    }

    [Fact]
    public void SettingsStatusDetails_UseChipHeightWithoutVerticalPadding()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var statusDetail = GetStyleSetters(document, "Border.status-detail");

        Assert.Equal(
            "{StaticResource LauncherChipHeight}",
            statusDetail["Height"]);
        Assert.Equal("12,0", statusDetail["Padding"]);
    }

    [Fact]
    public void SettingsNavigation_SelectedItemUsesSemiboldText()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var selected = GetStyleSetters(
            document,
            "ListBox.settings-navigation > ListBoxItem:selected");

        Assert.Equal(
            "{StaticResource LauncherFontWeightStrong}",
            selected["FontWeight"]);
        Assert.Equal(
            "{DynamicResource LauncherFlatPressedBrush}",
            selected["Background"]);
    }

    [Fact]
    public void StatusSummary_TitleAndVersionUseMatchingTypography()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var title = GetStyleSetters(document, "TextBlock.status-summary-title");
        var version = GetStyleSetters(document, "TextBlock.status-summary-version");

        Assert.Equal(title["FontSize"], version["FontSize"]);
        Assert.Equal("Center", version["VerticalAlignment"]);
    }

    [Fact]
    public void DialogClose_FocusUsesSubtleAccentTreatment()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var focus = GetStyleSetters(document, "Button.dialog-close:focus-visible");

        Assert.Equal("{DynamicResource LauncherAccentSoftBrush}", focus["Background"]);
        Assert.Equal("{DynamicResource LauncherAccentBrush}", focus["BorderBrush"]);
        Assert.Equal("1", focus["BorderThickness"]);
    }

    [Fact]
    public void ConfirmDialogs_DangerousActionsUseDangerHeadingIcons()
    {
        var control = XDocument.Load(ProjectFile("Controls/ConfirmDialog.axaml"));
        var icon = control
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "MaterialIcon"
                && HasClass(element, "confirm-heading-icon"));
        Assert.Equal(
            "{Binding IsDangerIcon, ElementName=Root}",
            icon.Attribute("Classes.danger")?.Value);

        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        Assert.Equal(
            "{DynamicResource LauncherDangerBrush}",
            GetStyleSetters(
                styles,
                "materialIcons|MaterialIcon.confirm-heading-icon.danger")["Foreground"]);

        var dialogs = XDocument.Load(ProjectFile("Views/MainWindowDialogsOverlay.axaml"));
        var dangerousConfirmDialogs = dialogs
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "ConfirmDialog"
                && element.Attribute("IsDangerConfirm")?.Value == "True")
            .ToList();
        Assert.NotEmpty(dangerousConfirmDialogs);
        Assert.All(
            dangerousConfirmDialogs,
            dialog => Assert.Equal("True", dialog.Attribute("IsDangerIcon")?.Value));
    }

    [Fact]
    public void LogViewer_FilterControlsShareHeightAndSingleBottomGap()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowLogViewerOverlay.axaml"));
        var filterButtons = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Button"
                && HasClass(element, "log-filter"))
            .ToList();
        Assert.Equal(7, filterButtons.Count);

        var search = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBox"
                && HasClass(element, "log-search"));
        Assert.Equal(
            "{StaticResource LauncherControlHeightSetting}",
            search.Attribute("Height")?.Value);

        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        Assert.Equal(
            "{DynamicResource LauncherControlHeightSetting}",
            GetStyleSetters(styles, "Button.news-tab.log-filter")["Height"]);
        Assert.Equal(
            "16,12,16,0",
            GetStyleSetters(styles, "StackPanel.log-filter-bar")["Margin"]);
    }

    [Fact]
    public void ResourcePanel_StatusStripHasVisibleSurfaceAndBorder()
    {
        var dialogs = XDocument.Load(ProjectFile("Views/MainWindowDialogsOverlay.axaml"));
        var statusStrip = dialogs
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "resource-panel-status"));
        Assert.True(HasClass(statusStrip, "info-strip"));

        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var statusStyle = GetStyleSetters(styles, "Border.info-strip.resource-panel-status");
        Assert.Equal(
            "{DynamicResource LauncherContentRowBrush}",
            statusStyle["Background"]);
        Assert.Equal(
            "{DynamicResource LauncherAccentBorderBrush}",
            statusStyle["BorderBrush"]);
        Assert.Equal("1", statusStyle["BorderThickness"]);
    }

    [Fact]
    public void SettingsGroups_UseAvailableContentWidth()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
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
            "TextBox",
            "ToggleSwitch"
        };

        foreach (var sectionPath in sectionPaths)
        {
            var document = XDocument.Load(ProjectFile(sectionPath));
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
                            attribute.Name.LocalName == "AutomationProperties.Name")
                        ?.Value;

                    Assert.False(
                        string.IsNullOrWhiteSpace(automationName),
                        $"{sectionPath}: {control.Name.LocalName} is missing AutomationProperties.Name.");
                    Assert.Contains("Shell.I18n.", automationName, StringComparison.Ordinal);
                });
        }
    }

    [Fact]
    public void AdvancedSettings_LogActionsBelongToDedicatedSettingRow()
    {
        var document = XDocument.Load(ProjectFile("Views/SettingsAdvancedSection.axaml"));
        var group = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && HasClass(element, "settings-group"));
        var rows = group
            .Elements()
            .Where(element => element.Name.LocalName == "SettingRow")
            .ToList();

        Assert.Equal(2, rows.Count);
        var logFilesRow = rows[1];
        Assert.Equal(
            "{Binding Shell.I18n.LogFiles}",
            logFilesRow.Attribute("Title")?.Value);
        Assert.Equal(
            "{Binding Shell.I18n.LogFilesDescription}",
            logFilesRow.Attribute("Description")?.Value);

        var action = logFilesRow
            .Elements()
            .Single(element => element.Name.LocalName == "SettingRow.Action");
        var actionPanel = action
            .Elements()
            .Single(element => element.Name.LocalName == "WrapPanel");
        Assert.Equal(
            "{StaticResource LauncherSpacingSm}",
            actionPanel.Attribute("ItemSpacing")?.Value);
        Assert.Equal(
            "{StaticResource LauncherSpacingSm}",
            actionPanel.Attribute("LineSpacing")?.Value);
        Assert.Equal(
            "{StaticResource LauncherSettingRowActionMaxWidth}",
            actionPanel.Attribute("MaxWidth")?.Value);

        var app = XDocument.Load(ProjectFile("App.axaml"));
        var actionMaxWidth = app
            .Descendants()
            .Single(element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "LauncherSettingRowActionMaxWidth"));
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
                "{Binding LogViewer.ExportCommand}",
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
        var aboutText = File.ReadAllText(ProjectFile("Views/SettingsAboutSection.axaml"));
        var advancedText = File.ReadAllText(ProjectFile("Views/SettingsAdvancedSection.axaml"));
        var overlay = File.ReadAllText(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));

        AssertOrdered(
            aboutText,
            "Shell.LauncherVersionText",
            "Shell.BuildTimeText",
            "Shell.CommitShaText",
            "Shell.BuildConfigText",
            "Shell.FrameworkVersionText",
            "Shell.AvaloniaVersionText",
            "Shell.PlatformText");
        AssertOrdered(
            aboutText,
            "Settings.CheckForUpdatesCommand",
            "WindowChrome.OpenOfficialSiteCommand",
            "WindowChrome.OpenHelpDocsCommand",
            "WindowChrome.OpenGitHubRepositoryCommand");
        AssertOrdered(
            advancedText,
            "LogViewer.OpenCommand",
            "LogViewer.ExportCommand",
            "WindowChrome.OpenDataDirectoryCommand");

        Assert.Contains("Shell.I18n.AboutActionsGeneral", aboutText, StringComparison.Ordinal);
        Assert.DoesNotContain("LogViewer.OpenCommand", aboutText, StringComparison.Ordinal);
        Assert.DoesNotContain("LogViewer.ExportCommand", aboutText, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowChrome.OpenDataDirectoryCommand", aboutText, StringComparison.Ordinal);
        Assert.Contains("Shell.I18n.SettingsGroupDiagnostics", advancedText, StringComparison.Ordinal);

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

    [Fact]
    public void ToastCards_DoNotUseOverlappingBoxShadows()
    {
        var document = XDocument.Load(ProjectFile("Views/Styles/Toast.axaml"));
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
        var overlayDocument = XDocument.Load(ProjectFile("Views/MainWindowToastOverlay.axaml"));
        var toastCard = overlayDocument
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "toast-card"));
        Assert.Equal("{Binding IsExiting}", toastCard.Attribute("Classes.motion-exit")?.Value);

        var document = XDocument.Load(ProjectFile("Views/Styles/Toast.axaml"));
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
        var enterMotionStyle = Assert.Single(
            toastStyles,
            style => style.Attribute("Selector")?.Value
                == "Border.toast-card.motion-enabled:not(.motion-exit)");
        var exitMotionStyle = Assert.Single(
            toastStyles,
            style => style.Attribute("Selector")?.Value
                == "Border.toast-card.motion-enabled.motion-exit");

        Assert.DoesNotContain(
            baseStyle.Elements(),
            element => element.Name.LocalName == "Style.Animations");
        Assert.Contains(
            enterMotionStyle.Elements(),
            element => element.Name.LocalName == "Style.Animations");
        Assert.Contains(
            exitMotionStyle.Elements(),
            element => element.Name.LocalName == "Style.Animations");

        AssertMotionAnimation(
            document,
            "Border.toast-card.motion-enabled:not(.motion-exit)",
            "0:0:0.22",
            expectedStartOffset: "4");
        AssertExitMotionAnimation(
            document,
            "Border.toast-card.motion-enabled.motion-exit",
            expectedEndOffset: "4");
    }

    [Fact]
    public void CoreMotionStyles_DefineExactConditionalAnimations()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));

        AssertMotionAnimation(
            document,
            "Grid.motion-overlay.motion-enabled.motion-enter",
            "0:0:0.16",
            expectedStartOffset: null);
        AssertMotionAnimation(
            document,
            "Grid.motion-overlay.motion-enabled.motion-enter > Border.motion-surface",
            "0:0:0.22",
            expectedStartOffset: "8");
        AssertMotionAnimation(
            document,
            ":is(UserControl).motion-content.motion-enabled.motion-enter",
            "0:0:0.18",
            expectedStartOffset: "6");
        AssertMotionAnimation(
            document,
            "StackPanel.motion-content.motion-enabled.motion-enter",
            "0:0:0.18",
            expectedStartOffset: "6");
        AssertMotionAnimation(
            document,
            "Border.motion-bottom.motion-enabled.motion-enter",
            "0:0:0.20",
            expectedStartOffset: "10");
        AssertExitMotionAnimation(
            document,
            "Grid.motion-overlay.motion-enabled.motion-exit",
            expectedEndOffset: null);
        AssertExitMotionAnimation(
            document,
            "Grid.motion-overlay.motion-enabled.motion-exit > Border.motion-surface",
            expectedEndOffset: "6");

        foreach (var selector in new[]
                 {
                     "Grid.motion-overlay",
                     "Border.motion-surface",
                     ":is(UserControl).motion-content",
                     "StackPanel.motion-content",
                     "Border.motion-bottom",
                     "ListBox.settings-navigation > ListBoxItem:selected"
                 })
        {
            var style = document
                .Descendants()
                .Single(element =>
                    element.Name.LocalName == "Style"
                    && element.Attribute("Selector")?.Value == selector);
            Assert.DoesNotContain(
                style.Elements(),
                element => element.Name.LocalName == "Style.Animations");
        }
    }

    [Fact]
    public void CoreMotionOverlays_UseMotionVisibilityWithoutDirectVisibilityBindings()
    {
        var overlayFiles = new[]
        {
            "Views/MainWindowSettingsOverlay.axaml",
            "Views/MainWindowLogViewerOverlay.axaml",
            "Views/MainWindowDialogsOverlay.axaml",
            "Views/SetupWizardOverlay.axaml"
        };
        var overlays = new List<(XElement Element, XNamespace ControlsNamespace)>();
        foreach (var path in overlayFiles)
        {
            var document = XDocument.Load(ProjectFile(path));
            var controlsNamespace = document.Root?.GetNamespaceOfPrefix("controls");
            Assert.NotNull(controlsNamespace);
            overlays.AddRange(
                document
                    .Descendants()
                    .Where(element => HasClass(element, "motion-overlay"))
                    .Select(element => (element, controlsNamespace)));
        }

        Assert.Equal(7, overlays.Count);
        Assert.All(overlays, overlay =>
        {
            var element = overlay.Element;
            Assert.Equal(
                "{Binding IsMotionEnabled}",
                element.Attribute("Classes.motion-enabled")?.Value);
            Assert.Equal(
                "{Binding IsMotionEnabled}",
                element.Attribute(
                    overlay.ControlsNamespace + "MotionVisibility.IsMotionEnabled")?.Value);
            Assert.StartsWith(
                "{Binding ",
                element.Attribute(overlay.ControlsNamespace + "MotionVisibility.IsOpen")?.Value);
            Assert.Null(element.Attribute("IsVisible"));
            Assert.Null(element.Attribute("Classes.motion-enter"));
            var surface = element.Elements().First();
            Assert.True(HasClass(surface, "motion-surface"));
            AssertHasLocalTranslateTransform(surface);
        });

        var settings = XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
        var wizard = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
        var contentTargets = settings
            .Descendants()
            .Concat(wizard.Descendants())
            .Where(element => HasClass(element, "motion-content"))
            .ToList();

        Assert.Equal(11, contentTargets.Count);
        Assert.All(contentTargets, element =>
        {
            Assert.Equal(
                "{Binding IsMotionEnabled}",
                element.Attribute("Classes.motion-enabled")?.Value);
            Assert.Equal(
                element.Attribute("IsVisible")?.Value,
                element.Attribute("Classes.motion-enter")?.Value);
            AssertHasLocalTranslateTransform(element);
        });

        var mainWindow = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var bottomTargets = mainWindow
            .Descendants()
            .Where(element => HasClass(element, "motion-bottom"))
            .ToList();

        Assert.Equal(2, bottomTargets.Count);
        Assert.All(bottomTargets, element =>
        {
            Assert.Equal(
                "{Binding IsMotionEnabled}",
                element.Attribute("Classes.motion-enabled")?.Value);
            Assert.Equal(
                element.Attribute("IsVisible")?.Value,
                element.Attribute("Classes.motion-enter")?.Value);
            AssertHasLocalTranslateTransform(element);
        });
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
            .Select(path => Path.GetRelativePath(FindProjectRoot(), path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(StyleFiles.Order(StringComparer.Ordinal), discoveredFiles);
        Assert.All(StyleFiles, path => XDocument.Load(ProjectFile(path)));
    }

    [Fact]
    public void OverlayStyles_DefineSettingsDialogAndSetupWizardLayerOrder()
    {
        var styles = File.ReadAllText(ProjectFile("Views/MainWindow.Styles.axaml"));

        Assert.Matches(
            """(?s)<Style Selector="Grid\.settings-overlay">.*?<Setter Property="ZIndex" Value="100"/>.*?</Style>""",
            styles);
        Assert.Matches(
            """(?s)<Style Selector="Grid\.dialog-overlay">.*?<Setter Property="ZIndex" Value="200"/>.*?</Style>""",
            styles);
        Assert.Matches(
            """(?s)<Style Selector="Grid\.setup-wizard-overlay">.*?<Setter Property="ZIndex" Value="500"/>.*?</Style>""",
            styles);
    }

    [Fact]
    public void OverlayStyles_TrapAndRestoreKeyboardFocus()
    {
        var styles = File.ReadAllText(ProjectFile("Views/MainWindow.Styles.axaml"));
        var behavior = File.ReadAllText(ProjectFile("Views/OverlayFocusBehavior.cs"));

        Assert.Equal(
            3,
            Regex.Count(
                styles,
                "KeyboardNavigation.TabNavigation\" Value=\"Cycle",
                RegexOptions.CultureInvariant));
        Assert.Equal(
            3,
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
        Assert.Contains("Shell.CurrentViewTitle", markup, StringComparison.Ordinal);
        Assert.Contains("Shell.VersionText", markup, StringComparison.Ordinal);
        Assert.Contains("Shell.NetworkStatusValueText", markup, StringComparison.Ordinal);
        Assert.Contains("Shell.DiskSpaceText", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Shell.OperationNote", markup, StringComparison.Ordinal);
        Assert.Contains("Kind=\"{Binding Shell.StatusIconKind}\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Shell.ExecutableText", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Shell.StatusText", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Shell.ProductName", markup, StringComparison.Ordinal);
        Assert.Equal("Auto,*,Auto,Auto", detailsGrid.Attribute("ColumnDefinitions")?.Value);
        Assert.Null(detailsGrid.Attribute("RowDefinitions"));
        Assert.Equal(2, detailCards.Count);
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

    [Fact]
    public void SystemTrayMenu_DoesNotLoadItemIcons()
    {
        var platform = File.ReadAllText(ProjectFile("Services/SystemTrayPlatform.cs"));

        Assert.DoesNotContain("LoadMenuIcon", platform, StringComparison.Ordinal);
        Assert.DoesNotContain("Icon = menuIcon", platform, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Assets/notification-8be8201c.png",
            platform,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BundledBackground_UsesNormalizedResourceName()
    {
        const string resourceName = "Assets/launcher-background.png";
        var backgroundViewModel =
            File.ReadAllText(ProjectFile("ViewModels/BackgroundViewModel.cs"));

        Assert.True(File.Exists(ProjectFile(resourceName)));
        Assert.Contains(resourceName, backgroundViewModel, StringComparison.Ordinal);
        Assert.False(File.Exists(ProjectFile("Assets/bg-7b36e4e0.png")));
    }

    private static bool HasClass(XElement element, string className) =>
        element.Attribute("Classes")?.Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(className, StringComparer.Ordinal) == true;

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

    private static void AssertMotionAnimation(
        XDocument document,
        string selector,
        string expectedDuration,
        string? expectedStartOffset)
    {
        var style = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == selector);
        var animation = style
            .Descendants()
            .Single(element => element.Name.LocalName == "Animation");
        Assert.Equal(expectedDuration, animation.Attribute("Duration")?.Value);
        Assert.Equal("Forward", animation.Attribute("FillMode")?.Value);
        Assert.Equal("QuadraticEaseOut", animation.Attribute("Easing")?.Value);

        var keyFrames = animation
            .Elements()
            .Where(element => element.Name.LocalName == "KeyFrame")
            .ToDictionary(
                element => element.Attribute("Cue")?.Value ?? "",
                element => element,
                StringComparer.Ordinal);
        Assert.Equal(2, keyFrames.Count);
        Assert.Equal(
            "0",
            keyFrames["0%"]
                .Elements()
                .Single(element => element.Attribute("Property")?.Value == "Opacity")
                .Attribute("Value")?.Value);
        Assert.Equal(
            "1",
            keyFrames["100%"]
                .Elements()
                .Single(element => element.Attribute("Property")?.Value == "Opacity")
                .Attribute("Value")?.Value);

        if (expectedStartOffset is null)
        {
            Assert.DoesNotContain(
                keyFrames.SelectMany(pair => pair.Value.Elements()),
                element => element.Attribute("Property")?.Value == "TranslateTransform.Y");
            return;
        }

        Assert.Equal(
            expectedStartOffset,
            keyFrames["0%"]
                .Elements()
                .Single(element => element.Attribute("Property")?.Value == "TranslateTransform.Y")
                .Attribute("Value")?.Value);
        Assert.Equal(
            "0",
            keyFrames["100%"]
                .Elements()
                .Single(element => element.Attribute("Property")?.Value == "TranslateTransform.Y")
                .Attribute("Value")?.Value);
    }

    private static void AssertExitMotionAnimation(
        XDocument document,
        string selector,
        string? expectedEndOffset)
    {
        var style = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == selector);
        var animation = style
            .Descendants()
            .Single(element => element.Name.LocalName == "Animation");
        Assert.Equal("0:0:0.15", animation.Attribute("Duration")?.Value);
        Assert.Equal("Forward", animation.Attribute("FillMode")?.Value);
        Assert.Equal("QuadraticEaseIn", animation.Attribute("Easing")?.Value);

        var keyFrames = animation
            .Elements()
            .Where(element => element.Name.LocalName == "KeyFrame")
            .ToDictionary(
                element => element.Attribute("Cue")?.Value ?? "",
                element => element,
                StringComparer.Ordinal);
        Assert.Equal(2, keyFrames.Count);
        Assert.Equal(
            "1",
            keyFrames["0%"]
                .Elements()
                .Single(element => element.Attribute("Property")?.Value == "Opacity")
                .Attribute("Value")?.Value);
        Assert.Equal(
            "0",
            keyFrames["100%"]
                .Elements()
                .Single(element => element.Attribute("Property")?.Value == "Opacity")
                .Attribute("Value")?.Value);

        if (expectedEndOffset is null)
        {
            Assert.DoesNotContain(
                keyFrames.SelectMany(pair => pair.Value.Elements()),
                element => element.Attribute("Property")?.Value == "TranslateTransform.Y");
            return;
        }

        Assert.Equal(
            "0",
            keyFrames["0%"]
                .Elements()
                .Single(element => element.Attribute("Property")?.Value == "TranslateTransform.Y")
                .Attribute("Value")?.Value);
        Assert.Equal(
            expectedEndOffset,
            keyFrames["100%"]
                .Elements()
                .Single(element => element.Attribute("Property")?.Value == "TranslateTransform.Y")
                .Attribute("Value")?.Value);
    }

    private static void AssertSettingRowIcon(
        XDocument document,
        string titleBinding,
        string expectedIconKind)
    {
        var settingRow = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "SettingRow"
                && element.Attribute("Title")?.Value == titleBinding);

        Assert.Equal(expectedIconKind, settingRow.Attribute("IconKind")?.Value);
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
