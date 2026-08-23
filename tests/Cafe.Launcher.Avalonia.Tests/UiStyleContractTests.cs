using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed partial class UiStyleContractTests
{
    [Fact]
    public void LauncherIcons_UserFacingActionsUseApprovedSemanticMappings()
    {
        var mainWindow = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var generalSettings = XDocument.Load(ProjectFile("Views/SettingsGeneralSection.axaml"));
        var downloadNetworkSettings = XDocument.Load(ProjectFile("Views/SettingsDownloadNetworkSection.axaml"));
        var dialogs = XDocument.Load(ProjectFile("Views/MainWindowDialogsOverlay.axaml"));
        var settingsOverlay = XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));

        var detectButton = mainWindow
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding Settings.SelectInstalledGameCommand}")
            .First();
        Assert.Equal(
            "FolderSearchOutline",
            detectButton.Descendants().Single(element => element.Name.LocalName == "MaterialIcon").Attribute("Kind")?.Value);

        Assert.DoesNotContain(generalSettings.Descendants(), element =>
            element.Name.LocalName is "SettingRow" or "SettingComboRow"
            && element.Attribute("IconKind") is not null);
        Assert.DoesNotContain(downloadNetworkSettings.Descendants(), element =>
            element.Name.LocalName is "SettingRow" or "SettingComboRow"
            && element.Attribute("IconKind") is not null);

        var resourcePanelButton = mainWindow
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding ResourcePanel.OpenResourcePanelCommand}");
        Assert.Equal(
            "Web",
            resourcePanelButton.Descendants().Single(element => element.Name.LocalName == "MaterialIcon").Attribute("Kind")?.Value);

        var settingsCloseButton = settingsOverlay.Descendants().Single(element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Command")?.Value == "{Binding WindowChrome.ShowSettingsCommand}");
        Assert.Equal(
            "Close",
            settingsCloseButton.Descendants().Single(element => element.Name.LocalName == "MaterialIcon").Attribute("Kind")?.Value);
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

        // Only the progress panel keeps the operation-layout status/action contract.
        var panelLayouts = operationLayouts
            .Where(l => !HasClass(l.Parent!, "operation-status"))
            .ToArray();
        Assert.Single(panelLayouts);
        Assert.All(panelLayouts, layout =>
        {
            Assert.Equal("*,Auto", layout.Attribute("ColumnDefinitions")?.Value);
            Assert.True(
                HasClass(layout.Parent!, "bottom-panel")
                || layout.Parent?.Name.LocalName == "Panel");

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
                "{StaticResource Cafe.Icon.Large}",
                statusColumns[0].Attribute("MinWidth")?.Value);
            Assert.Equal("*", statusColumns[1].Attribute("Width")?.Value);
            Assert.Equal(
                "{StaticResource Cafe.Space.3}",
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
    public void MainWindow_InstallPanel_UsesCompactPathAndActionLayout()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var installLayout = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && element.Elements().Any(child => HasClass(child, "operation-actions"))
                && element.Descendants().Any(descendant => HasClass(descendant, "install-path-row")));
        var actions = installLayout.Elements().Single(element => HasClass(element, "operation-actions"));

        var refreshButton = actions
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding RefreshCommand}");
        var installButton = actions
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding Operations.InstallOrUpdateCommand}");
        var pathField = installLayout
            .Elements()
            .SelectMany(element => element.Descendants())
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "path-field"));
        var pathRow = pathField
            .Elements()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "install-path-row"));
        var pathLayout = pathRow;
        var changePathButton = pathLayout
            .Elements()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding Settings.ChangePersistedGamePathCommand}");

        Assert.Equal("*,Auto", installLayout.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("*,Auto", installLayout.Attribute("RowDefinitions")?.Value);
        Assert.Equal("{StaticResource Cafe.Space.5}", installLayout.Attribute("ColumnSpacing")?.Value);
        Assert.Equal("Auto,*,Auto,Auto", pathLayout.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("{StaticResource Cafe.Space.2}", pathLayout.Attribute("ColumnSpacing")?.Value);
        Assert.Equal("{Binding Shell.I18n[changePath]}", changePathButton.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("{Binding Shell.I18n[refresh]}", refreshButton.Attribute("AutomationProperties.Name")?.Value);
        Assert.True(HasClass(installButton, "primary-action"));
        Assert.DoesNotContain(
            pathField.Descendants(),
            element => element.Name.LocalName == "Border");
        Assert.DoesNotContain(
            installLayout.DescendantsAndSelf().Attributes(),
            attribute => attribute.Name.LocalName == "Margin"
                && !attribute.Value.StartsWith("{StaticResource Launcher", StringComparison.Ordinal));
    }

    [Fact]
    public void MainWindow_StatusPanel_DoesNotExposeDetailedLayout()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));

        Assert.DoesNotContain(
            document.Descendants().SelectMany(element => element.Attributes()),
            attribute => attribute.Value.Contains("IsStatusDetailExpanded", StringComparison.Ordinal));
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding Shell.I18n[launchCheckDescription]}");
        Assert.DoesNotContain(
            document.Descendants(),
            element => HasClass(element, "operation-status-title")
                && element.Attribute("Text")?.Value == "{Binding Shell.StatusText}");
    }

    [Fact]
    public void MainWindow_OperationButtons_ExposeLocalizedNamesAndActionPriority()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        Dictionary<string, (string Name, bool IsPrimary)> expectedButtons = new(StringComparer.Ordinal)
        {
            ["{Binding RefreshCommand}"] = ("{Binding Shell.I18n[refresh]}", false),
            ["{Binding Operations.InstallOrUpdateCommand}"] = ("{Binding Operations.InstallButtonText}", true),
            ["{Binding Settings.ChangePersistedGamePathCommand}"] = ("{Binding Shell.I18n[changePath]}", false),
            ["{Binding Settings.SelectInstalledGameCommand}"] = ("{Binding Shell.I18n[selectInstalledGame]}", false),
            ["{Binding WindowChrome.OpenOfficialSiteCommand}"] = ("{Binding Shell.I18n[officialSite]}", false),
            ["{Binding Operations.StartGameCommand}"] = ("{Binding Shell.I18n[startGame]}", true),
            ["{Binding Operations.PauseResumeCommand}"] = ("{Binding Operations.PauseResumeText}", false),
            ["{Binding Operations.StopOperationCommand}"] = ("{Binding Shell.I18n[stop]}", false)
        };

        foreach (var (command, expected) in expectedButtons)
        {
            var button = document
                .Descendants()
                .First(element =>
                    element.Name.LocalName == "Button"
                    && element.Attribute("Command")?.Value == command);

            Assert.Equal(expected.IsPrimary, HasClass(button, "primary-action"));
            Assert.Equal(
                expected.Name,
                button.Attributes()
                    .Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                    .Value);
            Assert.NotNull(button.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == "ToolTip.Tip"));
        }
    }

    [Fact]
    public void MainWindow_OperationButtons_RestoreHistoricalPressedStateStyles()
    {
        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var officialSitePressed = styles
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Button.flat-action.elevated:pressed");
        var primaryPressed = styles
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Button.primary-action:pressed");

        Assert.Equal(
            "{DynamicResource Cafe.Color.Surface.Info}",
            officialSitePressed
                .Elements()
                .Single(element => element.Name.LocalName == "Setter"
                    && element.Attribute("Property")?.Value == "Background")
                .Attribute("Value")?.Value);
        Assert.Equal(
            "{DynamicResource Cafe.Color.Accent.Pressed}",
            primaryPressed
                .Elements()
                .Single(element => element.Name.LocalName == "Setter"
                    && element.Attribute("Property")?.Value == "Background")
                .Attribute("Value")?.Value);
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
        var statusText = controlPanel
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding Shell.LaunchCheckText}");

        Assert.NotNull(statusText);
        Assert.DoesNotContain(
            controlPanel.Descendants(),
            element => element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding Shell.I18n[launchCheckDescription]}");
    }

    [Fact]
    public void MainWindow_ControlPanel_RightAlignsStatusSummaryAndCentersVertically()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var controlPanel = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "control-panel"));
        var statusSummary = controlPanel
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && element.Attribute("Grid.Row")?.Value == "1"
                && element.Attribute("Grid.ColumnSpan")?.Value == "2"
                && element.Descendants().Any(descendant =>
                    descendant.Name.LocalName == "TextBlock"
                    && descendant.Attribute("Text")?.Value == "{Binding Shell.LaunchCheckText}"));

        Assert.Equal("Right", statusSummary.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Center", statusSummary.Attribute("VerticalAlignment")?.Value);
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
    public void MainWindow_DataTemplateRootBindings_UseTypedNamedElementSyntax()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var bindingValues = document
            .Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .Where(value => value.Contains("Root", StringComparison.Ordinal))
            .ToArray();

        Assert.DoesNotContain(
            bindingValues,
            value => value.Contains("ElementName=Root", StringComparison.Ordinal));
        Assert.Equal(
            7,
            bindingValues.Count(value =>
                value.Contains(
                    "#Root.((vm:MainWindowViewModel)DataContext).",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public void SharedLauncherStyles_AreApplicationScopedForSplitViews()
    {
        const string sharedStylesSource =
            "avares://Cafe.Launcher.Avalonia/Views/MainWindow.Styles.axaml";
        var application = XDocument.Load(ProjectFile("App.axaml"));
        var mainWindow = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));

        Assert.Contains(
            application.Descendants(),
            element =>
                element.Name.LocalName == "StyleInclude"
                && element.Attribute("Source")?.Value == sharedStylesSource);
        Assert.DoesNotContain(
            mainWindow.Descendants(),
            element =>
                element.Name.LocalName == "StyleInclude"
                && element.Attribute("Source")?.Value == sharedStylesSource);
    }

    [Fact]
    public void LocalizedTextCatalog_DebugBindings_UseResourceKeys()
    {
        var overlay = XDocument.Load(ProjectFile("Views/MainWindowDebugOverlay.axaml"));
        var source = File.ReadAllText(ProjectFile("Services/LocalizationService.cs"));
        var debugProperties = overlay
            .Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .SelectMany(value => Regex.Matches(value, @"Shell\.I18n\[(debug[A-Za-z0-9_]+)\]"))
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(debugProperties);
        foreach (var key in debugProperties)
        {
            Assert.Contains($"this[string key] => localizer.T(key)", source, StringComparison.Ordinal);
            Assert.StartsWith("debug", key, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LauncherSettings_JsonProperties_AreExplicitNonGeneratedDeclarations()
    {
        var source = File.ReadAllText(ProjectFile("Models/LauncherSettings.cs"));

        Assert.DoesNotContain("[property:", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[ObservableProperty]", source, StringComparison.Ordinal);
        Assert.Contains("public sealed class LauncherSettings", source, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class LauncherSettings", source, StringComparison.Ordinal);
        Assert.Contains("[JsonPropertyName(\"gamePath\")]", source, StringComparison.Ordinal);
        Assert.Contains("public string GamePath", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DebugResetDialogMembers_AreExplicitlyDeclared()
    {
        var source = File.ReadAllText(ProjectFile("ViewModels/DialogsViewModel.cs"));

        Assert.Contains("public bool IsDebugResetConfirmationVisible", source, StringComparison.Ordinal);
        Assert.Contains("public IRelayCommand CancelDebugResetCommand", source, StringComparison.Ordinal);
        Assert.Contains("public IAsyncRelayCommand ConfirmDebugResetCommand", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SplitViewDataTemplates_UseTypedRootBindings()
    {
        string[] paths =
        [
            "Views/MainWindowDialogsOverlay.axaml",
            "Views/MainWindowToastOverlay.axaml",
            "Views/SettingsAppearanceSection.axaml"
        ];

        foreach (var path in paths)
        {
            var markup = File.ReadAllText(ProjectFile(path));
            Assert.DoesNotContain("DataContext.", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("$parent[UserControl].DataContext.", markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MainWindow_SemanticContractClasses_HaveDeclaredStyleSelectors()
    {
        var selectors = new[]
        {
            "Views/MainWindow.Styles.axaml",
            "Views/Styles/RemoteContent.axaml"
        }
            .Select(path => XDocument.Load(ProjectFile(path)))
            .SelectMany(document => document.Descendants())
            .Where(element => element.Name.LocalName == "Style")
            .Select(element => element.Attribute("Selector")?.Value ?? "")
            .ToArray();
        string[] semanticClasses =
        [
            "install-path-row",
            "news-row",
            "news-viewport",
            "operation-actions",
            "operation-layout",
            "operation-status"
        ];

        foreach (var semanticClass in semanticClasses)
        {
            Assert.Contains(
                selectors,
                selector => Regex.IsMatch(
                    selector,
                    $@"\.{Regex.Escape(semanticClass)}(?:[^A-Za-z0-9_-]|$)",
                    RegexOptions.CultureInvariant));
        }
    }

    [Fact]
    public void MainWindow_ButtonsDoNotUseDeprecatedRoleAndPositionClasses()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        string[] deprecatedClasses =
        [
            "bottom-action",
            "dock-icon-action",
            "launcher-control",
            "path-dock-action",
            "path-operation",
            "primary-operation",
            "secondary-operation",
            "start"
        ];

        Assert.All(
            document.Descendants().Where(element => element.Name.LocalName == "Button"),
            button =>
            {
                var classes = (button.Attribute("Classes")?.Value ?? "")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Assert.DoesNotContain(
                    classes,
                    className => deprecatedClasses.Contains(className, StringComparer.Ordinal));
            });
    }

    [Fact]
    public void MainWindow_MultiRowGridChildren_DeclareTheirFirstRowExplicitly()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var expectedBindings = new[]
        {
            "{Binding RefreshCommand}",
            "{Binding Operations.ProgressTitle}"
        };

        foreach (var binding in expectedBindings)
        {
            var directGridChildren = document
                .Descendants()
                .Where(element => element.Attributes().Any(attribute => attribute.Value == binding))
                .Select(element => element
                    .Ancestors()
                    .First(ancestor =>
                        ancestor.Parent?.Name.LocalName == "Grid"
                        && ancestor.Parent.Attribute("RowDefinitions") is not null))
                .Distinct()
                .ToArray();

            Assert.NotEmpty(directGridChildren);
            Assert.All(
                directGridChildren,
                directGridChild => Assert.Equal("0", directGridChild.Attribute("Grid.Row")?.Value));
        }
    }

    [Fact]
    public void MainWindow_BannerAndNews_UseSeparateCardSurfacesWithoutNewsHeading()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var remoteSurface = document
            .Descendants()
            .Single(element => element.Name.LocalName == "Border" && HasClass(element, "remote-surface"));
        var bannerCard = document
            .Descendants()
            .Single(element => element.Name.LocalName == "Border" && HasClass(element, "banner-shell"));
        var newsCard = document
            .Descendants()
            .Single(element => element.Name.LocalName == "Border" && HasClass(element, "news-card"));

        Assert.Equal(
            "{Binding RemoteContent.IsPanelVisible}",
            remoteSurface.Attribute("IsVisible")?.Value);
        Assert.False(HasClass(remoteSurface, "surface"));
        Assert.True(HasClass(bannerCard, "surface"));
        Assert.True(HasClass(newsCard, "surface"));
        Assert.Same(bannerCard.Parent, newsCard.Parent);
        Assert.Contains(
            document.Descendants().Single(element =>
                element.Name.LocalName == "StackPanel" && HasClass(element, "social-rail")).Ancestors(),
            element => element.Name.LocalName == "Border" && HasClass(element, "remote-surface"));
        Assert.DoesNotContain(
            newsCard.Descendants(),
            element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding Shell.I18n[news]}");
    }

    [Fact]
    public void MainWindow_CarouselPlayback_RemovesTheManualPauseButton()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var pageText = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding RemoteContent.CarouselPageText}");
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding RemoteContent.ToggleCarouselLoopCommand}");
        Assert.Equal("{Binding RemoteContent.CarouselPageText}", pageText.Attribute("Text")?.Value);
    }

    [Fact]
    public void MainWindow_CarouselNavigation_HoverUsesSemiTransparentChromeBackground()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var hoverStyle = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Button.icon-button.carousel-navigation:pointerover");

        Assert.Equal(
            "{DynamicResource Cafe.Color.Chrome.ControlHover}",
            hoverStyle
                .Elements()
                .Single(element => element.Name.LocalName == "Setter" && element.Attribute("Property")?.Value == "Background")
                .Attribute("Value")?.Value);

        var navigationStyle = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Button.icon-button.carousel-navigation");
        Assert.Equal(
            "{StaticResource Cafe.Control.Template.BorderButton}",
            navigationStyle
                .Elements()
                .Single(element => element.Name.LocalName == "Setter" && element.Attribute("Property")?.Value == "Template")
                .Attribute("Value")?.Value);
    }

    [Fact]
    public void MainWindow_SocialLinks_UseTheRightSideVerticalRail()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var rail = document
            .Descendants()
            .Single(element => element.Name.LocalName == "StackPanel" && HasClass(element, "social-rail"));

        Assert.Equal("Right", rail.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Center", rail.Attribute("VerticalAlignment")?.Value);
        Assert.Contains(
            rail.Descendants(),
            element => element.Name.LocalName == "ItemsPanelTemplate"
                && element.Elements().Single().Name.LocalName == "StackPanel");
        Assert.Contains(
            rail.Descendants(),
            element => element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding WindowChrome.OpenOfficialSiteCommand}");

        var socialStyles = XDocument.Load(ProjectFile("Views/Styles/RemoteContent.axaml"));
        var socialStyle = socialStyles
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Button.social-chip");
        Assert.Equal(
            "{DynamicResource Cafe.Color.Chrome.ControlHover}",
            socialStyle
                .Elements()
                .Single(element => element.Name.LocalName == "Setter"
                    && element.Attribute("Property")?.Value == "Background")
                .Attribute("Value")?.Value);
        Assert.Equal(
            "{DynamicResource Cafe.Color.OnChrome}",
            socialStyle
                .Elements()
                .Single(element => element.Name.LocalName == "Setter"
                    && element.Attribute("Property")?.Value == "Foreground")
                .Attribute("Value")?.Value);

        var foundation = XDocument.Load(ProjectFile("Views/Styles/Foundation.axaml"));
        var margin = foundation
            .Descendants()
            .Single(element => element.Name.LocalName == "Thickness"
                && element.Attributes().Any(attribute => attribute.Name.LocalName == "Key"
                    && attribute.Value == "Cafe.Layout.Home.SocialRail.Margin"));
        Assert.Equal("0,0,8,0", margin.Value);
    }

    [Fact]
    public void MainWindow_RightEdgeGradient_UsesHorizontalChromeGradient()
    {
        var mainWindow = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        Assert.Contains(
            mainWindow.Descendants(),
            element => element.Name.LocalName == "Border" && HasClass(element, "home-edge-gradient"));

        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var edgeStyle = styles
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Border.home-edge-gradient");
        Assert.Equal(
            "{DynamicResource Cafe.Color.Chrome.EdgeGradient}",
            edgeStyle
                .Elements()
                .Single(element => element.Name.LocalName == "Setter"
                    && element.Attribute("Property")?.Value == "Background")
                .Attribute("Value")?.Value);

        var theme = XDocument.Load(ProjectFile("Views/Styles/Theme.axaml"));
        var gradient = theme
            .Descendants()
            .Single(element => element.Name.LocalName == "LinearGradientBrush"
                && element.Attributes().Any(attribute => attribute.Name.LocalName == "Key"
                    && attribute.Value == "Cafe.Color.Chrome.EdgeGradient"));
        Assert.Equal("0%,0%", gradient.Attribute("StartPoint")?.Value);
        Assert.Equal("100%,0%", gradient.Attribute("EndPoint")?.Value);
    }

    private static readonly string[] StyleFiles =
    [
        "Views/MainWindow.Styles.axaml",
        "Views/Styles/Controls.axaml",
        "Views/Styles/Diagnostics.axaml",
        "Views/Styles/Foundation.axaml",
        "Views/Styles/RemoteContent.axaml",
        "Views/Styles/SetupWizard.axaml",
        "Views/Styles/Theme.axaml",
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
                "Settings.Editor.Current.StatusDetailMode"
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
                "Settings.Appearance.IsMotionEnabled",
                "Settings.ChooseBackgroundImageCommand",
                "Settings.ChooseBackgroundFolderCommand",
                "Settings.ClearBackgroundCommand",
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
        Assert.Equal("*", dialogLayout.Attribute("RowDefinitions")?.Value);

        var workspace = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "settings-workspace"));
        Assert.Equal("188,*", workspace.Attribute("ColumnDefinitions")?.Value);

        var navigation = workspace
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ListBox"
                && HasClass(element, "settings-navigation"));
        var navigationPane = workspace
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "settings-navigation-pane"));
        var settingsTitle = navigationPane
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBlock"
                && HasClass(element, "settings-navigation-title"));
        Assert.Equal("{Binding Shell.I18n[settings]}", settingsTitle.Attribute("Text")?.Value);
        var closeButton = workspace
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && HasClass(element, "settings-floating-close"));
        Assert.Equal("1", closeButton.Attribute("Grid.Column")?.Value);
        Assert.DoesNotContain(
            workspace.Descendants(),
            element => HasClass(element, "settings-dialog-header"));
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
        Assert.DoesNotContain(content.Descendants(), element => HasClass(element, "status-summary"));
        Assert.Contains(content.Descendants(), element => HasClass(element, "settings-save-error"));
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
        Assert.Equal(
            "{Binding Shell.I18n[settingsGroupDisplay]}",
            groups[2]
                .Elements()
                .First(element => element.Name.LocalName == "TextBlock")
                .Attribute("Text")?.Value);
    }

    [Fact]
    public void AppearanceSection_UsesCompactToggleTemplateSpacing()
    {
        var document = XDocument.Load(ProjectFile("Views/SettingsAppearanceSection.axaml"));
        var resources = document.Root?
            .Elements()
            .Single(element => element.Name.LocalName == "UserControl.Resources");

        Assert.NotNull(resources);
        Assert.Equal(
            "2",
            resources!.Elements().Single(element =>
                    element.Attributes().Single(attribute => attribute.Name.LocalName == "Key").Value
                        == "ToggleSwitchPreContentMargin")
                .Value);
        Assert.Equal(
            "2",
            resources.Elements().Single(element =>
                    element.Attributes().Single(attribute => attribute.Name.LocalName == "Key").Value
                        == "ToggleSwitchPostContentMargin")
                .Value);

        var toggles = document
            .Descendants()
            .Where(element => element.Name.LocalName == "ToggleSwitch")
            .ToArray();
        Assert.Equal(2, toggles.Length);
        Assert.All(toggles, toggle =>
        {
            Assert.Contains("design-toggle", toggle.Attribute("Classes")?.Value, StringComparison.Ordinal);
            Assert.Equal("Center", toggle.Attribute("VerticalAlignment")?.Value);
        });
    }

    [Fact]
    public void AppearancePalette_ReservesWidthForFourDesignSwatches()
    {
        var document = XDocument.Load(ProjectFile("Views/SettingsAppearanceSection.axaml"));
        var palette = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ItemsControl"
                && element.Attribute("ItemsSource")?.Value
                    == "{Binding Settings.Appearance.ThemeColorPaletteItems}");

        Assert.Equal("136", palette.Attribute("Width")?.Value);
    }

    [Fact]
    public void AppearancePalette_IsOnlyVisibleForWallpaperAndRetainsRefreshAction()
    {
        var document = XDocument.Load(ProjectFile("Views/SettingsAppearanceSection.axaml"));
        var paletteFrame = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "settings-row-frame"));

        Assert.Equal(
            "{Binding Settings.Appearance.IsWallpaperThemeColorSelected}",
            paletteFrame.Attribute("IsVisible")?.Value);

        var refreshButton = paletteFrame
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value
                    == "{Binding Settings.Appearance.RefreshThemeColorPaletteCommand}");

        Assert.Equal(
            "{Binding Shell.I18n[refreshThemeColorPalette]}",
            refreshButton.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal(
            "{Binding Shell.I18n[refreshThemeColorPalette]}",
            refreshButton.Attribute("ToolTip.Tip")?.Value);
    }

    [Fact]
    public void AppearancePalette_UsesPrototypeColorPickerMetrics()
    {
        var view = XDocument.Load(ProjectFile("Views/SettingsAppearanceSection.axaml"));
        var paletteButton = view
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && HasClass(element, "color-swatch-button"));

        Assert.Equal("{Binding IsSelected}", paletteButton.Attribute("Classes.selected")?.Value);
        Assert.DoesNotContain(
            paletteButton.Descendants(),
            element => element.Attribute("Classes.selected") is not null);

        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var colorPickerStyle = styles
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "ColorPicker.setting-control");
        Assert.Equal(
            "178",
            colorPickerStyle.Elements().Single(element => element.Attribute("Property")?.Value == "Width")
                .Attribute("Value")?.Value);

        var paletteStyle = styles
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Button.color-swatch-button");
        Assert.Equal(
            "{DynamicResource Cafe.Color.Surface.Dialog}",
            paletteStyle.Elements().Single(element => element.Attribute("Property")?.Value == "Background")
                .Attribute("Value")?.Value);
        Assert.Equal(
            "{DynamicResource Cafe.Color.Border.Button}",
            paletteStyle.Elements().Single(element => element.Attribute("Property")?.Value == "BorderBrush")
                .Attribute("Value")?.Value);

        var selectedPaletteStyle = styles
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Button.color-swatch-button.selected");
        Assert.Equal(
            "{DynamicResource Cafe.Color.Accent}",
            selectedPaletteStyle.Elements().Single(element => element.Attribute("Property")?.Value == "BorderBrush")
                .Attribute("Value")?.Value);
        Assert.Equal(
            "2",
            selectedPaletteStyle.Elements().Single(element => element.Attribute("Property")?.Value == "BorderThickness")
                .Attribute("Value")?.Value);
    }

    [Fact]
    public void AboutSection_UsesSettingsGroupForTopLevelRhythm()
    {
        var document = XDocument.Load(ProjectFile("Views/SettingsAboutSection.axaml"));
        var root = document.Root?.Elements().Single(element => element.Name.LocalName == "StackPanel");

        Assert.NotNull(root);
        Assert.True(HasClass(root!, "settings-group"));
    }

    [Fact]
    public void AboutSection_DoesNotRenderDecorativeHeadingIcon()
    {
        var document = XDocument.Load(ProjectFile("Views/SettingsAboutSection.axaml"));
        var heading = document
            .Descendants()
            .Single(element => element.Name.LocalName == "StackPanel" && HasClass(element, "card-heading"));

        Assert.DoesNotContain(
            heading.Elements(),
            element => element.Name.LocalName == "MaterialIcon");
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

        var appDocument = XDocument.Load(ProjectFile("Views/Styles/Foundation.axaml"));
        var monospace = appDocument
            .Descendants()
            .Single(element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "Cafe.Type.Family.Monospace"));
        Assert.Equal("Consolas", monospace.Value.Trim());
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
                Assert.True(
                    width.StartsWith("{StaticResource Cafe.Icon.", StringComparison.Ordinal)
                    && width.EndsWith('}'),
                    $"MaterialIcon width must use a Cafe.Icon semantic token, but was '{width}'.");
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
                    value.StartsWith("{StaticResource Cafe.Space.", StringComparison.Ordinal)
                    || value.StartsWith("{StaticResource Cafe.Layout.", StringComparison.Ordinal),
                    $"Spacing value must use a semantic Cafe token: {value}"));
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
                attribute =>
                    attribute.Name.LocalName == "Padding"
                    && !attribute.Value.StartsWith("{StaticResource Cafe.", StringComparison.Ordinal));
            Assert.DoesNotContain(
                attributes,
                attribute =>
                    attribute.Name.LocalName == "Margin"
                    && attribute.Value is "0,0,16,0" or "0,4,0,0");
        }
    }

    [Fact]
    public void CornerRadii_UseDeclaredHierarchyTokens()
    {
        var allowedTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "0",
            "{StaticResource Cafe.Radius.Small}",
            "{StaticResource Cafe.Radius.Medium}",
            "{StaticResource Cafe.Radius.Large}",
            "{StaticResource Cafe.Radius.None}",
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
    public void NewsCategoryTab_FocusUsesAStraightUnderline()
    {
        var document = XDocument.Load(ProjectFile("Views/Styles/RemoteContent.axaml"));
        var baseStyle = document
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Button.news-category-tab");
        var focusStyle = document
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Button.news-category-tab:focus-visible");

        Assert.Equal(
            "{x:Null}",
            baseStyle.Elements().Single(element => element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "FocusAdorner")
                .Attribute("Value")?.Value);
        Assert.Equal(
            "{StaticResource Cafe.Radius.None}",
            baseStyle.Elements().Single(element => element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "CornerRadius")
                .Attribute("Value")?.Value);
        Assert.Equal(
            "{DynamicResource Cafe.Border.TabUnderline}",
            focusStyle.Elements().Single(element => element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "BorderThickness")
                .Attribute("Value")?.Value);
    }

    [Fact]
    public void SharedControls_DeclareTheApplicationCornerRadiusDefaults()
    {
        var document = XDocument.Load(ProjectFile("Views/Styles/Controls.axaml"));
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Button"] = "{StaticResource Cafe.Radius.Small}",
            ["TextBox"] = "{StaticResource Cafe.Radius.Medium}",
            ["ComboBox"] = "{StaticResource Cafe.Radius.Medium}",
            ["ColorPicker"] = "{StaticResource Cafe.Radius.Medium}",
            ["ListBox"] = "{StaticResource Cafe.Radius.Medium}",
            ["ListBoxItem"] = "{StaticResource Cafe.Radius.Small}",
            ["ProgressBar"] = "{StaticResource Cafe.Radius.Small}"
        };

        foreach (var (selector, radius) in expected)
        {
            var style = document
                .Descendants()
                .Single(element => element.Name.LocalName == "Style"
                    && element.Attribute("Selector")?.Value == selector);
            Assert.Equal(
                radius,
                style.Elements().Single(element => element.Name.LocalName == "Setter"
                    && element.Attribute("Property")?.Value == "CornerRadius")
                    .Attribute("Value")?.Value);
        }
    }

    [Fact]
    public void PrimaryAndDangerActions_UseTheMediumRadius()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));

        foreach (var selector in new[] { "Button.primary-action", "Button.danger-action" })
        {
            var style = document
                .Descendants()
                .Single(element => element.Name.LocalName == "Style"
                    && element.Attribute("Selector")?.Value == selector);
            Assert.Equal(
                "{StaticResource Cafe.Radius.Medium}",
                style.Elements().Single(element => element.Name.LocalName == "Setter"
                    && element.Attribute("Property")?.Value == "CornerRadius")
                    .Attribute("Value")?.Value);
        }
    }

    [Fact]
    public void OverlayOrder_IsBaseThenSettingsThenDialogsThenToast()
    {
        var mainWindow = File.ReadAllText(ProjectFile("Views/MainWindow.axaml"));
        var settingsIndex = mainWindow.IndexOf("<views:MainWindowSettingsOverlay/>", StringComparison.Ordinal);
        var logViewerIndex = mainWindow.IndexOf("<views:MainWindowLogViewerOverlay/>", StringComparison.Ordinal);
        var debugIndex = mainWindow.IndexOf("<views:MainWindowDebugOverlay/>", StringComparison.Ordinal);
        var dialogsIndex = mainWindow.IndexOf("<views:MainWindowDialogsOverlay/>", StringComparison.Ordinal);
        var toastIndex = mainWindow.IndexOf("<views:MainWindowToastOverlay/>", StringComparison.Ordinal);

        Assert.True(settingsIndex >= 0);
        Assert.True(debugIndex > settingsIndex);
        Assert.True(logViewerIndex > debugIndex);
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
                ["{Binding ResourcePanel.CloseResourcePanelCommand}"] = "{Binding Shell.I18n[close]}",
                ["{Binding ResourcePanel.SaveManualResourcePanelUidCommand}"] = "{Binding Shell.I18n[resourcePanelSaveUid]}",
                ["{Binding ResourcePanel.CancelEditResourcePanelUidCommand}"] = "{Binding Shell.I18n[cancel]}",
                ["{Binding ResourcePanel.BeginEditResourcePanelUidCommand}"] = "{Binding Shell.I18n[resourcePanelChangeUid]}",
                ["{Binding ResourcePanel.RefreshResourcePanelCommand}"] = "{Binding Shell.I18n[resourcePanelRefresh]}",
                ["{Binding ResourcePanel.SaveResourcePanelCommand}"] = "{Binding Shell.I18n[resourcePanelSave]}"
            },
            ["Views/MainWindowLogViewerOverlay.axaml"] = new(StringComparer.Ordinal)
            {
                ["{Binding LogViewer.CloseCommand}"] = "{Binding Shell.I18n[close]}",
                ["{Binding LogViewer.ExportCommand}"] = "{Binding Shell.I18n[exportLogs]}"
            },
            ["Views/MainWindowToastOverlay.axaml"] = new(StringComparer.Ordinal)
            {
                ["{Binding #ToastOverlayRoot.((vm:MainWindowViewModel)DataContext).Toasts.DismissToastCommand}"] =
                    "{Binding #ToastOverlayRoot.((vm:MainWindowViewModel)DataContext).Shell.I18n[close]}"
            },
            ["Views/SetupWizardOverlay.axaml"] = new(StringComparer.Ordinal)
            {
                ["{Binding Dialogs.RequestSetupWizardExitCommand}"] = "{Binding Shell.I18n[setupWizardSkip]}",
                ["{Binding Dialogs.SetupWizard.BrowseGamePathCommand}"] = "{Binding Shell.I18n[setupWizardBrowse]}",
                ["{Binding Dialogs.SetupWizard.PreviousCommand}"] = "{Binding Shell.I18n[setupWizardPrevious]}",
                ["{Binding Dialogs.SetupWizard.NextCommand}"] = "{Binding Shell.I18n[setupWizardNext]}",
                ["{Binding Dialogs.SetupWizard.CompleteCommand}"] = "{Binding Shell.I18n[setupWizardFinish]}"
            }
        };

        foreach (var (path, expectedByCommand) in expectedActions)
        {
            var document = XDocument.Load(ProjectFile(path));
            foreach (var (command, expectedName) in expectedByCommand)
            {
                var matchingActions = document
                    .Descendants()
                    .Where(element =>
                        (element.Name.LocalName == "Button"
                         && element.Attribute("Command")?.Value == command)
                        || (element.Name.LocalName == "DialogFrame"
                            && element.Attribute("CloseCommand")?.Value == command))
                    .ToList();

                Assert.NotEmpty(matchingActions);
                Assert.All(
                    matchingActions,
                    action => Assert.Equal(
                        expectedName,
                        action.Attribute(
                            action.Name.LocalName == "DialogFrame"
                                ? "CloseToolTip"
                                : "AutomationProperties.Name")?.Value));
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
        var resourceStatus = resourcePanel
            .Descendants()
            .Single(element => element.Name.LocalName == "Border" && HasClass(element, "resource-panel-status"));

        Assert.Equal(2, uidInputs.Count);
        Assert.All(uidInputs, input => Assert.Equal(
            "{Binding Shell.I18n[resourcePanelUid]}",
            input.Attributes().SingleOrDefault(attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name")?.Value));
        Assert.Equal(
            "{Binding Shell.I18n[resourcePanelUidSource]}",
            uidSource.Attributes().SingleOrDefault(attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name")?.Value);
        Assert.Equal(
            "{Binding DisplayName}",
            resourceSwitch.Attributes().SingleOrDefault(attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name")?.Value);
        Assert.DoesNotContain(
            resourceStatus.Descendants(),
            element => element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding ResourcePanel.ResourcePanelUidText}");
        Assert.Single(
            resourceStatus.Descendants(),
            element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding ResourcePanel.ResourcePanelMessage}");
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
                    == "{Binding #ToastOverlayRoot.((vm:MainWindowViewModel)DataContext).Toasts.DismissToastCommand}");
        const string expectedBinding =
            "{Binding #ToastOverlayRoot.((vm:MainWindowViewModel)DataContext).Shell.I18n[close]}";

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
        var application = XDocument.Load(ProjectFile("Views/Styles/Foundation.axaml"));
        var minWidthToken = application
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key"
                && attribute.Value == "Cafe.Layout.Settings.RowContent.MinWidth"));

        Assert.Equal("*,Auto", layout.Attribute("ColumnDefinitions")?.Value);
        Assert.Null(copy.Attribute("Grid.Column"));
        Assert.True(double.Parse(minWidthToken.Value, CultureInfo.InvariantCulture) > 0);
        Assert.Equal(
            "{StaticResource Cafe.Layout.Settings.RowContent.MinWidth}",
            copy.Attribute("MinWidth")?.Value);
        Assert.Equal(2, textBlocks.Count);
        Assert.All(textBlocks, text => Assert.Equal("Wrap", text.Attribute("TextWrapping")?.Value));
        Assert.Equal("1", action.Attribute("Grid.Column")?.Value);
        var codeBehind = File.ReadAllText(ProjectFile("Controls/SettingRow.axaml.cs"));
        Assert.Contains("CompactBreakpoint = 600", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(ActionPresenter, isCompact ? 0 : 1);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("new RowDefinitions(\"Auto,Auto\")", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmDialog_LongContentScrollsWhileActionsRemainFixed()
    {
        var document = XDocument.Load(ProjectFile("Controls/ConfirmDialog.axaml"));
        var frame = document.Descendants().Single(element => element.Name.LocalName == "DialogFrame");
        var frameDocument = XDocument.Load(ProjectFile("Controls/DialogFrame.axaml"));
        var layout = frameDocument.Descendants().Single(element =>
            element.Name.LocalName == "Grid"
            && element.Attribute("RowDefinitions")?.Value == "Auto,*,Auto");
        var messageScroller = layout.Elements().Single(element => element.Name.LocalName == "ScrollViewer");
        var actions = document.Descendants().Single(element => element.Name.LocalName == "StackPanel" && HasClass(element, "confirm-actions"));

        Assert.Equal(
            "{StaticResource Cafe.Layout.Dialog.Confirm.MaxHeight}",
            frame.Attribute("MaxHeight")?.Value);
        var application = XDocument.Load(ProjectFile("Views/Styles/Foundation.axaml"));
        var maxHeightToken = application
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key"
                && attribute.Value == "Cafe.Layout.Dialog.Confirm.MaxHeight"));
        Assert.Equal("480", maxHeightToken.Value);
        Assert.Equal("Auto,*,Auto", layout.Attribute("RowDefinitions")?.Value);
        Assert.Equal("1", messageScroller.Attribute("Grid.Row")?.Value);
        Assert.Null(actions.Attribute("Grid.Row"));
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
        Assert.DoesNotContain(
            settingsOverlay.Descendants(),
            element => element.Name.LocalName == "Button" && HasClass(element, "dialog-action"));
    }

    [Theory]
    [InlineData("LauncherStrings.resx")]
    [InlineData("LauncherStrings.ja.resx")]
    [InlineData("LauncherStrings.zh-Hans.resx")]
    [InlineData("LauncherStrings.zh-Hant.resx")]
    public void LogSeverityNames_MatchBetweenViewerFiltersAndSettings(string resxFile)
    {
        var values = TestLocalizationHelper.ReadResx(ProjectFile($"Resources/{resxFile}"));
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
                && element.Attribute("Text")?.Value == "{Binding Shell.I18n[logNoMatchingEntries]}");

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
                    == "{DynamicResource Cafe.Color.Text.Muted}");
    }

    [Theory]
    [InlineData("Views/MainWindowDialogsOverlay.axaml", "{Binding ResourcePanel.CloseResourcePanelCommand}")]
    [InlineData("Views/MainWindowLogViewerOverlay.axaml", "{Binding LogViewer.CloseCommand}")]
    [InlineData("Views/MainWindowDebugOverlay.axaml", "{Binding Debug.CloseCommand}")]
    public void ToolPanel_UsesSharedDialogFrame(string relativePath, string closeCommand)
    {
        var document = XDocument.Load(ProjectFile(relativePath));
        var frame = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "DialogFrame"
                && element.Attribute("UseToolPanelChrome")?.Value == "True");

        Assert.Equal("False", frame.Attribute("ShowIcon")?.Value);
        Assert.Equal(closeCommand, frame.Attribute("CloseCommand")?.Value);
        Assert.NotNull(frame.Element(frame.Name.Namespace + "DialogFrame.BodyContent"));
        Assert.NotNull(frame.Element(frame.Name.Namespace + "DialogFrame.ActionsContent"));
    }

    [Fact]
    public void DebugPanel_UsesDialogBodyPaddingWithoutNestedMargin()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowDebugOverlay.axaml"));
        var frame = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "DialogFrame"
                && element.Attribute("UseToolPanelChrome")?.Value == "True");
        var bodyContent = frame.Element(frame.Name.Namespace + "DialogFrame.BodyContent");
        Assert.NotNull(bodyContent);

        var content = bodyContent!
            .Elements()
            .Single(element => element.Name.LocalName == "StackPanel");

        Assert.Equal("{StaticResource Cafe.Space.3}", content.Attribute("Spacing")?.Value);
        Assert.Null(content.Attribute("Margin"));
    }

    [Fact]
    public void GenericDialogs_UseSharedDialogFrameShell()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowDialogsOverlay.axaml"));
        var titleBindings = new[]
        {
            "{Binding Shell.I18n[notice]}",
            "{Binding Shell.I18n[launcherUpdateAvailableTitle]}",
            "{Binding Shell.I18n[errorDialogTitle]}"
        };

        foreach (var titleBinding in titleBindings)
        {
            var frame = document
                .Descendants()
                .Single(element =>
                    element.Name.LocalName == "DialogFrame"
                    && element.Attribute("Title")?.Value == titleBinding);

            Assert.Equal("True", frame.Attribute("IsConfirmPanel")?.Value);
            Assert.NotNull(frame.Element(frame.Name.Namespace + "DialogFrame.BodyContent"));
            Assert.NotNull(frame.Element(frame.Name.Namespace + "DialogFrame.ActionsContent"));
        }

        Assert.DoesNotContain(
            document.Descendants(),
            element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "dialog")
                && HasClass(element, "confirm-panel"));
    }

    [Fact]
    public void DialogFrame_UsesSharedCloseAndBodyConstraints()
    {
        var document = XDocument.Load(ProjectFile("Controls/DialogFrame.axaml"));
        var closeButton = document
            .Descendants()
            .Single(element => element.Name.LocalName == "Button" && HasClass(element, "dialog-close"));
        var body = document
            .Descendants()
            .Single(element => element.Name.LocalName == "ScrollViewer" && HasClass(element, "dialog-frame-body"));

        Assert.Equal("{Binding ShowCloseButton, ElementName=Root}", closeButton.Attribute("IsVisible")?.Value);
        Assert.Equal("{Binding BodyMaxHeight, ElementName=Root}", body.Attribute("MaxHeight")?.Value);
    }

    [Fact]
    public void DialogFrame_ConfirmPanelsKeepInnerContentMargin()
    {
        var document = XDocument.Load(ProjectFile("Controls/DialogFrame.axaml"));
        var layout = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && element.Attribute("RowDefinitions")?.Value == "Auto,*,Auto");

        Assert.Equal(
            "{Binding IsConfirmPanel, ElementName=Root}",
            layout.Attribute("Classes.confirm-layout")?.Value);
    }

    [Fact]
    public void SharedStyles_UseCaptionAndInsetTokens()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        Dictionary<string, Dictionary<string, string>> expectedSetters = new(StringComparer.Ordinal)
        {
            ["TextBlock.caption"] = new(StringComparer.Ordinal)
            {
                ["FontSize"] = "{StaticResource Cafe.Type.Caption}"
            },
            ["Button.icon-link"] = new(StringComparer.Ordinal)
            {
                ["Padding"] = "{StaticResource Cafe.Inset.Tab}"
            },
            ["Button.flat-action"] = new(StringComparer.Ordinal)
            {
                ["Padding"] = "{StaticResource Cafe.Inset.Control}"
            },
            ["Grid.confirm-layout"] = new(StringComparer.Ordinal)
            {
                ["Margin"] = "{StaticResource Cafe.Inset.Large}"
            },
            ["Border.confirm-message"] = new(StringComparer.Ordinal)
            {
                ["Padding"] = "{StaticResource Cafe.Inset.Medium}"
            },
            ["TextBox.uid-input"] = new(StringComparer.Ordinal)
            {
                ["Padding"] = "{StaticResource Cafe.Inset.Control}"
            }
        };

        foreach (var (selector, setters) in expectedSetters)
        {
            var style = document
                .Descendants()
                .Single(element =>
                    element.Name.LocalName == "Style"
                    && element.Attribute("Selector")?.Value == selector);

            foreach (var (property, value) in setters)
            {
                Assert.Equal(
                    value,
                    style.Elements()
                        .Single(element =>
                            element.Name.LocalName == "Setter"
                            && element.Attribute("Property")?.Value == property)
                .Attribute("Value")?.Value);
            }
        }

        var controls = XDocument.Load(ProjectFile("Views/Styles/Controls.axaml"));
        var surfaceStyle = controls
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Border.surface");
        Assert.Equal(
            "{StaticResource Cafe.Inset.Large}",
            surfaceStyle.Elements()
                .Single(element =>
                    element.Name.LocalName == "Setter"
                    && element.Attribute("Property")?.Value == "Padding")
                .Attribute("Value")?.Value);

        var diagnostics = XDocument.Load(ProjectFile("Views/Styles/Diagnostics.axaml"));
        Assert.All(
            diagnostics
                .Descendants()
                .Where(element =>
                    element.Name.LocalName == "Style"
                    && element.Attribute("Selector")?.Value is "Border.log-empty-state" or "ListBox.log-entry-list")
                .SelectMany(style => style.Elements().Where(element =>
                    element.Name.LocalName == "Setter"
                    && element.Attribute("Property")?.Value == "Margin")),
            setter => Assert.Equal("{StaticResource Cafe.Inset.Large}", setter.Attribute("Value")?.Value));
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
            "Shell.I18n[bannerLoading]",
            mainWindow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "BannerBitmap, Converter={x:Static ObjectConverters.IsNull}",
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
        Assert.Equal("1200", window.Attribute("MinWidth")?.Value);
        Assert.Equal("720", window.Attribute("MinHeight")?.Value);
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
                "{Binding Shell.I18n[downloadSourceCafe]}",
                "{Binding Shell.I18n[downloadSourceOfficial]}",
                "{Binding Shell.I18n[proxyAuto]}",
                "{Binding Shell.I18n[proxyDirect]}",
                "{Binding Shell.I18n[proxySystem]}"
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
    public void SettingsPanel_AutoSavesAndUsesOnlyTheHeaderCloseAction()
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
        Assert.DoesNotContain("Settings.RequestResetSettingsCommand", settingsOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.SaveSettingsCommand", settingsOverlay, StringComparison.Ordinal);
        Assert.Contains("RowDefinitions=\"Auto,*\"", settingsOverlay, StringComparison.Ordinal);
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
        Assert.Empty(settingsFooterButtons);
        // The escape-key resolution for the settings modal lives in ShellLifecycle
        // where modal coordination was consolidated from ShellCoordinator.
        var shellLifecycle = File.ReadAllText(ProjectFile("Features/Shell/ShellLifecycle.cs"));
        Assert.Contains(
            "case ModalKind.Settings:",
            shellLifecycle,
            StringComparison.Ordinal);
        Assert.Contains(
            "ShowSettingsCommand",
            shellLifecycle,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "vm.WindowChrome.IsSettingsVisible",
            mainWindowCodeBehind,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsOverlay_UsesPersistentAutoSaveFailureStateInsteadOfStatusSummary()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
        Assert.DoesNotContain(document.Descendants(), element => HasClass(element, "settings-status-summary"));
        var error = document.Descendants().Single(element => HasClass(element, "settings-save-error"));
        Assert.Equal("{Binding Settings.HasAutoSaveFailure}", error.Attribute("IsVisible")?.Value);
        Assert.Contains(
            error.Descendants(),
            element => element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding Settings.RetryAutoSaveCommand}");
    }

    [Fact]
    public void ConfirmDialogs_DangerousActionsUseDangerHeadingIcons()
    {
        var control = XDocument.Load(ProjectFile("Controls/ConfirmDialog.axaml"));
        var frame = control
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "DialogFrame");
        Assert.Equal(
            "{Binding IsDangerIcon, ElementName=Root}",
            frame.Attribute("IsDanger")?.Value);

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
            "SettingComboRow",
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
                    var nameAttribute = control.Name.LocalName == "SettingComboRow"
                        ? "Title"
                        : "AutomationProperties.Name";
                    var automationName = control
                        .Attributes()
                        .SingleOrDefault(attribute =>
                            attribute.Name.LocalName == nameAttribute)
                        ?.Value;

                    Assert.False(
                        string.IsNullOrWhiteSpace(automationName),
                        $"{sectionPath}: {control.Name.LocalName} is missing AutomationProperties.Name.");
                    Assert.Contains("Shell.I18n[", automationName, StringComparison.Ordinal);
                });
        }
    }

    [Fact]
    public void AdvancedSettings_LogAndResetActionsBelongToDedicatedRows()
    {
        var document = XDocument.Load(ProjectFile("Views/SettingsAdvancedSection.axaml"));
        var logFilesRow = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "SettingRow"
                && element.Attribute("Title")?.Value == "{Binding Shell.I18n[logFiles]}");
        Assert.Equal(
            "{Binding Shell.I18n[logFiles]}",
            logFilesRow.Attribute("Title")?.Value);
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
            "{StaticResource Cafe.Space.2}",
            actionPanel.Attribute("ItemSpacing")?.Value);
        Assert.Equal(
            "{StaticResource Cafe.Space.2}",
            actionPanel.Attribute("LineSpacing")?.Value);
        Assert.Equal(
            "{StaticResource Cafe.Layout.Settings.RowAction.MaxWidth}",
            actionPanel.Attribute("MaxWidth")?.Value);

        var foundation = XDocument.Load(ProjectFile("Views/Styles/Foundation.axaml"));
        var actionMaxWidth = foundation
            .Descendants()
            .Single(element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "Cafe.Layout.Settings.RowAction.MaxWidth"));
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
            document.Root!.Descendants(),
            element => element.Name.LocalName == "WrapPanel" && element.Parent?.Name.LocalName == "StackPanel");

        var resetRow = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "SettingRow"
                && element.Attribute("Title")?.Value == "{Binding Shell.I18n[debugResetSettingsConfirm]}");
        Assert.Equal(
            "{Binding Shell.I18n[debugResetSettingsDescription]}",
            resetRow.Attribute("Description")?.Value);
        var resetButton = resetRow
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding Settings.RequestResetSettingsCommand}");
        Assert.True(HasClass(resetButton, "danger-action"));
        Assert.Equal(
            "{Binding Shell.I18n[debugResetSettingsConfirm]}",
            resetButton.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal(
            "{Binding Shell.I18n[debugResetSettingsConfirm]}",
            resetButton.Attribute("ToolTip.Tip")?.Value);
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
            "WindowChrome.OpenDataDirectoryCommand",
            "Settings.RequestResetSettingsCommand");

        Assert.Contains("Shell.I18n[aboutActionsGeneral]", aboutText, StringComparison.Ordinal);
        Assert.DoesNotContain("LogViewer.OpenCommand", aboutText, StringComparison.Ordinal);
        Assert.DoesNotContain("LogViewer.ExportCommand", aboutText, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowChrome.OpenDataDirectoryCommand", aboutText, StringComparison.Ordinal);
        Assert.Contains("Shell.I18n[settingsGroupDiagnostics]", advancedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.RequestResetSettingsCommand", overlay, StringComparison.Ordinal);

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
        Assert.Empty(footerButtons);
    }

    [Fact]
    public void SettingComboRow_SelectedValueBinding_UsesSelectableOptionDataType()
    {
        var document = XDocument.Load(ProjectFile("Controls/SettingComboRow.axaml"));
        var comboBox = document
            .Descendants()
            .Single(element => element.Name.LocalName == "ComboBox");

        Assert.Equal(
            "{Binding Code, DataType={x:Type models:SelectableOption}}",
            comboBox.Attribute("SelectedValueBinding")?.Value);
    }

    [Fact]
    public void ToastActions_UseTitleAlignedGridAndPrimaryFirstLeftAlignedLayout()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowToastOverlay.axaml"));
        var layout = document.Descendants().Single(element => HasClass(element, "toast-layout"));
        Assert.Equal("Auto,*,Auto", layout.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("Auto,Auto,Auto", layout.Attribute("RowDefinitions")?.Value);

        var title = document.Descendants().Single(element => HasClass(element, "toast-title"));
        Assert.Equal("{Binding Title}", title.Attribute("Text")?.Value);
        Assert.Equal("0", title.Attribute("Grid.Row")?.Value);
        Assert.Equal("1", title.Attribute("Grid.Column")?.Value);

        var icon = document.Descendants().Single(element => HasClass(element, "toast-icon"));
        Assert.Equal("0", icon.Attribute("Grid.Row")?.Value);
        Assert.Equal("0", icon.Attribute("Grid.Column")?.Value);
        Assert.Equal("Center", icon.Attribute("VerticalAlignment")?.Value);
        Assert.Null(icon.Attribute("Margin"));

        var actions = document.Descendants().Single(element => HasClass(element, "toast-actions"));
        Assert.Equal("2", actions.Attribute("Grid.Row")?.Value);
        Assert.Equal("1", actions.Attribute("Grid.Column")?.Value);
        Assert.Equal("Left", actions.Attribute("HorizontalAlignment")?.Value);

        var actionButtons = actions.Elements().Where(element => element.Name.LocalName == "Button").ToArray();
        Assert.Equal(2, actionButtons.Length);
        Assert.True(HasClass(actionButtons[0], "toast-primary-action"));
        Assert.True(HasClass(actionButtons[1], "toast-secondary-action"));
        Assert.Equal(
            "{Binding #ToastOverlayRoot.((vm:MainWindowViewModel)DataContext).Toasts.ExecutePrimaryToastActionCommand}",
            actionButtons[0].Attribute("Command")?.Value);
        Assert.Equal(
            "{Binding #ToastOverlayRoot.((vm:MainWindowViewModel)DataContext).Toasts.ExecuteSecondaryToastActionCommand}",
            actionButtons[1].Attribute("Command")?.Value);
        Assert.Equal("{Binding Id}", actionButtons[0].Attribute("CommandParameter")?.Value);
        Assert.Equal("{Binding Id}", actionButtons[1].Attribute("CommandParameter")?.Value);
        Assert.Equal("{Binding PrimaryActionLabel}", actionButtons[0].Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("{Binding SecondaryActionLabel}", actionButtons[1].Attribute("AutomationProperties.Name")?.Value);

    }

    [Fact]
    public void ToastProgress_OnlyRepresentsExecutingActions()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowToastOverlay.axaml"));
        var progressElements = document.Descendants()
            .Where(element => HasClass(element, "toast-progress")).ToArray();
        Assert.Single(progressElements);
        var actionExecuting = progressElements[0];
        Assert.Equal("1", actionExecuting.Attribute("Grid.Row")?.Value);
        Assert.Equal("{Binding IsActionExecuting}", actionExecuting.Attribute("IsVisible")?.Value);
        Assert.Null(actionExecuting.Attribute("Value"));

    }

    [Fact]
    public void DebugPanel_ProvidesLocalizedActionToastEntry()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowDebugOverlay.axaml"));
        var button = document.Descendants().Single(element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Command")?.Value == "{Binding Debug.TestActionToastCommand}");

        Assert.Equal(
            "{Binding Shell.I18n[debugTestActionToast]}",
            button.Attribute("AutomationProperties.Name")?.Value);
        var text = button.Descendants().Single(element => element.Name.LocalName == "TextBlock");
        Assert.Equal("{Binding Shell.I18n[debugTestActionToast]}", text.Attribute("Text")?.Value);
    }

    [Fact]
    public void ToastStack_UsesRootMotionPreference()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowToastOverlay.axaml"));
        var controlsNamespace = document.Root?.GetNamespaceOfPrefix("controls");
        Assert.NotNull(controlsNamespace);
        var toastList = document
            .Descendants()
            .Single(element => element.Name.LocalName == "StackPanel" && HasClass(element, "toast-list"));

        Assert.Equal(
            "{Binding #ToastOverlayRoot.((vm:MainWindowViewModel)DataContext).IsMotionEnabled}",
            toastList.Attribute(controlsNamespace! + "ToastStackMotion.IsEnabled")?.Value);
    }

    [Fact]
    public void CoreMotionOverlays_UseMotionVisibilityWithoutDirectVisibilityBindings()
    {
        var overlayFiles = new[]
        {
            "Views/MainWindowSettingsOverlay.axaml",
            "Views/MainWindowLogViewerOverlay.axaml",
            "Views/MainWindowDebugOverlay.axaml",
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

        Assert.Equal(8, overlays.Count);
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
            var surfaceContent = surface
                .Elements()
                .Single(child => child.Name.LocalName == "Grid");
            Assert.True(HasClass(surfaceContent, "motion-surface-content"));
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
                element.Attribute("Classes.motion-enter")?.Value,
                element.Attribute("Classes.motion-enter")?.Value);
            // Bottom panels now use MultiBinding for IsVisible (AND of panel mode + status detail mode),
            // so IsVisible is no longer a direct binding.
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
    public void SettingsPanel_ExposesOnlyAutoSaveRecoveryStatus()
    {
        var markup = File.ReadAllText(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
        Assert.DoesNotContain("settings-status-summary", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("status-details", markup, StringComparison.Ordinal);
        Assert.Contains("Settings.HasAutoSaveFailure", markup, StringComparison.Ordinal);
        Assert.Contains("Settings.RetryAutoSaveCommand", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DynamicAccent_DoesNotReplaceThemeSpecificInformationTextBrush()
    {
        var settingsViewModel = File.ReadAllText(ProjectFile("Features/Settings/SettingsViewModel.cs"));

        Assert.DoesNotContain(
            "SetBrush(application, \"Cafe.Color.Text.Info\"",
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

    private static void AssertSettingRowIcon(
        XDocument document,
        string titleBinding,
        string expectedIconKind)
    {
        var settingRow = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName is "SettingRow" or "SettingComboRow"
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

}
