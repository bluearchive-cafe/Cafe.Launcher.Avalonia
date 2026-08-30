using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

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
        // 箭头压在壁纸上，hover/pressed 反馈走 chrome 态层（与 banner-link 同配方），
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
                "Settings.Editor.Current.ShowRemoteContentCard",
                "Settings.Appearance.ThemeColorPaletteItems",
                "Settings.Appearance.SelectedCustomThemeColor",
                "Settings.Appearance.SelectedBackgroundFillColor",
                "Settings.ChooseBackgroundImageCommand",
                "Settings.ChooseBackgroundFolderCommand",
                "Settings.ClearBackgroundCommand",
                "Settings.Appearance.IsWallpaperThemeColorSelected",
                "Settings.Appearance.IsThemeColorExtractionAlgorithmVisible",
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
                "WindowChrome.OpenGitHubRepositoryCommand",
                "WindowChrome.OpenIssueTrackerCommand",
                "WindowChrome.OpenPrivacyPolicyCommand",
                "WindowChrome.OpenDefaultBackgroundArtworkCommand"
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
        var document = XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
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
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var dialogSurfaceStyles = XDocument.Load(ProjectFile("Views/Styles/DialogSurface.axaml"));

        Assert.Equal(
            "0",
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
            "0",
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

        var application = XDocument.Load(ProjectFile("App.axaml"));
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

        var overlayDocument = XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
        var scrollViewer = overlayDocument
            .Descendants()
            .Single(element => element.Name.LocalName == "ScrollViewer");
        Assert.True(HasClass(scrollViewer, "settings-content-scroll"));
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
        Assert.Equal(
            "{Binding Shell.I18n[settingsGroupDisplay]}",
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

        Assert.Equal(
            "{StaticResource Launcher.Layout.Appearance.Preview.Width}",
            palette.Attribute("Width")?.Value);
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
    public void AboutSection_LegalLinks_AreInlineHyperlinks()
    {
        var document = XDocument.Load(ProjectFile("Views/SettingsAboutSection.axaml"));
        var links = document
            .Descendants()
            .Where(element => element.Name.LocalName == "HyperlinkButton")
            .ToList();

        Assert.Equal(2, links.Count);
        Assert.All(links, link =>
        {
            Assert.True(HasClass(link, "inline-legal-link"));
            Assert.False(HasClass(link, "text-link"));
            Assert.Equal("InlineUIContainer", link.Parent?.Name.LocalName);
        });
        Assert.Equal(
            [
                "{Binding WindowChrome.OpenPrivacyPolicyCommand}",
                "{Binding WindowChrome.OpenDefaultBackgroundArtworkCommand}"
            ],
            links.Select(link => link.Attribute("Command")?.Value));
    }

    [Fact]
    public void AboutSection_RendersIdentityCardAndKeyValueRows()
    {
        var document = XDocument.Load(ProjectFile("Views/SettingsAboutSection.axaml"));

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

    private static readonly HashSet<string> IconTokens =
    [
        "{StaticResource Launcher.Icon.Sm}",
        "{StaticResource Launcher.Icon.Md}",
        "{StaticResource Launcher.Icon.Lg}",
        "{StaticResource Launcher.Icon.Xl}",
        "{StaticResource Launcher.Icon.Xxl}"
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
    public void SetupWizardStyles_CoverFilledDisabledAndOptionFocusStates()
    {
        // 2026-08-28 审计修复：wizard filled 型禁用态组合须声明在 primary 之后，
        // 与主样式 filled 型家族（primary-action.dialog-action:disabled 等）同一预混配方；
        // 选项行（wizard-option）键盘聚焦时铺 token 焦点环（spec §8）。
        var wizardStyles = XDocument.Load(ProjectFile("Views/Styles/SetupWizard.axaml"));

        var primaryDisabled = GetStyleSetters(wizardStyles, "Button.wizard-action.primary-action:disabled");
        Assert.Equal("{DynamicResource Launcher.Color.Content.Row}", primaryDisabled["Background"]);
        Assert.Equal("{DynamicResource Launcher.Color.Card.Border}", primaryDisabled["BorderBrush"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Default}", primaryDisabled["BorderThickness"]);
        Assert.Equal("{DynamicResource Launcher.Text.Secondary}", primaryDisabled["Foreground"]);

        var optionFocus = GetStyleSetters(wizardStyles, "RadioButton.wizard-option:focus-visible");
        Assert.Equal("{DynamicResource Launcher.Color.FocusRing}", optionFocus["BorderBrush"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Focus}", optionFocus["BorderThickness"]);
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

        foreach (var family in Cafe.Launcher.Avalonia.ViewModels.DesignTokenGrouping.FamilyOrder)
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
    public void SettingsNavigationAndUpdateFileItems_TokenizeFocusVisibleRings() // spec §8 visible focus ring
    {
        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        foreach (var selector in new[]
                 {
                     "ListBox.settings-navigation > ListBoxItem:focus-visible",
                     "ListBox.update-file-list > ListBoxItem:focus-visible"
                 })
        {
            Assert.Equal(
                "{DynamicResource Launcher.Color.FocusRing}",
                GetStyleSetters(styles, selector)["BorderBrush"]);
            Assert.Equal(
                "{StaticResource Launcher.Border.Thickness.Focus}",
                GetStyleSetters(styles, selector)["BorderThickness"]);
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
                "{DynamicResource Launcher.Color.Card.Background}",
                setters["Background"]);
            Assert.Equal(
                "{DynamicResource Launcher.Text.Primary}",
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
    public void SetupWizardOverlay_UsesProgressRowAndContentActions()
    {
        var document = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));

        // 实验台解剖（ADR-017）：无侧栏导航；进度行承载向导标题与步骤进度，跳过钮居右。
        Assert.DoesNotContain(
            document.Descendants(),
            element => HasClass(element, "settings-navigation-pane"));
        var skipButton = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding Dialogs.RequestSetupWizardExitCommand}");
        var progressRow = skipButton.Parent!;
        Assert.Equal("Grid", progressRow.Name.LocalName);
        var heading = progressRow
            .Elements()
            .First(element => element.Name.LocalName == "StackPanel");
        var title = heading
            .Elements()
            .Single(element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding Shell.I18n[setupWizardStepTitle]}");
        Assert.True(HasClass(title, "dialog-title"));
        // 步骤进度由后置代码在换面中点翻转（ApplyChromeState），不得绑定 Step 先于内容跳变。
        var progressXNamespace = document.Root?.GetNamespaceOfPrefix("x");
        Assert.NotNull(progressXNamespace);
        var progressText = Assert.Single(heading.Elements(), element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute(progressXNamespace + "Name")?.Value == "StepProgressText");
        Assert.True(HasClass(progressText, "caption"));
        Assert.Null(progressText.Attribute("Text"));

        // 外壳模式自管留白：wizard-body 消费 Panel 正文内边距（Padding 禁止内联于视图）。
        var paddingBorder = skipButton.Ancestors().Single(element =>
            element.Name.LocalName == "Border"
            && HasClass(element, "wizard-body"));
        var contentBlock = paddingBorder
            .Elements()
            .Single(element => element.Name.LocalName == "Grid");
        Assert.Equal("Auto,*,Auto", contentBlock.Attribute("RowDefinitions")?.Value);
        var wizardStyles = XDocument.Load(ProjectFile("Views/Styles/SetupWizard.axaml"));
        Assert.Equal(
            "{StaticResource Launcher.Component.Dialog.Panel.Body.Padding}",
            GetStyleSetters(wizardStyles, "Border.wizard-body")["Padding"]);

        // 动作带并入内容区：不再使用 DialogSurface.Footer（空动作带会残留发丝线）。
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Name.LocalName == "DialogSurface.Footer");
        var actionsRow = contentBlock
            .Elements()
            .Last(element => element.Name.LocalName == "Grid");
        Assert.All(
            actionsRow.Descendants().Where(element => element.Name.LocalName == "Button"),
            button => Assert.True(HasClass(button, "wizard-action")));
        Assert.Contains(actionsRow.Descendants(), element =>
            element.Name.LocalName == "Button"
            && !HasClass(element, "primary-action")
            && element.Attribute("Command")?.Value == "{Binding Dialogs.SetupWizard.PreviousCommand}");
        Assert.Contains(actionsRow.Descendants(), element =>
            element.Name.LocalName == "Button"
            && HasClass(element, "primary-action")
            && element.Attribute("Command")?.Value == "{Binding Dialogs.SetupWizard.NextCommand}");
        Assert.Contains(actionsRow.Descendants(), element =>
            element.Name.LocalName == "Button"
            && HasClass(element, "primary-action")
            && element.Attribute("Command")?.Value == "{Binding Dialogs.SetupWizard.CompleteCommand}");
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
                // 头带 ✕ 迁入模板后经 CloseAutomationName 传递，不再出现在文件中。
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
    }

    [Fact]
    public void ErrorDialog_HeaderProvidesLocalizedCloseAction() // ADR-015 dialog surface anatomy
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowDialogsOverlay.axaml"));
        var errorSurface = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "DialogSurface"
                && element.Attribute("Status")?.Value == "Danger");

        // 头带 ✕ 由模板渲染，命令与本地化名称经表面属性传入。
        Assert.Equal(
            "{Binding Dialogs.ContinueAfterErrorCommand}",
            errorSurface.Attribute("CloseCommand")?.Value);
        Assert.Equal(
            "{Binding Shell.I18n[close]}",
            errorSurface.Attribute("CloseAutomationName")?.Value);
        Assert.Equal(
            "{Binding Shell.I18n[close]}",
            errorSurface.Attribute("CloseToolTip")?.Value);
    }

    [Fact]
    public void AppearanceSection_NeutralStrategyHint_IsLocalizedAndConditionallyVisible() // ADR-010
    {
        var document = XDocument.Load(ProjectFile("Views/SettingsAppearanceSection.axaml"));
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
    public void AppDialogSurfaceTokens_MatchGeneratorDeclaredDefaults() // ADR-010
    {
        // The Brand Blue neutral strategy resets the dialog surface family to the
        // values declared here; this pin keeps the XAML and the reset table from
        // drifting apart.
        var document = XDocument.Load(ProjectFile("App.axaml"));
        foreach (var (key, light, dark) in MaterialSchemeGenerator.DialogSurfaceDefaults)
        {
            Assert.Equal(light, ReadThemeBrushColor(document, "Light", key));
            Assert.Equal(dark, ReadThemeBrushColor(document, "Dark", key));
        }
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
        var application = XDocument.Load(ProjectFile("App.axaml"));
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
    public void ConfirmDialog_LongContentScrollsWhileActionsRemainFixed()
    {
        var document = XDocument.Load(ProjectFile("Controls/ConfirmDialog.axaml"));
        var surface = document
            .Descendants()
            .Single(element => element.Name.LocalName == "DialogSurface");

        // Basic 形态归约到 DialogSurface；尺寸由 Confirm token 家族背书。
        Assert.Equal("Basic", surface.Attribute("Form")?.Value);
        Assert.Equal(
            "{StaticResource Launcher.Component.Dialog.Confirm.MaxHeight}",
            surface.Attribute("MaxHeight")?.Value);
        Assert.Equal(
            "{StaticResource Launcher.Component.Dialog.Confirm.MinWidth}",
            surface.Attribute("MinWidth")?.Value);
        Assert.Equal("Center", surface.Attribute("VerticalAlignment")?.Value);
        Assert.Null(surface.Attribute("Subtitle"));

        // Basic 动作带绝不出现 hairline footer；三按钮规律保留。
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Name.LocalName == "Border" && HasClass(element, "dialog-footer"));

        var application = XDocument.Load(ProjectFile("App.axaml"));
        var maxHeightToken = application
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key"
                && attribute.Value == "Launcher.Component.Dialog.Confirm.MaxHeight"));
        Assert.Equal("480", maxHeightToken.Value);

        var actions = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "StackPanel" && HasClass(element, "confirm-actions"));
        Assert.Equal(3, actions.Elements().Count(element => element.Name.LocalName == "Button"));
    }

    [Fact]
    public void DialogsOverlay_DialogsUseHairlineFooterForActions()
    {
        // ADR-015：发丝动作带内化为 DialogSurface Panel 模板；视图文件只承载
        // 四个表面实例（三 Panel + 一 Basic 公告），辅助动作进左槽。
        var text = File.ReadAllText(ProjectFile("Views/MainWindowDialogsOverlay.axaml"));

        Assert.Equal(3, Regex.Count(text, @"Form=""Panel""", RegexOptions.CultureInvariant));
        Assert.Equal(1, Regex.Count(text, @"Form=""Basic""", RegexOptions.CultureInvariant));
        Assert.Equal(4, Regex.Count(text, @"Classes=""motion-surface""", RegexOptions.CultureInvariant));

        // 发丝底带不再由调用方摆放：文件里不允许残留 legacy footer 标记。
        Assert.DoesNotContain("dialog-footer", text, StringComparison.Ordinal);

        var panels = 0;
        foreach (Match match in Regex.Matches(text, @"<controls:DialogSurface\b[^>]*>", RegexOptions.CultureInvariant))
        {
            if (match.Value.Contains(@"Form=""Panel""", StringComparison.Ordinal))
            {
                panels++;
            }
        }

        Assert.Equal(3, panels);
        Assert.Matches(
            """(?s)<controls:DialogSurface\b[^>]*Form="Panel"[^>]*>.*?<controls:DialogSurface\.FooterLeading>.*?</controls:DialogSurface\.FooterLeading>.*?</controls:DialogSurface>""",
            text);
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
            ["{Binding WindowChrome.ShowSettingsCommand}"] = "{Binding Shell.I18n[cancel]}",
            ["{Binding Settings.SaveSettingsCommand}"] = "{Binding Shell.I18n[save]}"
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
                    == "{DynamicResource Launcher.Text.Secondary}");
    }

    [Fact]
    public void LogViewer_UsesFixedDialogDimensions()
    {
        // ADR-015 尺寸律：自适应优先，固定宽高退场；token 仅作 Max 上限背书。
        var document = XDocument.Load(ProjectFile("Views/MainWindowLogViewerOverlay.axaml"));
        var dialog = document
            .Descendants()
            .Single(element => element.Name.LocalName == "DialogSurface");

        Assert.Equal(
            "{StaticResource Launcher.Layout.LogViewer.Width}",
            dialog.Attribute("MaxWidth")?.Value);
        Assert.Equal(
            "{StaticResource Launcher.Layout.LogViewer.Height}",
            dialog.Attribute("MaxHeight")?.Value);
        Assert.Null(dialog.Attribute("Width"));
        Assert.Null(dialog.Attribute("Height"));

        // 过滤栏固定于头带之下：工具行插槽必须由 Toolbar 属性承载。
        Assert.Contains(
            dialog.Elements(),
            element => element.Name.LocalName == "DialogSurface.Toolbar");
    }

    [Fact]
    public void LocalizationManagement_UsesFixedDialogDimensions()
    {
        // ADR-015 尺寸律：自适应优先，固定宽高退场；token 仅作 Max 上限背书。
        var document = XDocument.Load(ProjectFile("Views/MainWindowDialogsOverlay.axaml"));
        var dialog = FindMotionOverlay(
                document,
                "{Binding ResourcePanel.IsResourcePanelVisible}")
            .Elements()
            .Single(element => element.Name.LocalName == "DialogSurface");

        Assert.Equal("{StaticResource Launcher.Layout.ResourcePanel.Width}", dialog.Attribute("MaxWidth")?.Value);
        Assert.Equal("{StaticResource Launcher.Layout.ResourcePanel.Height}", dialog.Attribute("MaxHeight")?.Value);
        Assert.Null(dialog.Attribute("Width"));
        Assert.Null(dialog.Attribute("Height"));
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
        Assert.Equal("1", GetStyleSetters(document, "Button.toast-close")["Opacity"]);
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
    public void RemoteContentPanel_UsesExplicitLoadingState()
    {
        var mainWindow = File.ReadAllText(ProjectFile("Views/MainWindow.axaml"));
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var app = XDocument.Load(ProjectFile("App.axaml"));

        Assert.Contains(
            "IsVisible=\"{Binding RemoteContent.IsPanelVisible}\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsVisible=\"{Binding RemoteContent.IsLoading}\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Shell.I18n[remoteContentLoading]",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsVisible=\"{Binding RemoteContent.HasLoadError}\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Shell.I18n[remoteContentLoadFailed]",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Equal("True", GetStyleSetters(styles, "Border.remote-surface")["ClipToBounds"]);
        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.None}", GetStyleSetters(styles, "Border.remote-surface")["Padding"]);
        Assert.Equal("#99000000", app.Descendants().Single(element =>
            element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "Launcher.Color.Overlay.Scrim.Md").Attribute("Color")?.Value);

        var remoteSurface = document.Descendants().Single(element =>
            element.Name.LocalName == "Border" && HasClass(element, "remote-surface"));
        var panel = remoteSurface.Elements().Single(element => element.Name.LocalName == "Panel");
        Assert.Single(panel.Elements(), element =>
            element.Name.LocalName == "ScrollViewer" && HasClass(element, "remote-content-layout-host"));
        Assert.Single(panel.Elements(), element => element.Name.LocalName == "Border");
        Assert.Single(panel.Elements(), element => element.Name.LocalName == "LoadingOverlay");
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
    public void SetupWizard_UsesConstrainedSingleColumnWorkspace()
    {
        var document = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
        var xNamespace = document.Root?.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNamespace);
        var dialog = document
            .Descendants()
            .Single(element => element.Name.LocalName == "DialogSurface");
        Assert.Null(dialog.Attribute("Width"));
        Assert.Null(dialog.Attribute("Height"));
        Assert.Equal("{StaticResource Launcher.Layout.SetupWizard.Width}", dialog.Attribute("MaxWidth")?.Value);
        Assert.Equal("{StaticResource Launcher.Layout.SetupWizard.Height}", dialog.Attribute("MaxHeight")?.Value);
        Assert.Equal("Stretch", dialog.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Stretch", dialog.Attribute("VerticalAlignment")?.Value);
        // 无头带折叠：向导自绘进度行，跳过钮保留文字语义。
        Assert.Null(dialog.Attribute("Title"));

        // 实验台解剖：居中单列内容，不再有侧栏导航列表。
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Name.LocalName == "ListBox");

        var container = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ScrollViewer"
                && element.Attribute(xNamespace + "Name")?.Value == "StepScroll")
            .Elements()
            .Single(element => element.Name.LocalName == "Grid");
        Assert.Equal(
            "{Binding $parent[ScrollViewer].Viewport.Height}",
            container.Attribute("MinHeight")?.Value);

        var steps = container
            .Elements()
            .Where(element => element.Name.LocalName == "StackPanel" && HasClass(element, "wizard-step"))
            .ToList();
        Assert.Equal(5, steps.Count);
        Assert.Equal(
            new[] { "WizardStep0", "WizardStep1", "WizardStep2", "WizardStep3", "WizardStep4" },
            steps.Select(step => step.Attribute(xNamespace + "Name")?.Value));
        Assert.All(steps, step =>
        {
            Assert.Equal("Center", step.Attribute("HorizontalAlignment")?.Value);
            Assert.Equal("Center", step.Attribute("VerticalAlignment")?.Value);
            Assert.Equal(
                "{StaticResource Launcher.Component.Wizard.Content.MaxWidth}",
                step.Attribute("MaxWidth")?.Value);
        });
    }

    [Fact]
    public void SetupWizard_StepsSwitchThroughSequentialFadeSwap()
    {
        // ADR-017：步骤切换 = 旧内容先淡出、新内容按方向滑入的顺序换页，由后置代码编排；
        // 步骤面板不走 MotionVisibility 与声明式动画类，可见性完全由后置代码接管。
        var document = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
        var xNamespace = document.Root?.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNamespace);
        var controls = document.Root?.GetNamespaceOfPrefix("controls");
        Assert.NotNull(controls);
        var steps = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "StackPanel"
                && HasClass(element, "wizard-step"))
            .ToList();
        Assert.Equal(5, steps.Count);

        foreach (var step in steps)
        {
            Assert.Equal("False", step.Attribute("IsVisible")?.Value);
            AssertHasLocalTranslateTransform(step);
            Assert.Null(step.Attribute(controls + "MotionVisibility.IsOpen"));
            Assert.Null(step.Attribute(controls + "MotionVisibility.IsMotionEnabled"));
            Assert.Null(step.Attribute("Classes.motion-enter"));
            Assert.Null(step.Attribute("Classes.motion-enabled"));
            Assert.Null(step.Attribute("Classes.motion-forward"));
            Assert.Null(step.Attribute("Classes.motion-backward"));
        }

        // 换面容器为单格 Grid（任意时刻仅一个步骤面板可见），内容列随视口垂直居中。
        var container = Assert.Single(
            steps
                .Select(step => step.Parent)
                .Cast<XElement>()
                .Distinct());
        Assert.Equal("Grid", container.Name.LocalName);
        Assert.Null(container.Attribute("RowDefinitions"));
        Assert.Null(container.Attribute("ColumnDefinitions"));
        Assert.Equal(
            "{Binding $parent[ScrollViewer].Viewport.Height}",
            container.Attribute("MinHeight")?.Value);

        // 共享 ScrollViewer 由后置代码在换面时复位滚动。
        Assert.Contains(
            document.Descendants(),
            element => element.Name.LocalName == "ScrollViewer"
                && element.Attribute(xNamespace + "Name")?.Value == "StepScroll");
    }

    [Fact]
    public void SetupWizardHeader_ShowsWizardTitleAndProgress()
    {
        var document = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
        var skipButton = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding Dialogs.RequestSetupWizardExitCommand}");
        var heading = skipButton.Parent!
            .Elements()
            .First(element => element.Name.LocalName == "StackPanel");
        var title = heading
            .Elements()
            .Single(element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding Shell.I18n[setupWizardStepTitle]}");
        Assert.True(HasClass(title, "dialog-title"));
        // 步骤进度由后置代码在换面中点翻转（ApplyChromeState），不得绑定 Step 先于内容跳变。
        var xNamespace = document.Root?.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNamespace);
        var progressText = Assert.Single(heading.Elements(), element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute(xNamespace + "Name")?.Value == "StepProgressText");
        Assert.True(HasClass(progressText, "caption"));
        Assert.Null(progressText.Attribute("Text"));
    }

    [Fact]
    public void SetupWizardCompletion_StepTitleUsesSuccessColor()
    {
        // 实验台完成态语义：最后一步（复核）即完成确认，标题以 Success 色标识，无庆祝动画。
        // 各面板标题静态绑定自身资源键——共享 StepTitle 绑定会在 Step 变化的 t=0 让全部
        // 标题（含淡出中的旧面板）同帧跳到新标题，破坏中点换面的次序语义。
        var overlay = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
        var expectedTitleKeys = new[]
        {
            "setupWizardLanguage",
            "setupWizardGamePath",
            "setupWizardDownloadSource",
            "setupWizardProxy",
            "setupWizardReview",
        };
        foreach (var key in expectedTitleKeys)
        {
            var headline = Assert.Single(overlay.Descendants(), element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == $"{{Binding Shell.I18n[{key}]}}"
                && HasClass(element, "wizard-step-title"));
            Assert.Null(headline.Attribute("Classes.wizard-complete"));
            Assert.Equal(key == "setupWizardReview", HasClass(headline, "wizard-complete"));
        }

        Assert.DoesNotContain(
            overlay.Descendants(),
            element => element.Name.LocalName == "TextBlock"
                && (element.Attribute("Text")?.Value.Contains("SetupWizard.StepTitle") ?? false));

        var styles = XDocument.Load(ProjectFile("Views/Styles/SetupWizard.axaml"));
        Assert.Equal(
            "{DynamicResource Launcher.Color.Success}",
            GetStyleSetters(styles, "TextBlock.wizard-step-title.wizard-complete")["Foreground"]);
    }

    [Fact]
    public void SetupWizard_Review_UsesSeparatedCenteredRows()
    {
        var overlay = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
        var xNamespace = overlay.Root?.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNamespace);
        var reviewStep = overlay
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && element.Attribute(xNamespace + "Name")?.Value == "WizardStep4");
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

        Assert.Equal("{StaticResource Launcher.Control.Height.Dialog}", rowStyle["MinHeight"]);
        Assert.Equal("Center", rowStyle["VerticalAlignment"]);
        Assert.Equal("{StaticResource Launcher.Component.Wizard.Divider.Height}", dividerStyle["Height"]);
        Assert.Equal("{DynamicResource Launcher.Color.Card.Border}", dividerStyle["Background"]);
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
        // M3 状态行：图标 + 文本承载同一状态，整行随路径为空一起隐藏。
        var statusRow = gamePathInput
            .ElementsAfterSelf()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && HasClass(element, "wizard-status-row"));
        Assert.Equal(
            "{Binding Dialogs.SetupWizard.IsGamePathEmpty, Converter={x:Static BoolConverters.Not}}",
            statusRow.Attribute("IsVisible")?.Value);
        var status = statusRow
            .Elements()
            .Single(element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value
                    == "{Binding Dialogs.SetupWizard.GamePathStatusText}");

        Assert.Equal(
            "{Binding Dialogs.SetupWizard.GamePathStatusText}",
            status.Attributes()
                .Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                .Value);
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
            "{DynamicResource Launcher.Color.Primary}",
            GetStyleSetters(styles, "TextBlock.wizard-game-path-status.checking")["Foreground"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Success}",
            GetStyleSetters(styles, "TextBlock.wizard-game-path-status.ready")["Foreground"]);
        Assert.Equal(
            "{StaticResource Launcher.Color.Danger}",
            GetStyleSetters(styles, "TextBlock.wizard-game-path-status.corrupted")["Foreground"]);
        Assert.Equal(
            "{StaticResource Launcher.Color.Danger}",
            GetStyleSetters(styles, "TextBlock.wizard-game-path-status.inaccessible")["Foreground"]);

        // 状态图标与文本共用语义色：检测中 Sync、就绪 CheckCircle、损坏/不可访问 Alert。
        var icons = statusRow
            .Elements()
            .Where(element => element.Name.LocalName == "MaterialIcon")
            .ToList();
        Assert.Collection(
            icons,
            icon =>
            {
                Assert.Equal("Sync", icon.Attribute("Kind")?.Value);
                Assert.Equal(
                    "{Binding Dialogs.SetupWizard.IsGamePathChecking}",
                    icon.Attribute("IsVisible")?.Value);
                Assert.Equal(
                    "{DynamicResource Launcher.Color.Primary}",
                    icon.Attribute("Foreground")?.Value);
            },
            icon =>
            {
                Assert.Equal("CheckCircle", icon.Attribute("Kind")?.Value);
                Assert.Equal(
                    "{Binding Dialogs.SetupWizard.IsGamePathReady}",
                    icon.Attribute("IsVisible")?.Value);
                Assert.Equal(
                    "{DynamicResource Launcher.Color.Success}",
                    icon.Attribute("Foreground")?.Value);
            },
            icon =>
            {
                Assert.Equal("Alert", icon.Attribute("Kind")?.Value);
                Assert.Equal(
                    "{Binding Dialogs.SetupWizard.IsGamePathCorruptedInstallation}",
                    icon.Attribute("IsVisible")?.Value);
                Assert.Equal(
                    "{StaticResource Launcher.Color.Danger}",
                    icon.Attribute("Foreground")?.Value);
            },
            icon =>
            {
                Assert.Equal("Alert", icon.Attribute("Kind")?.Value);
                Assert.Equal(
                    "{Binding Dialogs.SetupWizard.IsGamePathInaccessible}",
                    icon.Attribute("IsVisible")?.Value);
                Assert.Equal(
                    "{StaticResource Launcher.Color.Danger}",
                    icon.Attribute("Foreground")?.Value);
            });
    }

    [Fact]
    public void SetupWizard_ChoiceSteps_UseGroupedRadioButtons()
    {
        var document = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
        var xNamespace = document.Root?.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNamespace);
        var downloadSourceStep = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && element.Attribute(xNamespace + "Name")?.Value == "WizardStep2");
        var proxyStep = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && element.Attribute(xNamespace + "Name")?.Value == "WizardStep3");
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
        // M3 选项行：RadioButton 语义不变，整行以 wizard-option 呈现；不得用命令按钮改写选择语义。
        var radioElements = downloadSourceRadioButtons.Concat(proxyRadioButtons).ToList();
        Assert.All(
            radioElements,
            button => Assert.Contains("wizard-option", button.Attribute("Classes")?.Value, StringComparison.Ordinal));
        Assert.DoesNotContain(
            downloadSourceStep.Descendants().Concat(proxyStep.Descendants()),
            element =>
                element.Attribute("Classes.active") is not null
                || element.Attribute("Command") is not null);
    }

    [Fact]
    public void SetupWizard_ActionButtons_UseTonalAndFilledStyles()
    {
        // ADR-017：向导动作钮 = 中性 tonal（Content.Row 底色）+ filled 主按钮（primary-action 叠加）。
        var styles = XDocument.Load(ProjectFile("Views/Styles/SetupWizard.axaml"));
        var tonal = GetStyleSetters(styles, "Button.wizard-action");
        Assert.Equal("{DynamicResource Launcher.Color.Content.Row}", tonal["Background"]);
        Assert.Equal("{DynamicResource Launcher.Text.Primary}", tonal["Foreground"]);
        Assert.Equal("{StaticResource Launcher.Radius.Xs}", tonal["CornerRadius"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Dialog.Close.Hover}",
            GetStyleSetters(styles, "Button.wizard-action:pointerover")["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Dialog.Close.Pressed}",
            GetStyleSetters(styles, "Button.wizard-action:pressed")["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Primary}",
            GetStyleSetters(styles, "Button.wizard-action.primary-action")["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Primary.Hover}",
            GetStyleSetters(styles, "Button.wizard-action.primary-action:pointerover")["Background"]);
    }

    [Fact]
    public void SetupWizard_OptionRowsUseTokenizedMinimumTargets()
    {
        var styles = XDocument.Load(ProjectFile("Views/Styles/SetupWizard.axaml"));

        // ADR-017：卡片按钮形态（wizard-choice）已被纯单选组 + M3 选项行取代，不得回归。
        Assert.DoesNotContain(
            styles.Descendants().Where(element => element.Name.LocalName == "Style"),
            style => style.Attribute("Selector")?.Value?.Contains("wizard-choice") == true);

        var option = GetStyleSetters(styles, "RadioButton.wizard-option");
        Assert.Equal("Stretch", option["HorizontalAlignment"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Option.MinHeight}",
            option["MinHeight"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Option.Padding}",
            option["Padding"]);
        Assert.Equal("{StaticResource Launcher.Radius.Md}", option["CornerRadius"]);
        Assert.Equal("Stretch", option["HorizontalContentAlignment"]);
        Assert.Equal("Center", option["VerticalContentAlignment"]);
        var hover = GetStyleSetters(styles, "RadioButton.wizard-option:pointerover");
        Assert.Equal(
            "{DynamicResource Launcher.Color.Content.Row.Hover}",
            hover["Background"]);
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

    [Theory]
    [InlineData("Views/SettingsGameSection.axaml", "{Binding Operations.RequestUninstallCommand}")]
    [InlineData("Views/SettingsAdvancedSection.axaml", "{Binding Settings.RequestResetSettingsCommand}")]
    public void SettingsDangerActions_UseDangerActionStyle(string sectionPath, string command)
    {
        var document = XDocument.Load(ProjectFile(sectionPath));
        var button = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == command);

        Assert.True(HasClass(button, "flat-action"), $"{command} must inherit flat-action geometry.");
        Assert.True(HasClass(button, "danger-action"), $"{command} must use danger-action.");
    }

    [Fact]
    public void SettingsDangerActionStyle_RestoresFlatActionGeometryAfterDangerOverrides()
    {
        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var settingDanger = GetStyleSetters(styles, "Button.flat-action.danger-action");

        Assert.Equal("{DynamicResource Launcher.Color.Error}", settingDanger["BorderBrush"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Default}", settingDanger["BorderThickness"]);
        Assert.Equal("{StaticResource Launcher.Control.Height.Setting}", settingDanger["MinHeight"]);
        Assert.Equal("{StaticResource Launcher.Component.Action.Outlined.Padding}", settingDanger["Padding"]);
    }

    [Fact]
    public void SettingsOverlay_RemovesTopStatusSummaryAndUsesInlineContentHeading()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
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
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
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
    public void DialogClose_FocusUsesSubtleAccentTreatment()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var focus = GetStyleSetters(document, "Button.dialog-close:focus-visible");

        Assert.Equal("{DynamicResource Launcher.Color.Primary.Soft}", focus["Background"]);
        Assert.Equal("{DynamicResource Launcher.Color.Primary}", focus["BorderBrush"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Default}", focus["BorderThickness"]);
    }

    [Fact]
    public void ConfirmDialogs_UseBasicMessageAndFilledPrimaryActions()
    {
        var control = XDocument.Load(ProjectFile("Controls/ConfirmDialog.axaml"));
        var message = control
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBlock"
                && HasClass(element, "dialog-message"));
        Assert.Equal(
            "{Binding Message, ElementName=Root}",
            message.Attribute("Text")?.Value);

        Assert.DoesNotContain(
            control.Descendants(),
            element => element.Name.LocalName == "MaterialIcon");
        Assert.DoesNotContain(
            control.Descendants(),
            element => element.Name.LocalName == "Button" && HasClass(element, "dialog-close"));
        Assert.Equal(
            3,
            control
                .Descendants()
                .Count(element => element.Name.LocalName == "Button" && HasClass(element, "confirm-dialog-action")));

        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        Assert.Equal(
            "{DynamicResource Launcher.Color.Primary}",
            GetStyleSetters(styles, "Button.confirm-dialog-action")["Foreground"]);

        var filledPrimary = GetStyleSetters(styles, "Button.confirm-dialog-action.primary-action");
        Assert.Equal(
            "{DynamicResource Launcher.Color.Primary}",
            filledPrimary["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.OnPrimary}",
            filledPrimary["Foreground"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Primary.Hover}",
            GetStyleSetters(styles, "Button.confirm-dialog-action.primary-action:pointerover")["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Primary.Pressed}",
            GetStyleSetters(styles, "Button.confirm-dialog-action.primary-action:pressed")["Background"]);

        var filledDanger = GetStyleSetters(styles, "Button.confirm-dialog-action.danger-action");
        Assert.Equal(
            "{DynamicResource Launcher.Color.Error}",
            filledDanger["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.OnError}",
            filledDanger["Foreground"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Error.Hover}",
            GetStyleSetters(styles, "Button.confirm-dialog-action.danger-action:pointerover")["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Error.Pressed}",
            GetStyleSetters(styles, "Button.confirm-dialog-action.danger-action:pressed")["Background"]);
    }

    [Fact]
    public void ConfirmDialogUsages_ExposeNoDeadAnatomyProperties()
    {
        // ADR-015：永不渲染的旧解剖属性整体退场，调用点不得再传。
        var deadPropertyNames = new[]
        {
            "IconKind",
            "AlertTitle",
            "IsWarningAlert",
            "IsDangerAlert",
            "ConfirmIconKind",
            "CloseToolTip",
            "DialogMaxWidth",
            "Description"
        };

        var document = XDocument.Load(ProjectFile("Views/MainWindowDialogsOverlay.axaml"));
        var usages = document
            .Descendants()
            .Where(element => element.Name.LocalName == "ConfirmDialog")
            .ToArray();
        // 调试重置 / 设置页重置共享同一对话框控件，各自独立实例。
        Assert.Equal(9, usages.Length);

        foreach (var usage in usages)
        {
            foreach (var deadPropertyName in deadPropertyNames)
            {
                Assert.Null(usage.Attribute(deadPropertyName));
            }

            // 调用点必须同时给出门面归约所需的最小语义集。
            Assert.NotNull(usage.Attribute("Title"));
            Assert.NotNull(usage.Attribute("Message"));
            Assert.NotNull(usage.Attribute("CancelCommand"));
            Assert.NotNull(usage.Attribute("ConfirmCommand"));
        }
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
            "{StaticResource Launcher.Control.Height.Setting}",
            search.Attribute("Height")?.Value);

        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        Assert.Equal(
            "{StaticResource Launcher.Control.Height.Setting}",
            GetStyleSetters(styles, "Button.filter-tab.log-filter")["Height"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.LogViewer.FilterBar.Margin}",
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
            "{DynamicResource Launcher.Color.Content.Row}",
            statusStyle["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Primary.Border}",
            statusStyle["BorderBrush"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Default}", statusStyle["BorderThickness"]);
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
            "SettingSelect",
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

        var app = XDocument.Load(ProjectFile("App.axaml"));
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

        // ADR-018 融合变体顺序：身份卡（版本 caption + 操作行）→ 版本信息 kv（5 行详情）
        // → 链接行（仓库/问题反馈）→ 法律信息（版权/署名/免责声明）。
        AssertOrdered(
            aboutText,
            "Shell.VersionCaptionText",
            "Settings.CheckForUpdatesCommand",
            "WindowChrome.OpenOfficialSiteCommand",
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
            "WindowChrome.OpenOfficialSiteCommand",
            "WindowChrome.OpenHelpDocsCommand",
            "WindowChrome.OpenGitHubRepositoryCommand",
            "WindowChrome.OpenIssueTrackerCommand",
            "Shell.I18n[aboutCopyrightText]",
            "WindowChrome.OpenPrivacyPolicyCommand",
            "Shell.I18n[defaultBackgroundCopyrightText]",
            "WindowChrome.OpenDefaultBackgroundArtworkCommand",
            "Shell.I18n[aboutDisclaimerText]");
        AssertOrdered(
            advancedText,
            "LogViewer.OpenCommand",
            "LogViewer.ExportCommand",
            "WindowChrome.OpenDataDirectoryCommand");

        Assert.DoesNotContain("LogViewer.OpenCommand", aboutText, StringComparison.Ordinal);
        Assert.DoesNotContain("LogViewer.ExportCommand", aboutText, StringComparison.Ordinal);
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

    [Fact]
    public void ToastAndDebugOverlay_NewMeasurements_UseLauncherTokens()
    {
        var debugOverlay = File.ReadAllText(ProjectFile("Views/MainWindowDebugOverlay.axaml"));
        var toastStyles = File.ReadAllText(ProjectFile("Views/Styles/Toast.axaml"));

        Assert.DoesNotContain("Width=\"720\"", debugOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("Height=\"540\"", debugOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"300\"", debugOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth=\"110\"", debugOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxHeight=\"160\"", debugOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight\" Value=\"30\"", toastStyles, StringComparison.Ordinal);
        Assert.DoesNotContain("Padding\" Value=\"12,8\"", toastStyles, StringComparison.Ordinal);
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
        Assert.DoesNotContain(document.Descendants(), element => HasClass(element, "toast-rail"));

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

        var styles = XDocument.Load(ProjectFile("Views/Styles/Toast.axaml"));
        var titleStyle = styles.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value == "TextBlock.toast-title");
        Assert.Contains(titleStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "FontSize"
            && element.Attribute("Value")?.Value == "{StaticResource Launcher.Typography.FontSize.Body.Md}");
        // toast-title no longer sets FontWeight (removed to match the lighter title + button styling).
    }

    [Fact]
    public void ToastProgress_ShowsOnlyActionExecutingIndeterminateBar()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowToastOverlay.axaml"));
        var progressElements = document.Descendants()
            .Where(element => HasClass(element, "toast-progress")).ToArray();
        Assert.Single(progressElements);

        var actionExecuting = progressElements[0];
        Assert.Equal("1", actionExecuting.Attribute("Grid.Row")?.Value);
        Assert.Equal("{Binding IsActionExecuting}", actionExecuting.Attribute("IsVisible")?.Value);
        Assert.Equal("True", actionExecuting.Attribute("IsIndeterminate")?.Value);

        var styles = XDocument.Load(ProjectFile("Views/Styles/Toast.axaml"));
        var progressStyle = styles.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value == "ProgressBar.toast-progress");
        Assert.Contains(progressStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Height"
            && element.Attribute("Value")?.Value
                == "{StaticResource Launcher.Component.Toast.Action.Progress.Height}");
        Assert.DoesNotContain(progressStyle.Descendants(), element =>
            element.Name.LocalName == "DoubleTransition"
            && element.Attribute("Property")?.Value == "Value");

        var toastCardStyle = styles.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value == "Border.toast-card");
        foreach (var property in new[] { "MinWidth", "MaxWidth" })
        {
            Assert.Contains(toastCardStyle.Elements(), element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == property
                && element.Attribute("Value")?.Value == "{StaticResource Launcher.Component.Toast.Width}");
        }
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
    public void ToastMotionAnimation_IsEnabledOnlyByRootMotionPreference()
    {
        var overlay = File.ReadAllText(ProjectFile("Views/MainWindowToastOverlay.axaml"));
        Assert.Contains(
            "Classes.motion-enabled=\"{Binding #ToastOverlayRoot.((vm:MainWindowViewModel)DataContext).IsMotionEnabled}\"",
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

        var enterAnimations = enterMotionStyle
            .Descendants()
            .Where(element => element.Name.LocalName == "Animation")
            .ToList();
        Assert.Equal(2, enterAnimations.Count);
        foreach (var animation in enterAnimations)
        {
            Assert.Equal(
                "{StaticResource Launcher.Motion.Duration.Fast}",
                animation.Attribute("Duration")?.Value);
            Assert.Equal("Forward", animation.Attribute("FillMode")?.Value);
            Assert.Null(animation.Attribute("Delay"));
        }

        // ADR-016 Toast 进入：透明度走进入减速曲线，右侧滑入位移走点到点曲线（与已确认原型一致）。
        var opacityEnterAnimation = Assert.Single(
            enterAnimations,
            animation => animation.Attribute("Easing")?.Value == "{StaticResource Launcher.Motion.Easing.Enter}");
        var slideEnterAnimation = Assert.Single(
            enterAnimations,
            animation => animation.Attribute("Easing")?.Value == "{StaticResource Launcher.Motion.Easing.PointToPoint}");

        var opacityEnterKeyFrames = GetAnimationKeyFrames(opacityEnterAnimation);
        AssertAnimationProperty(opacityEnterKeyFrames, "Opacity", "0", "1");
        Assert.DoesNotContain(
            opacityEnterKeyFrames.SelectMany(pair => pair.Value.Elements()),
            element => element.Attribute("Property")?.Value == "TranslateTransform.X");

        var slideEnterKeyFrames = GetAnimationKeyFrames(slideEnterAnimation);
        AssertAnimationProperty(
            slideEnterKeyFrames,
            "TranslateTransform.X",
            "{StaticResource Launcher.Motion.Offset.Toast}",
            "0");
        Assert.DoesNotContain(
            slideEnterKeyFrames.SelectMany(pair => pair.Value.Elements()),
            element => element.Attribute("Property")?.Value == "Opacity");

        AssertExitMotionAnimation(
            document,
            "Border.toast-card.motion-enabled.motion-exit",
            expectedEndOffset: "{StaticResource Launcher.Motion.Offset.Toast}",
            expectedEndAxis: "TranslateTransform.X");
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
    public void CoreMotionStyles_DefineExactConditionalAnimations()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));

        AssertOverlayBrushAnimation(
            document,
            "Grid.motion-overlay.motion-enabled.motion-enter",
            "{StaticResource Launcher.Motion.Duration.Fast}");
        AssertMotionAnimation(
            document,
            "Grid.motion-shell.motion-enabled.motion-enter",
            "{StaticResource Launcher.Motion.Duration.Fast}",
            expectedStartOffset: null);
        AssertMotionAnimation(
            document,
            "Grid.motion-overlay.motion-enabled.motion-enter > Border.motion-surface",
            "{StaticResource Launcher.Motion.Duration.Normal}",
            expectedStartOffset: "{StaticResource Launcher.Motion.Offset.Surface}",
            expectsOpacity: false);
        AssertMotionAnimation(
            document,
            ":is(UserControl).motion-content.motion-enabled.motion-enter",
            "{StaticResource Launcher.Motion.Duration.Fast}",
            expectedStartOffset: null);
        AssertMotionAnimation(
            document,
            "StackPanel.motion-content.motion-enabled.motion-enter",
            "{StaticResource Launcher.Motion.Duration.Fast}",
            expectedStartOffset: null);
        AssertMotionAnimation(
            document,
            "Border.motion-bottom.motion-enabled.motion-enter",
            "{StaticResource Launcher.Motion.Duration.Normal}",
            expectedStartOffset: "{StaticResource Launcher.Motion.Offset.Bottom}");
        AssertOverlayBrushExitAnimation(
            document,
            "Grid.motion-overlay.motion-enabled.motion-exit");
        AssertExitMotionAnimation(
            document,
            "Grid.motion-overlay.motion-enabled.motion-exit > Border.motion-surface",
            expectedEndOffset: "{StaticResource Launcher.Motion.Offset.Surface}",
            expectsOpacity: false);

        // ADR-016：对话框内部内容层不得再有任何二次运动样式。
        foreach (var removedSelector in new[]
                 {
                     "Grid.motion-overlay.motion-enabled.motion-enter > Border.motion-surface > Grid.motion-surface-content",
                     "Grid.motion-overlay.motion-enabled.motion-exit > Border.motion-surface > Grid.motion-surface-content"
                 })
        {
            Assert.DoesNotContain(
                document.Descendants()
                    .Where(element => element.Name.LocalName == "Style"),
                style => style.Attribute("Selector")?.Value == removedSelector);
        }

        foreach (var selector in new[]
                 {
                     "Grid.motion-overlay",
                     "Border.motion-surface",
                     "Grid.motion-surface-content",
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

            if (surface.Name.LocalName != "DialogSurface")
            {
                // ADR-015 之前的三层结构（Border > Grid）保留内容层淡出检查；
                // 模板控件表面合并了内容层，滑移与整体淡入淡出由平行规则驱动。
                var surfaceContent = surface
                    .Elements()
                    .Single(child => child.Name.LocalName == "Grid");
                Assert.True(HasClass(surfaceContent, "motion-surface-content"));
            }
        });

        var settings = XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
        var contentTargets = settings
            .Descendants()
            .Where(element => HasClass(element, "motion-content"))
            .ToList();

        // ADR-017：向导步骤面板改由后置代码顺序换页（wizard-step），不再属于 motion-content
        // 家族；设置页六个内容分区维持直接绑定驱动的纯淡化（motion-enter 与可见性同源）。
        // 审计-0828：淡化家族不承载位移，仅表面（motion-surface）保留本地 TranslateTransform。
        Assert.Equal(6, contentTargets.Count);
        Assert.All(contentTargets, element =>
        {
            Assert.Equal(
                "{Binding IsMotionEnabled}",
                element.Attribute("Classes.motion-enabled")?.Value);
            Assert.Equal(
                element.Attribute("IsVisible")?.Value,
                element.Attribute("Classes.motion-enter")?.Value);
            Assert.Null(element.Attribute("RenderTransform"));
        });

        var mainWindow = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var bottomTargets = mainWindow
            .Descendants()
            .Where(element => HasClass(element, "motion-bottom"))
            .ToList();

        // ADR-016 游戏操作表面：安装/进度/控制收敛为同一任务容器，motion-bottom 仅剩容器本身。
        var operationSurface = Assert.Single(bottomTargets);
        Assert.Equal(
            "OperationSurface",
            operationSurface
                .Attributes()
                .Single(attribute => attribute.Name.LocalName == "Name"
                    && attribute.Name.NamespaceName.EndsWith("/2006/xaml", StringComparison.Ordinal))
                .Value);
        Assert.Equal(
            "{Binding IsMotionEnabled}",
            operationSurface.Attribute("Classes.motion-enabled")?.Value);
        // 容器的 motion-enter 是一次性入场锚点，静态类声明（任何面板可见即成立），
        // 入场窗期后由后置代码摘除，不随状态切换重放；不得改回绑定表达式——
        // 摘除后绑定回流会重放入场（枚举值恒真时绑定等价于常量 true，掩盖摘除语义）。
        Assert.True(HasClass(operationSurface, "motion-enter"));
        Assert.Null(operationSurface.Attribute("Classes.motion-enter"));
        Assert.Null(operationSurface.Attribute("IsVisible"));
        AssertHasLocalTranslateTransform(operationSurface);
    }

    [Fact]
    public void MainWindow_OperationStates_TransformInsideSingleTaskContainer()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));

        var surface = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "operation-surface"));
        var host = surface.Elements().Single(element => element.Name.LocalName == "Panel");

        var states = host
            .Elements()
            .Where(element => element.Name.LocalName == "Border" && HasClass(element, "operation-state"))
            .ToList();
        Assert.Equal(3, states.Count);

        var expectedVisibility = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OperationInstallState"] = "{Binding Operations.IsInstallPanelVisible}",
            ["OperationProgressState"] = "{Binding Operations.IsProgressPanelVisible}",
            ["OperationControlState"] = "{Binding Operations.IsControlPanelVisible}",
        };
        static string? XamlName(XElement element) => element
            .Attributes()
            .SingleOrDefault(attribute => attribute.Name.LocalName == "Name"
                && attribute.Name.NamespaceName.EndsWith("/2006/xaml", StringComparison.Ordinal))
            ?.Value;
        foreach (var state in states)
        {
            // 状态在任务容器内原地交换：不再作为独立面板直接绑定 motion 入场动画。
            Assert.Null(state.Attribute("Classes.motion-enter"));
            Assert.Null(state.Attribute("Classes.motion-enabled"));
            if (XamlName(state) is { } name)
            {
                Assert.Contains(name, expectedVisibility.Keys);
                Assert.Equal(expectedVisibility[name], state.Attribute("IsVisible")?.Value);
            }
        }

        // 三个状态各自具名，逐一锁定可见性绑定。
        foreach (var stateName in expectedVisibility.Keys)
        {
            Assert.Single(states, state => XamlName(state) == stateName);
        }

        // 底栏外观（内边距与两档最小高度）由各状态自行承载，控制态叠加专属渐变。
        Assert.All(states, state => Assert.True(HasClass(state, "bottom-panel")));
        Assert.Single(states, state => HasClass(state, "control-panel"));
        // 外壳仅承载定位与形变，不带任何外观类，避免高度双重计入。
        Assert.DoesNotContain(surface.Attributes(), attribute =>
            attribute.Name.LocalName.StartsWith("Classes", StringComparison.Ordinal)
            && attribute.Value?.Contains("bottom-panel") == true);
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
    public void DynamicAccent_DoesNotReplaceThemeSpecificInformationTextBrush()
    {
        var settingsViewModel = File.ReadAllText(ProjectFile("Features/Settings/SettingsViewModel.cs"));

        Assert.DoesNotContain(
            "SetBrush(application, \"Launcher.Text.Info\"",
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

    [Fact]
    public void DialogSurface_ControlTheme_CarriesAnatomyPartsAndProfileTokens()
    {
        var document = XDocument.Load(ProjectFile("Views/Styles/DialogSurface.axaml"));
        var templateText = document.ToString();

        foreach (var partName in new[]
                 {
                     "PART_PanelHead",
                     "PART_SurfaceBorder",
                     "PART_BasicHead",
                     "PART_CloseButton",
                     "PART_ScrollViewer",
                     "PART_DirectContentPresenter",
                     "PART_ScrollContentPresenter",
                     "PART_FooterBand",
                     "PART_BadgePresenter",
                     "PART_FooterLeadingPresenter"
                 })
        {
            Assert.Contains(partName, templateText, StringComparison.Ordinal);
        }

        // ADR-015 表面档案：阴影必须经 BoxShadowsExtension 消费单一 token。
        Assert.Contains(
            "{helpers:BoxShadows {StaticResource Launcher.Elevation.Shadow.Dialog}}",
            templateText,
            StringComparison.Ordinal);

        // 形态与状态的伪类解剖规则齐备。
        // 形态可见性由 RefreshChrome 以本地值管理；样式仅保留动作带皮肤差异。
        var selectors = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .Select(element => element.Attribute("Selector")?.Value ?? string.Empty)
            .ToArray();
        Assert.DoesNotContain(selectors, selector =>
            selector.Contains("PART_PanelHead", StringComparison.Ordinal)
            || selector.Contains("PART_BasicHead", StringComparison.Ordinal));
        Assert.Contains("controls|DialogSurface /template/ Border#PART_FooterBand", selectors);
        Assert.Contains("controls|DialogSurface:panel /template/ Border#PART_FooterBand", selectors);
        Assert.Contains("controls|DialogSurface /template/ ScrollViewer#PART_ScrollViewer", selectors);
        Assert.Contains("controls|DialogSurface:panel /template/ ScrollViewer#PART_ScrollViewer", selectors);
        Assert.Contains("controls|DialogSurface:info /template/ ContentPresenter#PART_BadgePresenter", selectors);
        Assert.Contains("controls|DialogSurface:warning /template/ ContentPresenter#PART_BadgePresenter", selectors);
        Assert.Contains("controls|DialogSurface:danger /template/ ContentPresenter#PART_BadgePresenter", selectors);

        var surfaceBorder = document
            .Descendants()
            .Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "PART_SurfaceBorder");
        Assert.Equal("{TemplateBinding ClipToBounds}", surfaceBorder.Attribute("ClipToBounds")?.Value);

        var badgePresenter = document
            .Descendants()
            .Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "PART_BadgePresenter");
        Assert.Equal(
            "{StaticResource Launcher.Component.Dialog.Badge.Margin}",
            badgePresenter.Attribute("Margin")?.Value);
        Assert.Null(badgePresenter.Parent?.Attribute("ColumnSpacing"));

        var footerLeadingPresenter = document
            .Descendants()
            .Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "PART_FooterLeadingPresenter");
        Assert.Equal(
            "{StaticResource Launcher.Component.Dialog.FooterLeading.Margin}",
            footerLeadingPresenter.Attribute("Margin")?.Value);

        Assert.Equal(
            "{StaticResource Launcher.Component.Dialog.Basic.Content.Padding}",
            GetStyleSetters(document, "controls|DialogSurface /template/ ScrollViewer#PART_ScrollViewer")["Padding"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.Dialog.Panel.Body.Padding}",
            GetStyleSetters(document, "controls|DialogSurface:panel /template/ ScrollViewer#PART_ScrollViewer")["Padding"]);
    }

    [Fact]
    public void DialogFamily_ProfileTokens_AreDeclaredOnceInAppResources()
    {
        var appResources = XDocument.Load(ProjectFile("App.axaml"));
        var xKey = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Key";

        string TokenValue(string key) => appResources
            .Descendants()
            .Single(element => (string?)element.Attribute(xKey) == key)
            .Value;

        Assert.Equal("20", TokenValue("Launcher.Component.Dialog.CornerRadius"));
        Assert.Equal(2, TokenValue("Launcher.Elevation.Shadow.Dialog").Split(',').Length);
        Assert.Equal("32", TokenValue("Launcher.Component.Dialog.Badge.Size"));
        Assert.Equal("16", TokenValue("Launcher.Component.Dialog.Badge.CornerRadius"));
        Assert.Equal("0,0,12,0", TokenValue("Launcher.Component.Dialog.Badge.Margin"));
        Assert.Equal("20,0,10,0", TokenValue("Launcher.Component.Dialog.Panel.Head.Padding"));
        Assert.Equal("24,18,24,18", TokenValue("Launcher.Component.Dialog.Panel.Body.Padding"));
        Assert.Equal("24,14,24,20", TokenValue("Launcher.Component.Dialog.Panel.Footer.Padding"));
        Assert.Equal("0,0,12,0", TokenValue("Launcher.Component.Dialog.FooterLeading.Margin"));
        Assert.Equal("28,28,28,8", TokenValue("Launcher.Component.Dialog.Basic.Head.Padding"));
        Assert.Equal("28,0,28,0", TokenValue("Launcher.Component.Dialog.Basic.Content.Padding"));
        Assert.Equal("28,16,28,24", TokenValue("Launcher.Component.Dialog.Basic.Actions.Padding"));
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
            var applicationProject = Path.Combine(directory.FullName, "src", "Cafe.Launcher.Avalonia", "Cafe.Launcher.Avalonia.csproj");
            if (File.Exists(applicationProject))
            {
                return Path.GetDirectoryName(applicationProject)!;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("src/Cafe.Launcher.Avalonia/Cafe.Launcher.Avalonia.csproj was not found.");
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
