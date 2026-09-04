using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

// MainWindow shell contracts: operation panels, title bar actions, game manage
// flyouts, news tabs, and carousel/banner navigation anatomy.
public sealed partial class UiStyleContractTests
{
    [Fact]
    public void LauncherIcons_UserFacingActions_UseApprovedSemanticMappings()
    {
        var mainWindow = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
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

        var resourcePanelButton = mainWindow
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding ResourcePanel.OpenResourcePanelCommand}");
        Assert.Equal(
            "ClipboardText",
            resourcePanelButton.Descendants().Single(element => element.Name.LocalName == "MaterialIcon").Attribute("Kind")?.Value);
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

        // The detailed bottom-panel layouts were removed; the progress panel is the
        // sole remaining operation layout with a dedicated status/action structure.
        var panelLayouts = operationLayouts
            .Where(l => !HasClass(l.Parent!, "operation-status"))
            .ToArray();
        Assert.Single(panelLayouts);
        Assert.All(panelLayouts, layout =>
        {
            Assert.Equal("*,Auto", layout.Attribute("ColumnDefinitions")?.Value);
            Assert.Equal(
                "{StaticResource Launcher.Spacing.Xl}",
                layout.Attribute("ColumnSpacing")?.Value);
            // ADR-016：状态布局属于单一任务容器，不再直接挂在各自 bottom-panel Border 下。
            Assert.Contains(
                layout.Ancestors(),
                element => HasClass(element, "operation-surface"));

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
                "{StaticResource Launcher.Icon.Xxl}",
                statusColumns[0].Attribute("MinWidth")?.Value);
            Assert.Equal("*", statusColumns[1].Attribute("Width")?.Value);
            Assert.Equal(
                "{StaticResource Launcher.Spacing.Md}",
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
    public void MainWindow_InstallPanel_UsesCompactLayoutWithoutDetailedStatus()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var installLayout = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && element.Attribute("RowDefinitions")?.Value == "*,Auto"
                && element.Ancestors().Any(ancestor => HasClass(ancestor, "operation-surface"))
                && element.Descendants().Any(descendant =>
                    descendant.Name.LocalName == "Button"
                    && descendant.Attribute("Command")?.Value == "{Binding Operations.InstallOrUpdateCommand}"));
        var actionButtons = installLayout
            .Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .ToArray();

        Assert.DoesNotContain(installLayout.Descendants(), element => HasClass(element, "operation-status"));
        Assert.Contains(installLayout.Descendants(), element => HasClass(element, "path-field"));
        Assert.Equal(4, actionButtons.Length);
        Assert.DoesNotContain(
            installLayout.DescendantsAndSelf().Attributes(),
            attribute => attribute.Name.LocalName == "Margin"
                && !attribute.Value.StartsWith("{StaticResource Launcher", StringComparison.Ordinal));
    }

    [Fact]
    public void MainWindow_OperationButtons_ExposeLocalizedNamesAndActionPriority()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        Dictionary<string, (string Name, string Priority)> expectedButtons = new(StringComparer.Ordinal)
        {
            ["{Binding RefreshCommand}"] = ("{Binding Shell.I18n[refresh]}", "secondary-operation"),
            ["{Binding Operations.InstallOrUpdateCommand}"] = ("{Binding Operations.InstallButtonText}", "primary-operation"),
            ["{Binding Settings.ChangePersistedGamePathCommand}"] = ("{Binding Shell.I18n[changePath]}", "secondary-operation"),
            ["{Binding Settings.SelectInstalledGameCommand}"] = ("{Binding Shell.I18n[selectInstalledGame]}", "secondary-operation"),
            ["{Binding Operations.StartGameCommand}"] = ("{Binding Shell.I18n[startGame]}", "primary-operation"),
            ["{Binding Operations.PauseResumeCommand}"] = ("{Binding Operations.PauseResumeText}", "secondary-operation"),
            ["{Binding Operations.StopOperationCommand}"] = ("{Binding Shell.I18n[stop]}", "secondary-operation")
        };

