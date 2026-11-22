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
        var settingsOverlay = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowSettingsOverlay.axaml"));

        var actions = XDocument.Load(TestRepository.FromPresentationRoot("Controls/GameOperationsSplitButton.axaml"));
        Assert.Contains(actions.Descendants(), element => element.Name.LocalName == "FluentIcon"
            && element.Attribute("Glyph")?.Value == "{Binding Operations.PrimaryActionGlyph}");

        var resourcePanelButton = mainWindow
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding ResourcePanel.OpenResourcePanelCommand}");
        Assert.Equal(
            "{x:Static glyphs:FluentGlyphs.Clipboard}",
            resourcePanelButton.Descendants().Single(element => element.Name.LocalName == "FluentIcon").Attribute("Glyph")?.Value);

        var settingsFooterIcons = settingsOverlay
            .Descendants()
            .Where(element => element.Name.LocalName == "FluentIcon")
            .Select(element => element.Attribute("Glyph")?.Value)
            .ToArray();
        Assert.Contains("{x:Static glyphs:FluentGlyphs.Cancel}", settingsFooterIcons);
        Assert.Contains("{x:Static glyphs:FluentGlyphs.Save}", settingsFooterIcons);
    }

    [Fact]
    public void MainWindow_OperationPanel_HasOneStableLayout()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
        var panel = Assert.Single(document.Descendants(), element => HasClass(element, "operation-state"));
        Assert.Null(panel.Attribute("IsVisible"));
        var layout = Assert.Single(panel.Elements(), element => element.Name.LocalName == "StackPanel");
        var split = Assert.Single(layout.Elements(), element => element.Name.LocalName == "GameOperationsSplitButton");
        Assert.Equal("{StaticResource Launcher.Component.GameTask.Width}", layout.Attribute("Width")?.Value);
        var taskCard = Assert.Single(layout.Elements(), element => element.Name.LocalName == "GameTaskCard");
        Assert.Equal("{Binding Operations.IsTaskCardVisible}", taskCard.Attribute("IsVisible")?.Value);
        Assert.NotNull(split);
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
        var statusDetails = document
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value == "GameStatusDetails"));
        Assert.Contains(
            statusDetails.Descendants(),
            element => element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding Shell.LaunchCheckText}");
    }

    [Fact]
    public void MainWindow_StatusCaptions_ExposeDownloadSource()
    {
        // 标题下方的状态行展示当前下载源，隐藏详情时整行隐藏。
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
        var captionBindings = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding Shell.DownloadSourceText}");
        Assert.Single(captionBindings);
    }

    [Theory]
    [InlineData("HomeStage", new[] { "Operations.GameSessionStateText", "Shell.LaunchCheckText", "Shell.NetworkText", "Shell.DownloadSourceText", "Shell.VersionText" })]
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
        Assert.Null(button.Attribute("Theme"));
        Assert.Equal("{Binding Operations.PrimaryActionCommand}", button.Attribute("Command")?.Value);
        Assert.Equal("{Binding Operations.PrimaryActionText}", button.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("{Binding Operations.CanUsePrimaryAction}", button.Attribute("IsEnabled")?.Value);

        var flyout = button.Descendants().Single(element => element.Name.LocalName == "FAMenuFlyout");
        Assert.Equal("TopEdgeAlignedRight", flyout.Attribute("Placement")?.Value);
        Assert.Equal("{StaticResource Launcher.Component.LaunchSplit.Menu.VerticalOffset}", flyout.Attribute("VerticalOffset")?.Value);
        Assert.Equal("FAMenuFlyoutSeparator", flyout.Elements().Last().PreviousNode is XElement separator ? separator.Name.LocalName : "");

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
            .Where(element => element.Name.LocalName == "FAMenuFlyoutItem")
            .ToArray();
        Assert.Equal(expectedMenuItems.Count, menuItems.Length);
        foreach (var (command, header) in expectedMenuItems)
        {
            var menuItem = menuItems.Single(element => element.Attribute("Command")?.Value == command);
            Assert.Equal(header, menuItem.Attribute("Text")?.Value);
            Assert.Equal(
                header,
                menuItem.Attributes().Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name").Value);
            var icon = menuItem.Descendants().Single(element => element.Name.LocalName == "FluentIconSource");
            Assert.Equal("{StaticResource Launcher.Icon.Md}", icon.Attribute("FontSize")?.Value);
        }
    }

    [Fact]
    public void GameTaskSurfaces_KeepPreparationModalAndExecutionProgressFloating()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/GameTaskDialog.axaml"));
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "ProgressBar");
        var card = XDocument.Load(TestRepository.FromPresentationRoot("Views/GameTaskCard.axaml"));
        var progress = Assert.Single(card.Descendants(), element => element.Name.LocalName == "ProgressBar"
            && element.Attribute("Value")?.Value == "{Binding Operations.ProgressValue}");
        Assert.Equal("{Binding Operations.ProgressValue}", progress.Attribute("Value")?.Value);
        Assert.Equal("{Binding Operations.IsProgressIndeterminate}", progress.Attribute("IsIndeterminate")?.Value);
    }

    [Fact]
    public void MainWindow_UsesFluentDefaultTitleBarAndKeepsRailActionsAccessible()
    {
        var view = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.Styles.axaml"));
        var window = Assert.IsType<XElement>(view.Root);
        Assert.Equal(XName.Get("FAAppWindow", "using:FluentAvalonia.UI.Windowing"), window.Name);
        Assert.Equal("{Binding Shell.WindowTitle}", window.Attribute("Title")?.Value);
        Assert.Null(window.Attribute("WindowDecorations"));
        Assert.Null(window.Attribute("ExtendClientAreaToDecorationsHint"));
        Assert.Null(window.Attribute("ExtendClientAreaTitleBarHeightHint"));

        // FAAppWindow supplies the title bar and native window actions.
        var commands = view
            .Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Select(element => element.Attribute("Command")?.Value ?? "")
            .ToArray();
        Assert.DoesNotContain("{Binding WindowChrome.MinimizeCommand}", commands);
        Assert.DoesNotContain("{Binding WindowChrome.CloseCommand}", commands);
        Assert.DoesNotContain(view.Descendants(), element => HasClass(element, "titlebar-brand-row"));

        // 页头空白的拖拽由原生装饰区角色承担；页头内的工具按钮就近声明 User，
        // 否则点击会连同按钮一起被解释为拖动窗口。
        var header = Assert.Single(view.Descendants(), element => HasClass(element, "game-header"));
        Assert.Equal("TitleBar", header.Attribute("WindowDecorationProperties.ElementRole")?.Value);
        var headerTools = header.Descendants().Where(element => element.Name.LocalName == "Button").ToArray();
        Assert.Equal(2, headerTools.Length);
        Assert.All(headerTools, button =>
            Assert.Equal("User", button.Attribute("WindowDecorationProperties.ElementRole")?.Value));

        Dictionary<string, string> expectedNames = new(StringComparer.Ordinal)
        {
            ["{Binding WindowChrome.OpenDebugPanelCommand}"] = "{Binding Shell.I18n[debugPanel]}",
            ["{Binding WindowChrome.ToggleSettingsCommand}"] = "{Binding Shell.I18n[launcherSettingsTitle]}"
        };

        var actionButtons = view
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Button"
                && expectedNames.ContainsKey(element.Attribute("Command")?.Value ?? ""))
            .ToArray();
        Assert.Equal(expectedNames.Count, actionButtons.Length);

        foreach (var (command, expectedName) in expectedNames)
        {
            var button = actionButtons
                .Single(element => element.Attribute("Command")?.Value == command);
            Assert.True(HasClass(button, "rail-action"));
            Assert.Equal(expectedName, button.Attribute("ToolTip.Tip")?.Value);
            Assert.Equal(
                expectedName,
                button.Attributes()
                    .Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                    .Value);
            Assert.Contains(button.Ancestors(), element =>
                element.Attribute("Width")?.Value == "{StaticResource Launcher.Layout.Home.RailWidth}");
            Assert.Contains(button.Ancestors(), element =>
                element.Attribute("Grid.Row")?.Value == "2");
        }

        Assert.All(actionButtons, button =>
            Assert.Equal("{StaticResource TransparentButton}", (string?)button.Attribute("Theme")));
    }

    [Fact]
    public void MainWindow_GameRailIcons_UseRoundedClipFrame()
    {
        var view = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.Styles.axaml"));

        // 官方图标是方形位图，用与关于页应用图标相同的 8 DIP 圆角裁切；
        // 裁切落在包住 Image 的 Border 上，只设置图片自身不会裁掉位图直角。
        var frame = Assert.Single(view.Descendants(), element => HasClass(element, "game-rail-icon-frame"));
        var image = Assert.Single(frame.Elements(), element => element.Name.LocalName == "Image");
        Assert.True(HasClass(image, "game-rail-icon"));

        var style = Assert.Single(styles.Descendants(), element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value == "Border.game-rail-icon-frame");
        var setters = style.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => element.Attribute("Property")?.Value ?? "",
                element => element.Attribute("Value")?.Value ?? "",
                StringComparer.Ordinal);
        Assert.Equal("True", setters["ClipToBounds"]);
        Assert.Equal("{StaticResource Launcher.Radius.Sm}", setters["CornerRadius"]);
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
        var gameSource = File.ReadAllText(TestRepository.FromCoreRoot("Models/GameSettings.cs"));

        Assert.DoesNotContain("[property:", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[ObservableProperty]", source, StringComparison.Ordinal);
        Assert.Contains("public sealed class LauncherSettings", source, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class LauncherSettings", source, StringComparison.Ordinal);
        Assert.Contains("[JsonPropertyName(\"formatVersion\")]", source, StringComparison.Ordinal);
        Assert.Contains("[JsonPropertyName(\"games\")]", source, StringComparison.Ordinal);
        Assert.Contains("public Dictionary<string, GameSettings> Games", source, StringComparison.Ordinal);

        // 游戏自有字段只在游戏条目里声明；根类型保留的兼容投影必须是 [JsonIgnore]。
        Assert.DoesNotContain("[JsonPropertyName(\"gamePath\")]", source, StringComparison.Ordinal);
        Assert.Contains("[JsonPropertyName(\"gamePath\")]", gameSource, StringComparison.Ordinal);
        Assert.Contains("public string GamePath", gameSource, StringComparison.Ordinal);
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

}
