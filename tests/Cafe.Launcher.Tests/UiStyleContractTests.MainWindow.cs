using System.Text.RegularExpressions;
using System.Xml.Linq;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

// MainWindow shell contracts: operation panels, title bar actions, game manage
// flyouts, news tabs, and carousel/banner navigation anatomy.
public sealed partial class UiStyleContractTests
{
    [Fact]
    public void LauncherIcons_UserFacingActions_UseApprovedSemanticMappings()
    {
        var mainWindow = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
        var dialogs = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowDialogsOverlay.axaml"));
        var resourcePanelOverlay = XDocument.Load(TestRepository.FromPresentationRoot("Views/ResourcePanelOverlay.axaml"));
        var settingsOverlay = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowSettingsOverlay.axaml"));

        var actions = XDocument.Load(TestRepository.FromPresentationRoot("Controls/GameOperationsSplitButton.axaml"));
        Assert.Contains(actions.Descendants(), element => element.Name.LocalName == "MaterialIcon"
            && element.Attribute("Kind")?.Value == "{Binding Operations.PrimaryActionIcon}");

        var resourcePanelButton = mainWindow
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding ResourcePanel.OpenResourcePanelCommand}");
        Assert.Equal(
            "ClipboardText",
            resourcePanelButton.Descendants().Single(element => element.Name.LocalName == "MaterialIcon").Attribute("Kind")?.Value);
        // ADR-041：面板正文里出现了模板内的状态图标，头部徽章按解剖槽位定位，不再依赖文档顺序。
        var resourcePanelHeadingIcon = resourcePanelOverlay
            .Descendants()
            .First(element => element.Name.LocalName == "DialogSurface.HeaderIcon")
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
    public void MainWindow_OperationPanel_HasOneStableLayout()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
        var panel = Assert.Single(document.Descendants(), element => HasClass(element, "bottom-panel"));
        Assert.Null(panel.Attribute("IsVisible"));
        var layout = Assert.Single(panel.Elements(), element => element.Name.LocalName == "Grid");
        Assert.Equal("*,Auto", layout.Attribute("ColumnDefinitions")?.Value);
        Assert.Single(layout.Elements(), element => element.Name.LocalName == "GameOperationsSplitButton");
        Assert.DoesNotContain(panel.Descendants(), element => element.Name.LocalName == "ProgressBar");
    }

    [Fact]
    public void GameTaskDialog_PreparationUsesDraftActions()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/GameTaskDialog.axaml"));
        foreach (var command in new[] { "ChooseInstallPath", "ConfirmInstall", "IdentifyInstalledGame" })
            Assert.Single(document.Descendants(), element => element.Attribute("Command")?.Value == "{Binding Operations." + command + "Command}");
        Assert.DoesNotContain(document.Descendants().Attributes(), attribute => attribute.Value.Contains("ChangePersistedGamePath", StringComparison.Ordinal));
    }

    [Fact]
    public void GameTaskDialog_ActionsHaveLocalizedAccessibleNames()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/GameTaskDialog.axaml"));
        var buttons = document.Descendants().Where(element => element.Name.LocalName == "Button").ToArray();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, button =>
        {
            Assert.Equal(button.Attribute("Content")?.Value, button.Attribute("AutomationProperties.Name")?.Value);
            Assert.True(HasClass(button, "dialog-action"));
        });
    }

    [Fact]
    public void MainWindow_ControlPanel_ExplainsTheStartAction()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
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
    public void MainWindow_StatusCaptions_ExposeDownloadSource()
    {
        // 统一底部状态行展示当前下载源，隐藏详情时整行隐藏。
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
        var captionBindings = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding Shell.DownloadSourceText}");
        Assert.Single(captionBindings);
    }

    [Theory]
    [InlineData("OperationControlState", new[] { "Operations.GameSessionStateText", "Shell.LaunchCheckText", "Shell.NetworkText", "Shell.DownloadSourceText", "Shell.VersionText" })]
    public void MainWindow_StatusCaptions_UseStatusFirstOrder(string panelName, string[] expectedBindings)
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
        var panel = document.Descendants().Single(element =>
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == panelName));
        var statusRow = panel.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "{Binding Shell.DownloadSourceText}").Parent!;
        Assert.Equal(
            expectedBindings.Select(binding => "{Binding " + binding + "}"),
            statusRow.Elements().Select(element => element.Attribute("Text")?.Value));
    }

    [Fact]
    public void MainWindow_SettingsAdvancedSection_ExposesLauncherSettingsReset()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SettingsAdvancedSection.axaml"));
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
        var overlay = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowDialogsOverlay.axaml"));
        var dialog = overlay
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ConfirmDialog"
                && element.Attribute("IsOpen")?.Value == "{Binding Dialogs.SettingsResetConfirm.IsVisible}");
        Assert.Equal(
            "{Binding Shell.I18n[debugResetSettingsTitle]}",
            dialog.Attribute("Title")?.Value);
        Assert.Equal(
            "{Binding Shell.I18n[debugResetSettingsConfirm]}",
            dialog.Attribute("ConfirmText")?.Value);
        Assert.Equal(
            "{Binding Dialogs.SettingsResetConfirm.ConfirmCommand}",
            dialog.Attribute("ConfirmCommand")?.Value);
        Assert.Equal(
            "{Binding Dialogs.SettingsResetConfirm.CancelCommand}",
            dialog.Attribute("CancelCommand")?.Value);
    }

    [Fact]
    public void MainWindow_GameOperationsSplitButtons_ReuseOneExtractedControl()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
        var manageEntries = document
            .Descendants()
            .Where(element => element.Name.LocalName == "GameOperationsSplitButton")
            .ToArray();

        // 紧凑布局与「仅按钮」布局各一处，共用同一控件；菜单不再在窗口中各自复制一份。
        Assert.Single(manageEntries);
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Name.LocalName == "MenuFlyout");
    }

    [Fact]
    public void GameOperationsSplitButton_ExposesFlyoutMenuWithOperationBindings()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Controls/GameOperationsSplitButton.axaml"));
        var button = document.Descendants()
            .Single(element => element.Name.LocalName == "SplitButton");

        Assert.True(HasClass(button, "primary-operation"));
        Assert.Equal("{Binding Operations.PrimaryActionCommand}", button.Attribute("Command")?.Value);
        Assert.Equal("{Binding Operations.PrimaryActionText}", button.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("{Binding Operations.CanUsePrimaryAction}", button.Attribute("IsEnabled")?.Value);

        var flyout = button.Descendants().Single(element => element.Name.LocalName == "MenuFlyout");
        Assert.Equal("TopEdgeAlignedRight", flyout.Attribute("Placement")?.Value);
        Assert.Equal("{StaticResource Launcher.Component.LaunchSplit.Menu.VerticalOffset}", flyout.Attribute("VerticalOffset")?.Value);
        Assert.Equal("Separator", flyout.Elements().Last().PreviousNode is XElement separator ? separator.Name.LocalName : "");

        Dictionary<string, string> expectedMenuItems = new(StringComparer.Ordinal)
        {
            ["{Binding Operations.CheckForGameUpdateCommand}"] = "{Binding Shell.I18n[gameCheckUpdate]}",
            ["{Binding Operations.CreateGameShortcutCommand}"] = "{Binding Shell.I18n[gameCreateShortcut]}",
            ["{Binding Operations.OpenGameFolderCommand}"] = "{Binding Shell.I18n[gameOpenFolder]}",
            ["{Binding Operations.RequestRepairCommand}"] = "{Binding Shell.I18n[repair]}",
            ["{Binding Operations.RequestUninstallCommand}"] = "{Binding Shell.I18n[uninstall]}"
        };

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

    [Fact]
    public void GameTaskSurfaces_KeepPreparationModalAndExecutionProgressFloating()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/GameTaskDialog.axaml"));
        Assert.Single(document.Descendants(), element => element.Attribute("Kind")?.Value == "{Binding Operations.TaskIcon}");
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "ProgressBar");
        var card = XDocument.Load(TestRepository.FromPresentationRoot("Views/GameTaskCard.axaml"));
        var progress = Assert.Single(card.Descendants(), element => element.Name.LocalName == "ProgressBar"
            && element.Attribute("Value")?.Value == "{Binding Operations.ProgressValue}");
        Assert.Equal("{Binding Operations.ProgressValue}", progress.Attribute("Value")?.Value);
        Assert.Equal("{Binding Operations.IsProgressIndeterminate}", progress.Attribute("IsIndeterminate")?.Value);
    }

    [Fact]
    public void MainWindow_TitleBarActions_ExposeAccessibleTokenizedPointerAndKeyboardFeedback()
    {
        var view = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.Styles.axaml"));
        Dictionary<string, string> expectedNames = new(StringComparer.Ordinal)
        {
            ["{Binding WindowChrome.OpenDebugPanelCommand}"] = "{Binding Shell.I18n[debugPanel]}",
            ["{Binding ResourcePanel.OpenResourcePanelCommand}"] = "{Binding Shell.I18n[resourcePanel]}",
            ["{Binding WindowChrome.ToggleSettingsCommand}"] = "{Binding Shell.I18n[settings]}",
            ["{Binding WindowChrome.MinimizeCommand}"] = "{Binding Shell.I18n[minimize]}",
            ["{Binding WindowChrome.CloseCommand}"] = "{Binding Shell.I18n[close]}"
        };

        var brandRow = view
            .Descendants()
            .Single(element => HasClass(element, "titlebar-brand-row"));
        var titleBar = brandRow.Parent!;
        Assert.Equal(
            ["{Binding Shell.ProductName}", "{Binding Shell.TitleBarVersionText}"],
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
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
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
            "avares://Cafe.Launcher.UI/Views/MainWindow.Styles.axaml";
        var application = XDocument.Load(TestRepository.FromHostRoot("App.axaml"));
        var mainWindow = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));

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
        var source = File.ReadAllText(TestRepository.FromCoreRoot("Models/LauncherSettings.cs"));

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
        var source = File.ReadAllText(TestRepository.FromPresentationRoot("ViewModels/DialogsViewModel.cs"));

        Assert.Contains("public ConfirmationDialogViewModel DebugResetConfirm { get; }", source, StringComparison.Ordinal);

        // 确认机制（可见性/文案/命令/顺序调用错误策略）由共享模块单点实现，
        // 家族成员不再各自手写命令与事件。
        var module = File.ReadAllText(TestRepository.FromPresentationRoot("ViewModels/ConfirmationDialogViewModel.cs"));
        Assert.Contains("public IRelayCommand CancelCommand", module, StringComparison.Ordinal);
        Assert.Contains("public IAsyncRelayCommand ConfirmCommand", module, StringComparison.Ordinal);
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
            var markup = File.ReadAllText(TestRepository.FromPresentationRoot(path));
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
            .Select(path => XDocument.Load(TestRepository.FromPresentationRoot(path)))
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
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
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

        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.Styles.axaml"));
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
    public void MainWindow_RemoteContentLoadError_CarriesTheCardBackground()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
        var errorCard = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "remote-content-load-error"));
        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.Styles.axaml"));
        var errorStyle = GetStyleSetters(styles, "Border.remote-content-load-error");

        // 失败态替代整块内容区，必须与 remote-content-card 同底同圆角，
        // 否则加载失败时透出壁纸，呈现为无背景的悬空错误文字。
        Assert.Equal("{DynamicResource Launcher.Color.Panel.Background}", errorStyle["Background"]);
        Assert.Equal("{StaticResource Launcher.Radius.Sm}", errorStyle["CornerRadius"]);

        // 文案指引"刷新重试"，卡片必须就地给出重试动作，而不是让用户去别处找刷新按钮。
        var retryButton = errorCard
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding RefreshCommand}");
        Assert.Equal("{Binding Shell.I18n[retry]}", retryButton.Attribute("AutomationProperties.Name")?.Value);
        Assert.Single(
            retryButton.Descendants(),
            icon => icon.Name.LocalName == "MaterialIcon"
                && icon.Attribute("Kind")?.Value == "Refresh");
    }

    [Fact]
    public void MainWindow_NewsTabs_UseNativeSelectionAndScrollableReadableRows()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
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
        var remoteStyles = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/RemoteContent.axaml"));
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
        var appResources = XDocument.Load(TestRepository.FromHostRoot("App.axaml"));
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

        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.Styles.axaml"));
        var newsContentRow = GetStyleSetters(styles, "Border.content-row.news-content-row");
        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.Sm}", newsContentRow["Padding"]);
        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.None}", newsContentRow["Margin"]);
    }

    [Fact]
    public void MainWindow_CarouselNavigation_UsesTokenizedHitTargetsAndLocalizedNames()
    {
        var view = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.Styles.axaml"));
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
        // 图标前景保持 OnChrome 豁免（spec §7）。
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
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
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

        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.Styles.axaml"));
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
        var remoteContentStyles = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/RemoteContent.axaml"));
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
        Assert.Equal(
            "{StaticResource Launcher.StateLayer.Disabled.Content}",
            GetStyleSetters(styles, "Grid.banner-stage.active > Button.banner-control:disabled")["Opacity"]);
    }

    [Fact]
    public void MainWindow_IsResizableWithMinimumViewportConstraints()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
        var window = document.Root;

        Assert.NotNull(window);
        Assert.Equal("True", window.Attribute("CanResize")?.Value);
        Assert.Equal("{StaticResource Launcher.Layout.Window.MinWidth}", window.Attribute("MinWidth")?.Value);
        Assert.Equal("{StaticResource Launcher.Layout.Window.MinHeight}", window.Attribute("MinHeight")?.Value);
    }

    [Fact]
    public void GameTaskDialog_PathDoesNotBindSettingsEditor()
    {
        var view = File.ReadAllText(TestRepository.FromPresentationRoot("Views/GameTaskDialog.axaml"));
        Assert.Contains("{Binding Operations.InstallPathDraft}", view, StringComparison.Ordinal);
        var card = File.ReadAllText(TestRepository.FromPresentationRoot("Views/GameTaskCard.axaml"));
        Assert.Contains("{Binding Operations.CompactTaskSummary}", card, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleTaskDetails", card, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollViewer", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.Editor", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.Editor", view, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_SocialActions_AddTopRightVerticalIconButtonsAndUseRemoteContentVisibilitySetting()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
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

        var mainStyles = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.Styles.axaml"));
        var remoteStyles = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/RemoteContent.axaml"));
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