        foreach (var (command, expected) in expectedButtons)
        {
            var button = document
                .Descendants()
                .First(element =>
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
        Assert.Contains(
            controlPanel.Descendants(),
            element => element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding Shell.LaunchCheckText}");
    }

    [Fact]
    public void MainWindow_SettingsAdvancedSection_ExposesLauncherSettingsReset()
    {
        var document = XDocument.Load(ProjectFile("Views/SettingsAdvancedSection.axaml"));
        var resetButton = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding Settings.RequestResetSettingsCommand}");

        Assert.Equal(
            "{Binding Shell.I18n[debugResetSettingsTitle]}",
            resetButton.Attribute("AutomationProperties.Name")?.Value);
        Assert.Contains(
            resetButton.Descendants(),
            element => element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding Shell.I18n[debugResetSettingsConfirm]}");

        // 复用 ADR-014 确认对话框族：设置页重置拥有独立可见性通道，但共享同一套文案键。
        var overlay = XDocument.Load(ProjectFile("Views/MainWindowDialogsOverlay.axaml"));
        var dialog = overlay
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ConfirmDialog"
                && element.Attribute("IsOpen")?.Value == "{Binding Dialogs.IsResetSettingsConfirmationVisible}");
        Assert.Equal(
            "{Binding Shell.I18n[debugResetSettingsTitle]}",
            dialog.Attribute("Title")?.Value);
        Assert.Equal(
            "{Binding Shell.I18n[debugResetSettingsConfirm]}",
            dialog.Attribute("ConfirmText")?.Value);
        Assert.Equal(
            "{Binding Dialogs.ConfirmSettingsResetCommand}",
            dialog.Attribute("ConfirmCommand")?.Value);
        Assert.Equal(
            "{Binding Dialogs.CancelSettingsResetCommand}",
            dialog.Attribute("CancelCommand")?.Value);
    }

    [Fact]
    public void MainWindow_GameManageButtons_ExposeFlyoutMenuWithOperationBindings()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var manageButtons = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Button"
                && HasClass(element, "manage"))
            .ToArray();

        Assert.Equal(2, manageButtons.Length);
        Dictionary<string, string> expectedMenuItems = new(StringComparer.Ordinal)
        {
            ["{Binding Operations.CheckForGameUpdateCommand}"] = "{Binding Shell.I18n[gameCheckUpdate]}",
            ["{Binding Operations.CreateGameShortcutCommand}"] = "{Binding Shell.I18n[gameCreateShortcut]}",
            ["{Binding Operations.OpenGameFolderCommand}"] = "{Binding Shell.I18n[gameOpenFolder]}",
            ["{Binding Operations.RequestRepairCommand}"] = "{Binding Shell.I18n[repair]}",
            ["{Binding Operations.RequestUninstallCommand}"] = "{Binding Shell.I18n[uninstall]}"
        };

        foreach (var button in manageButtons)
        {
            Assert.True(HasClass(button, "secondary-operation"), "Manage button must be secondary-operation.");
            Assert.Equal(
                "{Binding Shell.I18n[gameManagement]}",
                button.Attributes().Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name").Value);
            Assert.NotNull(button.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == "ToolTip.Tip"));
            Assert.Equal(
                "{StaticResource Launcher.Icon.Lg}",
                button.Descendants()
                    .Single(element => element.Name.LocalName == "MaterialIcon" && element.Parent == button)
                    .Attribute("Width")?.Value);
            Assert.Equal(
                "Menu",
                button.Descendants()
                    .Single(element => element.Name.LocalName == "MaterialIcon" && element.Parent == button)
                    .Attribute("Kind")?.Value);

            var flyout = button
                .Elements()
                .Single(element => element.Name.LocalName == "Button.Flyout")
                .Elements()
                .Single(element => element.Name.LocalName == "MenuFlyout");
            Assert.Equal("Top", flyout.Attribute("Placement")?.Value);

            var menuItems = flyout
                .Elements()
                .Where(element => element.Name.LocalName == "MenuItem")
                .ToArray();
            Assert.Equal(expectedMenuItems.Count, menuItems.Length);
            foreach (var (command, header) in expectedMenuItems)
            {
                var menuItem = menuItems.Single(element => element.Attribute("Command")?.Value == command);
                Assert.Equal(header, menuItem.Attribute("Header")?.Value);
                Assert.Equal(
                    header,
                    menuItem.Attributes().Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name").Value);
                var icon = menuItem.Descendants().Single(element => element.Name.LocalName == "MaterialIcon");
                Assert.Equal("{StaticResource Launcher.Icon.Md}", icon.Attribute("Width")?.Value);
            }
        }
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
            ["{Binding WindowChrome.OpenDebugPanelCommand}"] = "{Binding Shell.I18n[debugPanel]}",
            ["{Binding ResourcePanel.OpenResourcePanelCommand}"] = "{Binding Shell.I18n[resourcePanel]}",
            ["{Binding WindowChrome.ShowSettingsCommand}"] = "{Binding Shell.I18n[settings]}",
            ["{Binding WindowChrome.MinimizeCommand}"] = "{Binding Shell.I18n[minimize]}",
            ["{Binding WindowChrome.CloseCommand}"] = "{Binding Shell.I18n[close]}"
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
        Assert.Equal("{StaticResource Launcher.Control.Height.Setting}", chrome["Width"]);
        Assert.Equal("{StaticResource Launcher.Control.Height.Setting}", chrome["Height"]);
        Assert.Equal(
            "{StaticResource Launcher.Color.Chrome.Hover}",
            GetStyleSetters(styles, "Button.chrome:pointerover")["Background"]);
        Assert.Equal(
            "{StaticResource Launcher.Color.Danger}",
            GetStyleSetters(styles, "Button.chrome.close:pointerover")["Background"]);
        Assert.Equal(
            "{StaticResource Launcher.Color.Danger.Pressed}",
            GetStyleSetters(styles, "Button.chrome.close:pressed")["Background"]);

        var focus = GetStyleSetters(styles, "Button:focus-visible");
        Assert.Equal("{DynamicResource Launcher.Color.FocusRing}", focus["BorderBrush"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Focus}", focus["BorderThickness"]);
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
            6,
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
            "news-row",
            "news-viewport",
            "operation-actions",
            "operation-layout",
            "operation-status",
            "primary-operation",
            "secondary-operation"
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
    public void MainWindow_RemoteContent_UsesIndependentCardsWithOuterVerticalScroll()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var remoteSurface = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "remote-surface"));
        var layoutHost = remoteSurface
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ScrollViewer"
                && HasClass(element, "remote-content-layout-host"));
        var cards = layoutHost
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "remote-content-card"))
            .ToArray();

        Assert.DoesNotContain(remoteSurface.Attributes(), attribute =>
            attribute.Name.LocalName == "Classes"
            && attribute.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("surface", StringComparer.Ordinal));
        Assert.Equal(2, cards.Length);
        Assert.All(
            new[] { "notice-card", "news-card" },
            cardClass => Assert.Single(cards, card => HasClass(card, cardClass)));
        Assert.DoesNotContain(cards, card => HasClass(card, "social-media-card"));
        var bannerStage = layoutHost
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "banner-stage"));
        Assert.DoesNotContain(bannerStage.Ancestors(), element => HasClass(element, "remote-content-card"));

        var layoutStack = layoutHost.Elements().Single(element => element.Name.LocalName == "StackPanel");
        Assert.DoesNotContain(layoutStack.Elements(), element => element.Name.LocalName == "ScrollViewer");

        var newsViewport = cards
            .Single(card => HasClass(card, "news-card"))
            .Descendants()
            .Single(element => HasClass(element, "news-viewport"));
        var newsCard = cards.Single(card => HasClass(card, "news-card"));
        var nestedScrollViewers = layoutHost
            .Descendants()
            .Where(element => element.Name.LocalName == "ScrollViewer")
            .ToArray();
        Assert.Single(nestedScrollViewers);
        Assert.Single(nestedScrollViewers, element => HasClass(element, "news-viewport"));
        Assert.Contains(newsCard.Descendants(), element => element == nestedScrollViewers.Single(e => HasClass(e, "news-viewport")));
        Assert.Equal("Auto", newsViewport.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("Disabled", newsViewport.Attribute("HorizontalScrollBarVisibility")?.Value);

        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var cardStyle = GetStyleSetters(styles, "Border.remote-content-card");
        Assert.Equal("{DynamicResource Launcher.Color.Panel.Background}", cardStyle["Background"]);
        Assert.Equal("{StaticResource Launcher.Radius.Sm}", cardStyle["CornerRadius"]);
        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.Md}", cardStyle["Padding"]);
        var layoutHostStyle = GetStyleSetters(styles, "ScrollViewer.remote-content-layout-host");
        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.Md}", layoutHostStyle["Padding"]);
        Assert.Equal("Auto", layoutHostStyle["VerticalScrollBarVisibility"]);
        Assert.Equal("Disabled", layoutHostStyle["HorizontalScrollBarVisibility"]);
    }

    [Fact]
    public void MainWindow_MultiRowGridChildren_DeclareTheirFirstRowExplicitly()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var expectedBindings = new[]
        {
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
    public void MainWindow_NewsTabs_UseNativeSelectionAndScrollableReadableRows()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var newsCard = document
            .Descendants()
            .Single(element => HasClass(element, "news-card"));
        var tabs = newsCard
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TabControl"
                && HasClass(element, "news-tabs"));
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

        Assert.Equal("{Binding RemoteContent.NewsCategories}", tabs.Attribute("ItemsSource")?.Value);
        Assert.Equal(
            "{Binding RemoteContent.SelectedNewsCategory, Mode=TwoWay}",
            tabs.Attribute("SelectedItem")?.Value);
        Assert.Equal("{x:Null}", tabs.Attribute("PageTransition")?.Value);
        var remoteStyles = XDocument.Load(ProjectFile("Views/Styles/RemoteContent.axaml"));
        var tabControlTheme = remoteStyles
            .Descendants()
            .Single(element => element.Name.LocalName == "ControlTheme"
                && element.Attributes().Any(attribute => attribute.Name.LocalName == "Key"
                    && attribute.Value == "Launcher.RemoteContent.NewsTabControlTheme"));
        var tabHeaderScrollViewer = tabControlTheme
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ScrollViewer"
                && element.Attribute("Classes") is null);
        Assert.Equal("Auto", tabHeaderScrollViewer.Attribute("HorizontalScrollBarVisibility")?.Value);
        Assert.Equal("Disabled", tabHeaderScrollViewer.Attribute("VerticalScrollBarVisibility")?.Value);
        var tabItemTheme = remoteStyles
            .Descendants()
            .Single(element => element.Name.LocalName == "ControlTheme"
                && element.Attributes().Any(attribute => attribute.Name.LocalName == "Key"
                    && attribute.Value == "Launcher.RemoteContent.NewsTabItemTheme"));
        Assert.Equal("TabItem", tabItemTheme.Attribute("TargetType")?.Value);
        Assert.Equal("{StaticResource {x:Type TabItem}}", tabItemTheme.Attribute("BasedOn")?.Value);
        Assert.Contains(
            tabItemTheme.Elements(),
            element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "^:selected");
        var tabTheme = tabControlTheme;
        var tabItemsPanel = tabTheme
            .Descendants()
            .Single(element => element.Name.LocalName == "StackPanel");
        Assert.Equal("Horizontal", tabItemsPanel.Attribute("Orientation")?.Value);
        Assert.Equal("{StaticResource Launcher.Spacing.Xs}", tabItemsPanel.Attribute("Spacing")?.Value);
        var tabItemSetters = tabItemTheme
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => element.Attribute("Property")?.Value ?? "",
                element => element.Attribute("Value")?.Value ?? "",
                StringComparer.Ordinal);
        Assert.Equal("{StaticResource Launcher.Color.Transparent}", tabItemSetters["Background"]);
        Assert.Equal(
            "{StaticResource Launcher.Color.Transparent}",
            tabItemTheme
                .Elements()
                .Single(element => element.Name.LocalName == "Style" && element.Attribute("Selector")?.Value == "^:selected")
                .Elements()
                .Single(element => element.Name.LocalName == "Setter" && element.Attribute("Property")?.Value == "Background")
                .Attribute("Value")?.Value);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Primary}",
            tabItemTheme
                .Elements()
                .Single(element => element.Name.LocalName == "Style" && element.Attribute("Selector")?.Value == "^:selected /template/ Border#PART_SelectedPipe")
                .Elements()
                .Single(element => element.Name.LocalName == "Setter")
                .Attribute("Value")?.Value);
        Assert.Equal(
            "{StaticResource Launcher.Component.Tabs.Indicator.Margin}",
            tabItemTheme
                .Elements()
                .Single(element => element.Name.LocalName == "Style" && element.Attribute("Selector")?.Value == "^[TabStripPlacement=Top] /template/ Border#PART_SelectedPipe")
                .Elements()
                .Single(element => element.Name.LocalName == "Setter")
                .Attribute("Value")?.Value);
        var appResources = XDocument.Load(ProjectFile("App.axaml"));
        var tabIndicatorMargin = appResources
            .Descendants()
            .Single(element => element.Name.LocalName == "Thickness"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "Launcher.Component.Tabs.Indicator.Margin"));
        Assert.Equal("0,4,0,2", tabIndicatorMargin.Value);
        var tabHeaderMargin = appResources
            .Descendants()
            .Single(element => element.Name.LocalName == "Thickness"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "Launcher.Component.Tabs.Header.Margin"));
        Assert.Equal("0,0,0,4", tabHeaderMargin.Value);
        Assert.DoesNotContain(newsCard.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "{Binding Shell.I18n[news]}");
        Assert.DoesNotContain(newsCard.Descendants(), element =>
            element.Name.LocalName == "Button"
            && HasClass(element, "news-tab"));
        Assert.Null(viewport.Attribute("Height"));
        Assert.Equal("{StaticResource Launcher.Layout.News.Viewport.Height}", viewport.Attribute("MaxHeight")?.Value);
        Assert.Equal("Auto", viewport.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("Disabled", viewport.Attribute("HorizontalScrollBarVisibility")?.Value);
        Assert.Equal("StackPanel", itemsPanel.Name.LocalName);
        Assert.Equal("{StaticResource Launcher.Spacing.Sm}", itemsPanel.Attribute("Spacing")?.Value);
        Assert.Null(rowButton.Attribute("Height"));
        Assert.Equal("{StaticResource Launcher.Control.Height.Setting}", rowButton.Attribute("MinHeight")?.Value);
        Assert.True(HasClass(rowButton, "content-link"));
        Assert.True(HasClass(rowBorder, "content-row"));
        Assert.True(HasClass(rowBorder, "news-content-row"));
        Assert.Equal("1", title.Attribute("MaxLines")?.Value);
        Assert.Equal("NoWrap", title.Attribute("TextWrapping")?.Value);
        Assert.Equal("CharacterEllipsis", title.Attribute("TextTrimming")?.Value);
        Assert.Equal("{Binding Title}", rowButton.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal("Right", date.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Right", date.Attribute("TextAlignment")?.Value);

        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var newsContentRow = GetStyleSetters(styles, "Border.content-row.news-content-row");
        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.Sm}", newsContentRow["Padding"]);
        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.None}", newsContentRow["Margin"]);
    }

    [Fact]
    public void MainWindow_CarouselNavigation_UsesTokenizedHitTargetsAndLocalizedNames()
    {
        var view = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        Dictionary<string, string> expectedNames = new(StringComparer.Ordinal)
        {
            ["{Binding RemoteContent.SelectPreviousBannerCommand}"] = "{Binding Shell.I18n[previousBanner]}",
            ["{Binding RemoteContent.SelectNextBannerCommand}"] = "{Binding Shell.I18n[nextBanner]}"
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
            Assert.Equal("{StaticResource Launcher.Icon.Xxl}", icon.Attribute("Width")?.Value);
            Assert.Equal("{StaticResource Launcher.Icon.Xxl}", icon.Attribute("Height")?.Value);
        }

        var navigation = GetStyleSetters(styles, "Button.icon-button.carousel-navigation");
        Assert.Equal("{StaticResource Launcher.Control.Height.Setting}", navigation["Width"]);
        Assert.Equal("{StaticResource Launcher.Control.Height.Setting}", navigation["Height"]);
        // 箭头压在壁纸上，hover/pressed 反馈走 chrome 态层，
        // 图标前景保持 OnChrome 豁免（spec §8）。
        Assert.Equal(
            "{StaticResource Launcher.Color.Chrome.Hover}",
            GetStyleSetters(styles, "Button.icon-button.carousel-navigation:pointerover")["Background"]);
        Assert.Equal(
            "{StaticResource Launcher.Color.Chrome.Pressed}",
            GetStyleSetters(styles, "Button.icon-button.carousel-navigation:pressed")["Background"]);
    }

    [Fact]
    public void MainWindow_CarouselControls_OverlayNavigationWithoutPageText()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var bannerStage = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "banner-stage"));
        var bannerFrame = bannerStage.Parent!;
        Assert.Equal("Border", bannerFrame.Name.LocalName);
        Assert.True(HasClass(bannerFrame, "banner-frame"));
        var navigationButtons = bannerStage.Descendants().Where(element =>
            element.Name.LocalName == "Button"
            && HasClass(element, "carousel-navigation"))
            .ToArray();
        var dots = bannerStage.Descendants().Where(element =>
            element.Name.LocalName == "Border"
            && HasClass(element, "banner-dot"))
            .ToArray();
        var bannerLink = bannerStage.Descendants().Single(element =>
            element.Name.LocalName == "Button"
            && HasClass(element, "banner-link"));

        Assert.DoesNotContain(
            bannerStage.Descendants(),
            element => HasClass(element, "banner-page-indicator"));
        var edgeGradients = bannerStage.Descendants().Where(element =>
            element.Name.LocalName == "Border"
            && HasClass(element, "banner-edge-gradient"))
            .ToArray();
        Assert.Equal(
            "{Binding #Root.((vm:MainWindowViewModel)DataContext).Shell.I18n[banner]}",
            bannerLink.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal(2, navigationButtons.Length);
        Assert.Equal(2, edgeGradients.Length);
        Assert.All(
            edgeGradients,
            gradient => Assert.Contains("banner-edge-gradient", gradient.Attribute("Classes")?.Value, StringComparison.Ordinal));
        Assert.All(navigationButtons, button =>
        {
            Assert.Equal("{Binding RemoteContent.HasMultipleBanners}", button.Attribute("IsEnabled")?.Value);
            Assert.NotNull(button.Attribute("ToolTip.Tip"));
            Assert.NotNull(button.Attribute("AutomationProperties.Name"));
        });
        Assert.NotEmpty(dots);
        Assert.DoesNotContain(
            bannerStage.Descendants(),
            element => element.Name.LocalName == "Button" && HasClass(element, "dot"));
        Assert.DoesNotContain(
            bannerStage.Descendants(),
            element => element.Attributes().Any(attribute =>
                attribute.Value.Contains("SelectBannerCommand", StringComparison.Ordinal)));
        var bannerIndicators = bannerStage.Descendants().Single(element =>
            element.Name.LocalName == "Grid" && HasClass(element, "banner-indicators"));
        Assert.Equal("False", bannerIndicators.Attribute("IsHitTestVisible")?.Value);
        Assert.Equal(
            "{StaticResource Launcher.Component.Banner.Indicator.Margin}",
            bannerIndicators.Attribute("Margin")?.Value);
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Attributes().Any(attribute =>
                attribute.Value.Contains("ToggleCarouselLoop", StringComparison.Ordinal)
                || attribute.Value.Contains("CarouselPause", StringComparison.Ordinal)));

        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var bannerLinkStyle = GetStyleSetters(styles, "Button.banner-link");
        Assert.Equal("{StaticResource Launcher.Color.Transparent}", bannerLinkStyle["Background"]);
        Assert.DoesNotContain(
            styles.Descendants(),
            element => element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value is "Button.banner-link:pointerover" or "Button.banner-link:pressed");
        var bannerFrameStyle = GetStyleSetters(styles, "Border.banner-frame");
        Assert.Equal("{StaticResource Launcher.Radius.Sm}", bannerFrameStyle["CornerRadius"]);
        Assert.Equal("True", bannerFrameStyle["ClipToBounds"]);
        var bannerControl = GetStyleSetters(styles, "Button.banner-control");
        Assert.Equal("{StaticResource Launcher.Color.Transparent}", bannerControl["Background"]);
        Assert.Equal("{StaticResource Launcher.Text.OnChrome}", bannerControl["Foreground"]);
        Assert.Equal("0", bannerControl["Opacity"]);
        Assert.Equal("False", bannerControl["IsHitTestVisible"]);
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Thickness.Sm}",
            GetStyleSetters(styles, "Button.banner-control.carousel-navigation")["Margin"]);
        Assert.Equal(
            "{StaticResource Launcher.Layout.Banner.EdgeGradient.Width}",
            GetStyleSetters(styles, "Border.banner-edge-gradient")["Width"]);
        Assert.Equal(
            "{StaticResource Launcher.Radius.Sm}",
            GetStyleSetters(styles, "Border.banner-edge-gradient")["CornerRadius"]);
        Assert.Equal("0", GetStyleSetters(styles, "Border.banner-edge-gradient")["Opacity"]);
        Assert.Equal(
            "1",
            GetStyleSetters(styles, "Grid.banner-stage.active > Border.banner-edge-gradient")["Opacity"]);
        var remoteContentStyles = XDocument.Load(ProjectFile("Views/Styles/RemoteContent.axaml"));
        var bannerDots = GetStyleSetters(remoteContentStyles, "Border.banner-dot");
        Assert.Equal("{DynamicResource Launcher.Color.Carousel.Dot.Inactive}", bannerDots["Background"]);
        Assert.Equal("{StaticResource Launcher.Spacing.Xs}", bannerDots["Width"]);
        Assert.Equal("{StaticResource Launcher.Spacing.Xs}", bannerDots["Height"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.Banner.Indicator.CornerRadius}",
            bannerDots["CornerRadius"]);
        var activeBannerDot = GetStyleSetters(remoteContentStyles, "Border.banner-dot.active");
        Assert.Equal("{DynamicResource Launcher.Color.Carousel.Dot.Active}", activeBannerDot["Background"]);
        Assert.Equal("{StaticResource Launcher.Spacing.Md}", activeBannerDot["Width"]);
        Assert.Equal("1", GetStyleSetters(styles, "Grid.banner-stage.active > Button.banner-control")["Opacity"]);
        Assert.Equal("True", GetStyleSetters(styles, "Grid.banner-stage.active > Button.banner-control")["IsHitTestVisible"]);
        Assert.Equal("True", GetStyleSetters(styles, "Grid.banner-stage.active > Grid.banner-control")["IsHitTestVisible"]);
        Assert.Equal("False", GetStyleSetters(styles, "Grid.banner-stage.active > TextBlock.banner-control")["IsHitTestVisible"]);
        Assert.Equal(
            "{StaticResource Launcher.StateLayer.Disabled.Content}",
            GetStyleSetters(styles, "Grid.banner-stage.active > Button.banner-control:disabled")["Opacity"]);
    }

    [Fact]
    public void MainWindow_IsResizableWithMinimumViewportConstraints()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var window = document.Root;

        Assert.NotNull(window);
        Assert.Equal("True", window.Attribute("CanResize")?.Value);
        Assert.Equal("{StaticResource Launcher.Layout.Window.MinWidth}", window.Attribute("MinWidth")?.Value);
        Assert.Equal("{StaticResource Launcher.Layout.Window.MinHeight}", window.Attribute("MinHeight")?.Value);
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
    public void MainWindow_SocialActions_AddTopRightVerticalIconButtonsAndUseRemoteContentVisibilitySetting()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var actions = document.Descendants().Single(element =>
            element.Name.LocalName == "StackPanel" && HasClass(element, "social-actions"));
        var officialSiteButton = actions.Elements().Single(element =>
            element.Name.LocalName == "Button" && HasClass(element, "official-site"));
        var items = actions.Elements().Single(element => element.Name.LocalName == "ItemsControl");
        var actionButton = items.Descendants().Single(element =>
            element.Name.LocalName == "Button" && HasClass(element, "social-action"));
        var layoutPanel = document.Descendants().Single(element =>
            element.Name.LocalName == "Panel"
            && element.Elements().Any(child => child.Name.LocalName == "Border" && HasClass(child, "remote-surface"))
            && element.Elements().Any(child => child.Name.LocalName == "StackPanel" && HasClass(child, "social-actions")));

        Assert.Contains(layoutPanel.Elements(), element =>
            element.Name.LocalName == "Border" && HasClass(element, "remote-surface"));
        Assert.Equal("{Binding RemoteContent.HasRemoteContent}", items.Attribute("IsVisible")?.Value);
        Assert.Equal("{Binding RemoteContent.SocialMediaItems}", items.Attribute("ItemsSource")?.Value);
        Assert.Equal("Vertical", items.Descendants().Single(element => element.Name.LocalName == "StackPanel").Attribute("Orientation")?.Value);
        Assert.True(HasClass(actionButton, "social-chip"));
        Assert.Equal("{Binding Title}", actionButton.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal("{Binding Title}", actionButton.Attribute("AutomationProperties.Name")?.Value);
        Assert.DoesNotContain(actionButton.Descendants(), element => element.Name.LocalName == "TextBlock");

        Assert.True(HasClass(officialSiteButton, "social-chip"));
        Assert.True(HasClass(officialSiteButton, "social-action"));
        Assert.Equal(
            "{Binding RemoteContent.HasRemoteContent}",
            officialSiteButton.Attribute("IsVisible")?.Value);
        Assert.Equal(
            "{Binding Shell.I18n[officialSite]}",
            officialSiteButton.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal(
            "{Binding Shell.I18n[officialSite]}",
            officialSiteButton.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal(
            "{Binding WindowChrome.OpenOfficialSiteCommand}",
            officialSiteButton.Attribute("Command")?.Value);
        Assert.DoesNotContain(officialSiteButton.Descendants(), element => element.Name.LocalName == "TextBlock");
        Assert.Equal("Web",
            officialSiteButton.Descendants().Single(element => element.Name.LocalName == "MaterialIcon").Attribute("Kind")?.Value);
        Assert.Equal(
            "Button",
            actions.Elements().First().Name.LocalName);
        Assert.True(HasClass(actions.Elements().First(), "official-site"));

        var mainStyles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var remoteStyles = XDocument.Load(ProjectFile("Views/Styles/RemoteContent.axaml"));
        var actionsStyle = GetStyleSetters(mainStyles, "StackPanel.social-actions");
        var actionButtonStyle = GetStyleSetters(remoteStyles, "Button.social-chip.social-action");
        Assert.Equal("Right", actionsStyle["HorizontalAlignment"]);
        Assert.Equal("Top", actionsStyle["VerticalAlignment"]);
        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.Xl}", actionsStyle["Margin"]);
        Assert.Equal("{StaticResource Launcher.Control.Height.Setting}", actionButtonStyle["Width"]);
        Assert.Equal("{StaticResource Launcher.Control.Height.Setting}", actionButtonStyle["Height"]);
        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.None}", actionButtonStyle["Padding"]);
    }
}
