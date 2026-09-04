using System.Text.RegularExpressions;
using System.Xml.Linq;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

// Dialog surface and overlay contracts: z-order, hairline footers, confirm
// dialog anatomy, critical action naming, and DialogSurface theme internals.
public sealed partial class UiStyleContractTests
{
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
}
