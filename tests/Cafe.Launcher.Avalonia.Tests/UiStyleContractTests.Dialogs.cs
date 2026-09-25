using System.Text.RegularExpressions;
using System.Xml.Linq;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

// Dialog surface and overlay contracts: z-order, hairline footers, confirm
// dialog anatomy, critical action naming, and DialogSurface theme internals.
public sealed partial class UiStyleContractTests
{
    [Fact]
    public void UpdateProgressArea_UsesTopDividerAndShowsDownloadSpeed()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindowDialogsOverlay.axaml"));
        var divider = document.Descendants().Single(element =>
            element.Name.LocalName == "Separator"
            && element.Attribute("Classes")?.Value == "update-progress-divider");
        var speed = document.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "{Binding Dialogs.UpdateDownloadSpeedText}");

        Assert.Equal("{Binding Dialogs.IsUpdateApplying}", divider.Attribute("IsVisible")?.Value);
        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.None}", divider.Attribute("Margin")?.Value);
        Assert.Equal("{DynamicResource Launcher.Color.Outline}", divider.Attribute("Background")?.Value);
        Assert.Equal("{Binding Dialogs.IsUpdateDownloading}", speed.Attribute("IsVisible")?.Value);
    }

    [Fact]
    public void UpdateReleaseNotesPreview_UsesGfmCompatibleParserProfile()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindowDialogsOverlay.axaml"));
        XNamespace controls = "using:Cafe.Launcher.Avalonia.Controls";

        Assert.Single(document.Descendants(controls + "ReleaseNotesMarkdownViewer"));

        var styles = File.ReadAllText(TestRepository.FromApplicationRoot("Views/MainWindow.Styles.axaml"));
        Assert.Contains("controls|ReleaseNotesMarkdownViewer", styles, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource Launcher.Text.Primary}", styles, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource Launcher.Color.Content.Row}", styles, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource Launcher.Color.Card.Border}", styles, StringComparison.Ordinal);

        var app = File.ReadAllText(TestRepository.FromApplicationRoot("App.axaml"));
        Assert.Contains("avares://MarkView.Avalonia/Themes/MarkdownTheme.axaml", app, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayOrder_IsBaseThenSettingsThenDialogsThenToast()
    {
        var mainWindow = File.ReadAllText(TestRepository.FromApplicationRoot("Views/MainWindow.axaml"));
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
                     "Views/ResourcePanelOverlay.axaml",
                     "Views/MainWindowLogViewerOverlay.axaml",
                     "Views/SetupWizardOverlay.axaml"
                 })
        {
            var text = File.ReadAllText(TestRepository.FromApplicationRoot(relativePath));
            Assert.DoesNotContain("ZIndex=\"500\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ZIndex=\"1001\"", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SecondaryOverlays_CriticalActionsExposeLocalizedAutomationNames()
    {
        Dictionary<string, Dictionary<string, string>> expectedActions = new(StringComparer.Ordinal)
        {
            ["Views/ResourcePanelOverlay.axaml"] = new(StringComparer.Ordinal)
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
                ["{Binding LogExport.OpenCommand}"] = "{Binding Shell.I18n[exportLogs]}"
            },
            ["Views/MainWindowLogExportOverlay.axaml"] = new(StringComparer.Ordinal)
            {
                ["{Binding LogExport.CloseCommand}"] = "{Binding Shell.I18n[cancel]}",
                ["{Binding LogExport.ExportCommand}"] = "{Binding Shell.I18n[logExportConfirm]}"
            },
            ["Views/MainWindowToastOverlay.axaml"] = new(StringComparer.Ordinal)
            {
                ["{Binding #ToastOverlayRoot.((vm:MainWindowViewModel)DataContext).Toasts.DismissToastCommand}"] =
                    "{Binding #ToastOverlayRoot.((vm:MainWindowViewModel)DataContext).Shell.I18n[close]}"
            },
            ["Views/SetupWizardOverlay.axaml"] = new(StringComparer.Ordinal)
            {
                ["{Binding Dialogs.SetupWizardExitConfirm.ShowCommand}"] = "{Binding Shell.I18n[setupWizardSkip]}",
                ["{Binding Dialogs.SetupWizard.BrowseGamePathCommand}"] = "{Binding Shell.I18n[setupWizardBrowse]}",
                ["{Binding Dialogs.SetupWizard.PreviousCommand}"] = "{Binding Shell.I18n[setupWizardPrevious]}",
                ["{Binding Dialogs.SetupWizard.NextCommand}"] = "{Binding Shell.I18n[setupWizardNext]}",
                ["{Binding Dialogs.SetupWizard.CompleteCommand}"] = "{Binding Shell.I18n[setupWizardFinish]}"
            }
        };

        foreach (var (path, expectedByCommand) in expectedActions)
        {
            var document = XDocument.Load(TestRepository.FromApplicationRoot(path));
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
        // ADR-041：来源选择是分段 RadioButton 对，条目开关是 ToggleSwitch——
        // 控件语义换了，自动化名仍是「本地化、可读、逐控件」的。
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/ResourcePanelOverlay.axaml"));
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
        var uidSourceSegments = resourcePanel
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "RadioButton"
                && HasClass(element, "segment-option"))
            .ToList();
        var resourceSwitch = document
            .Descendants()
            .Single(element => element.Name.LocalName == "ToggleSwitch");

        Assert.Equal(2, uidInputs.Count);
        Assert.All(uidInputs, input => Assert.Equal(
            "{Binding Shell.I18n[resourcePanelUid]}",
            input.Attributes().SingleOrDefault(attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name")?.Value));
        Assert.Equal(2, uidSourceSegments.Count);
        Assert.All(uidSourceSegments, segment => Assert.True(
            new[]
            {
                "{Binding Shell.I18n[resourcePanelUidSourceAuto]}",
                "{Binding Shell.I18n[resourcePanelUidSourceCustom]}"
            }.Contains(segment.Attributes().SingleOrDefault(attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name")?.Value)));
        Assert.Equal(
            "{Binding DisplayName}",
            resourceSwitch.Attributes().SingleOrDefault(attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name")?.Value);
    }

    [Fact]
    public void ErrorDialog_HeaderProvidesLocalizedCloseAction() // ADR-015 dialog surface anatomy
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindowDialogsOverlay.axaml"));
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
        var document = XDocument.Load(TestRepository.FromApplicationRoot("App.axaml"));
        foreach (var (key, light, dark) in MaterialSchemeGenerator.DialogSurfaceDefaults.Concat(MaterialSchemeGenerator.NeutralContentDefaults))
        {
            Assert.Equal(light, ReadThemeBrushColor(document, "Light", key));
            Assert.Equal(dark, ReadThemeBrushColor(document, "Dark", key));
        }
    }

    [Fact]
    public void ToastCloseButton_WhenRendered_UsesLocalizedAutomationNameAndToolTip()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindowToastOverlay.axaml"));
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
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Controls/ConfirmDialog.axaml"));
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

        var application = XDocument.Load(TestRepository.FromApplicationRoot("App.axaml"));
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
        // 表面实例（主 overlay 三 Panel + 一 Basic 公告，资源面板独立文件一个 Panel），
        // 辅助动作进左槽。
        var text = File.ReadAllText(TestRepository.FromApplicationRoot("Views/MainWindowDialogsOverlay.axaml"));

        Assert.Equal(2, Regex.Count(text, @"Form=""Panel""", RegexOptions.CultureInvariant));
        Assert.Equal(1, Regex.Count(text, @"Form=""Basic""", RegexOptions.CultureInvariant));
        Assert.Equal(3, Regex.Count(text, @"Classes=""motion-surface""", RegexOptions.CultureInvariant));

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

        Assert.Equal(2, panels);
        Assert.Matches(
            """(?s)<controls:DialogSurface\b[^>]*Form="Panel"[^>]*>.*?<controls:DialogSurface\.FooterLeading>.*?</controls:DialogSurface\.FooterLeading>.*?</controls:DialogSurface>""",
            text);

        // 资源面板覆盖层拆分后仍是一个 Panel 表面（与 SetupWizard 拆分模式同构）。
        var resourcePanelText = File.ReadAllText(TestRepository.FromApplicationRoot("Views/ResourcePanelOverlay.axaml"));
        Assert.Equal(1, Regex.Count(resourcePanelText, @"Form=""Panel""", RegexOptions.CultureInvariant));
        Assert.Equal(1, Regex.Count(resourcePanelText, @"Classes=""motion-surface""", RegexOptions.CultureInvariant));
        Assert.DoesNotContain("dialog-footer", resourcePanelText, StringComparison.Ordinal);
    }

    [Fact]
    public void CriticalDialogActions_ExposeMatchingLocalizedTooltipsAndAutomationNames()
    {
        var confirmDialog = XDocument.Load(TestRepository.FromApplicationRoot("Controls/ConfirmDialog.axaml"));
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

        var settingsOverlay = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindowSettingsOverlay.axaml"));
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
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/ResourcePanelOverlay.axaml"));
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
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindow.Styles.axaml"));
        var focus = GetStyleSetters(document, "Button.dialog-close:focus-visible");

        Assert.Equal("{DynamicResource Launcher.Color.Primary.Soft}", focus["Background"]);
        Assert.Equal("{DynamicResource Launcher.Color.Primary}", focus["BorderBrush"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Default}", focus["BorderThickness"]);
    }

    [Fact]
    public void ConfirmDialogs_UseBasicMessageAndFilledPrimaryActions()
    {
        var control = XDocument.Load(TestRepository.FromApplicationRoot("Controls/ConfirmDialog.axaml"));
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

        // 可选行（ADR-030）：勾选项必须带本地化名（辅助技术要读得出这个破坏性选项），
        // 且状态双向绑定（用户勾了要能读回来）。
        var option = control
            .Descendants()
            .Single(element => element.Name.LocalName == "CheckBox");
        Assert.Equal("{Binding IsOptionChecked, ElementName=Root, Mode=TwoWay}", option.Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding OptionText, ElementName=Root}", option.Attribute("Content")?.Value);
        Assert.Equal(
            "{Binding OptionText, ElementName=Root}",
            option.Attribute("AutomationProperties.Name")?.Value);

        var styles = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindow.Styles.axaml"));
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

        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindowDialogsOverlay.axaml"));
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
    public void ResourcePanel_ChangeUidAction_RemainsVisibleForAutoSource()
    {
        // 修改 UID 入口常驻：自动获取来源下也必须可见，避免"先切来源才能改 UID"的隐藏操作链。
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/ResourcePanelOverlay.axaml"));
        var changeUidButton = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value
                    == "{Binding ResourcePanel.BeginEditResourcePanelUidCommand}");
        Assert.Null(changeUidButton.Attribute("IsVisible"));
    }

    [Fact]
    public void ResourcePanel_StatusStripHasVisibleSurfaceAndBorder()
    {
        var dialogs = XDocument.Load(TestRepository.FromApplicationRoot("Views/ResourcePanelOverlay.axaml"));
        var statusStrip = dialogs
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "resource-panel-status"));
        Assert.True(HasClass(statusStrip, "info-strip"));
        // 严重度经 Classes.danger 绑定驱动；UID 事实只在展示卡渲染，状态条只承载消息。
        Assert.Equal(
            "{Binding ResourcePanel.IsResourcePanelMessageError}",
            statusStrip.Attribute("Classes.danger")?.Value);
        Assert.DoesNotContain(
            statusStrip.Descendants(),
            element => element.Attribute("Text")?.Value == "{Binding ResourcePanel.ResourcePanelUidText}");

        var styles = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindow.Styles.axaml"));
        var statusStyle = GetStyleSetters(styles, "Border.info-strip.resource-panel-status");
        Assert.Equal(
            "{DynamicResource Launcher.Color.Content.Row}",
            statusStyle["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Primary.Border}",
            statusStyle["BorderBrush"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Default}", statusStyle["BorderThickness"]);

        var dangerStyle = GetStyleSetters(styles, "Border.info-strip.danger");
        Assert.Equal("{DynamicResource Launcher.Color.Danger.Soft}", dangerStyle["Background"]);
        Assert.Equal("{DynamicResource Launcher.Color.Danger}", dangerStyle["BorderBrush"]);
    }

    [Fact]
    public void ResourcePanel_UidCard_IsSingleCardWithThreeMutuallyExclusiveStates()
    {
        // ADR-041：三张互斥卡并成一张卡内的三个互斥可见面板；共享 dialog-card 类不动，
        // 圆角升档由资源面板专属的 uid-card 类承担。
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/ResourcePanelOverlay.axaml"));
        var uidCard = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "uid-card"));
        Assert.True(HasClass(uidCard, "dialog-card"));

        var stateBindings = new[]
        {
            "{Binding ResourcePanel.IsResourcePanelUidMissing}",
            "{Binding ResourcePanel.IsResourcePanelUidEditing}",
            "{Binding ResourcePanel.IsResourcePanelUidPresent}"
        };
        foreach (var binding in stateBindings)
        {
            Assert.Single(uidCard.Descendants(), element =>
                element.Attribute("IsVisible")?.Value == binding);
        }

        var styleSetters = GetStyleSetters(document, "Border.uid-card");
        Assert.Equal(
            "{StaticResource Launcher.Radius.Md}",
            styleSetters["CornerRadius"]);
    }

    [Fact]
    public void ResourcePanel_UidSource_IsSegmentedPairWithoutComboBox()
    {
        // ADR-041：两个互斥选项直接常显为 MD3 分段按钮；ComboBox 与其专属 token 退场。
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/ResourcePanelOverlay.axaml"));
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Name.LocalName == "ComboBox");
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Attribute("MinWidth")?.Value
                == "{StaticResource Launcher.Component.ResourcePanel.UidSource.MinWidth}");

        var segments = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "RadioButton"
                && HasClass(element, "segment-option"))
            .ToArray();
        Assert.Equal(2, segments.Length);
        Assert.All(segments, segment => Assert.Equal(
            "ResourcePanelUidSource",
            segment.Attribute("GroupName")?.Value));
        Assert.Contains(segments, segment => segment.Attribute("IsChecked")?.Value
            .Contains("ConverterParameter={x:Static models:ResourcePanelUidSources.Auto}", StringComparison.Ordinal) == true);
        Assert.Contains(segments, segment => segment.Attribute("IsChecked")?.Value
            .Contains("ConverterParameter={x:Static models:ResourcePanelUidSources.Custom}", StringComparison.Ordinal) == true);
        Assert.All(segments, segment => Assert.True(
            segment.Attribute("IsChecked")?.Value
                .Contains("Converter={x:Static converters:ResourcePanelSourceSegmentConverter.Instance}", StringComparison.Ordinal) == true
            && segment.Attribute("IsChecked")?.Value.EndsWith(", Mode=TwoWay}", StringComparison.Ordinal) == true));

        // busy 时整组禁用（分段容器承载 IsEnabled，与旧 ComboBox 一致）。
        var segmented = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "segmented"));
        Assert.Equal(
            "{Binding ResourcePanel.IsResourcePanelBusy, Converter={x:Static BoolConverters.Not}}",
            segmented.Attribute("IsEnabled")?.Value);

        // 选中段 = accent 家族（Primary.Soft 底 + Primary 字），不是静态 SecondaryContainer。
        var checkedStyle = GetStyleSetters(document, "RadioButton.segment-option:checked");
        Assert.Equal("{DynamicResource Launcher.Color.Primary.Soft}", checkedStyle["Background"]);
        Assert.Equal("{DynamicResource Launcher.Color.Primary}", checkedStyle["Foreground"]);
    }

    [Fact]
    public void ResourcePanel_ResourceEntries_UseSingleCardRowsWithSwitchAndChip()
    {
        // ADR-041：三张条目卡并成一张卡的三行（显式发丝分隔线），开关换 Switch，
        // 状态装进 chip（底色 + 图标 + 文案，不只靠颜色）。
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/ResourcePanelOverlay.axaml"));
        var resourceCard = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "resource-card"));
        Assert.True(HasClass(resourceCard, "dialog-card"));
        Assert.Equal(
            "{StaticResource Launcher.Radius.Md}",
            GetStyleSetters(document, "Border.resource-card")["CornerRadius"]);

        var template = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "DataTemplate"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "ResourcePanelItemRowTemplate"));
        Assert.DoesNotContain(
            template.Descendants(),
            element => element.Name.LocalName == "CheckBox");
        var resourceSwitch = template
            .Descendants()
            .Single(element => element.Name.LocalName == "ToggleSwitch");
        Assert.Equal("{Binding IsEnabled, Mode=TwoWay}", resourceSwitch.Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding IsOperable}", resourceSwitch.Attribute("IsEnabled")?.Value);

        var chip = template
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "status-chip"));
        Assert.Equal(
            "{Binding IsStatusLoading}",
            chip.Attribute("Classes.loading")?.Value);
        Assert.Equal(
            "{Binding IsStatusReady}",
            chip.Attribute("Classes.ready")?.Value);
        Assert.Equal(
            "{Binding IsStatusWaiting}",
            chip.Attribute("Classes.waiting")?.Value);
        Assert.Equal(
            "{Binding IsStatusFailed}",
            chip.Attribute("Classes.failed")?.Value);

        // 三行两线：分隔线是行与行之间的显式发丝元素（与向导复核列表同一手法）。
        Assert.Equal(3, resourceCard.Descendants().Count(element =>
            element.Name.LocalName == "ContentControl"));
        Assert.Equal(2, resourceCard.Descendants().Count(element =>
            element.Name.LocalName == "Border"
            && HasClass(element, "resource-row-divider")));
    }

    [Fact]
    public void ResourcePanel_StatusChipStyles_MapStatusClassesToSemanticColors()
    {
        // ADR-041 提案 1：Ready=Success（配新增 Success.Soft 底），Failed=Danger，
        // Loading/Waiting 用中性的 Content.Row 底与 Text.Secondary 前景。
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/ResourcePanelOverlay.axaml"));

        var baseStyle = GetStyleSetters(document, "Border.status-chip");
        Assert.Equal("{DynamicResource Launcher.Color.Content.Row}", baseStyle["Background"]);
        Assert.Equal("{StaticResource Launcher.Radius.Full}", baseStyle["CornerRadius"]);

        var readyBackground = GetStyleSetters(document, "Border.status-chip.ready");
        Assert.Equal("{DynamicResource Launcher.Color.Success.Soft}", readyBackground["Background"]);
        var readyIcon = GetStyleSetters(document, "Border.status-chip.ready materialIcons|MaterialIcon.status-chip-icon");
        Assert.Equal("{DynamicResource Launcher.Color.Success}", readyIcon["Foreground"]);

        var failedBackground = GetStyleSetters(document, "Border.status-chip.failed");
        Assert.Equal("{DynamicResource Launcher.Color.Danger.Soft}", failedBackground["Background"]);
        var failedText = GetStyleSetters(document, "Border.status-chip.failed TextBlock.status-chip-text");
        Assert.Equal("{StaticResource Launcher.Color.Danger}", failedText["Foreground"]);
    }

    [Fact]
    public void OverlayStyles_DefineSettingsDialogAndSetupWizardLayerOrder()
    {
        var styles = File.ReadAllText(TestRepository.FromApplicationRoot("Views/MainWindow.Styles.axaml"));

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
        var styles = File.ReadAllText(TestRepository.FromApplicationRoot("Views/MainWindow.Styles.axaml"));
        var behavior = File.ReadAllText(TestRepository.FromApplicationRoot("Views/OverlayFocusBehavior.cs"));

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
        // ADR-040（修订）：自动聚焦与归还焦点都不是键盘导航 —— 必须用 Unspecified，
        // 否则打开叠层的一瞬间就会画出焦点环（Tab 会把焦点判成键盘导航）。
        Assert.Contains("?.Focus(NavigationMethod.Unspecified)", behavior, StringComparison.Ordinal);
        Assert.Contains("focus?.Focus(NavigationMethod.Unspecified)", behavior, StringComparison.Ordinal);
    }

    [Fact]
    public void DialogSurface_ControlTheme_CarriesAnatomyPartsAndProfileTokens()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/Styles/DialogSurface.axaml"));
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

    /// <summary>
    /// ADR-040：动作顺序按 Fluent/WinUI —— do-it（primary/danger）在安全动作（flat）之前。
    /// 这是一条不变量而非逐文件断言：任何新的动作带只要同时含两类动作就受约束。
    /// </summary>
    [Theory]
    [InlineData("Controls/ConfirmDialog.axaml")]
    [InlineData("Views/MainWindowDialogsOverlay.axaml")]
    [InlineData("Views/MainWindowLogExportOverlay.axaml")]
    [InlineData("Views/MainWindowLogViewerOverlay.axaml")]
    [InlineData("Views/ResourcePanelOverlay.axaml")]
    [InlineData("Views/MainWindowDebugOverlay.axaml")]
    [InlineData("Views/DesignGalleryOverlay.axaml")]
    public void DialogActionBands_PutDoItActionsBeforeSafeActions(string relativePath)
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot(relativePath));
        var bands = document
            .Descendants()
            .Where(element => element.Name.LocalName == "StackPanel" && HasClass(element, "confirm-actions"))
            .ToArray();

        Assert.NotEmpty(bands);

        foreach (var band in bands)
        {
            var buttons = band.Descendants().Where(element => element.Name.LocalName == "Button").ToList();
            var lastDoIt = buttons.FindLastIndex(button =>
                HasClass(button, "primary-action") || HasClass(button, "danger-action"));
            var firstSafe = buttons.FindIndex(button => HasClass(button, "flat-action"));

            if (lastDoIt < 0 || firstSafe < 0)
            {
                continue;
            }

            Assert.True(
                lastDoIt < firstSafe,
                $"{relativePath}: do-it 动作必须在安全动作之前（do-it #{lastDoIt}, safe #{firstSafe}）。");
        }
    }

    /// <summary>ADR-040：确认框的动作顺序（确认 → 危险确认 → 取消）。</summary>
    [Fact]
    public void ConfirmDialog_PlacesConfirmAndDangerBeforeCancel()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Controls/ConfirmDialog.axaml"));
        var band = document
            .Descendants()
            .Single(element => element.Name.LocalName == "StackPanel" && HasClass(element, "confirm-actions"));
        var names = band
            .Elements()
            .Where(element => element.Name.LocalName == "Button")
            .Select(element => element.Attributes().Single(attribute =>
                attribute.Name.LocalName == "Name").Value)
            .ToArray();

        Assert.Equal(["PrimaryActionButton", "DangerActionButton", "SafeActionButton"], names);
    }

    /// <summary>
    /// ADR-040（修订）：对话框动作按钮不画边框 —— 底色即形状。焦点视觉由全局开关统一关掉
    /// （见 <c>GlobalFocusVisual_IsDisabledAppWide</c>）。
    /// </summary>
    [Fact]
    public void DialogActionButtons_UseNeutralFillAndNoVisibleBorder()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindow.Styles.axaml"));

        const string neutralFill = "{DynamicResource Launcher.Color.Dialog.Action.Background}";
        var family = GetStyleSetters(document, "Button.confirm-dialog-action");
        Assert.Equal(neutralFill, family["Background"]);
        // 家族规则不再自己声明描边：无边框外观与预留厚度都由动作带那条规则统一给。
        Assert.DoesNotContain("BorderThickness", family.Keys);
        Assert.DoesNotContain("BorderBrush", family.Keys);

        var bandStandard = GetStyleSetters(document, "StackPanel.confirm-actions Button.flat-action");
        Assert.Equal(neutralFill, bandStandard["Background"]);

        // 新 token 必须同时落在两套 ThemeDictionary 里（生成器的中性重置表由
        // AppDialogSurfaceTokens_MatchGeneratorDeclaredDefaults 成对守护）。
        var application = XDocument.Load(TestRepository.FromApplicationRoot("App.axaml"));
        Assert.Equal("#FFF4F8FC", ReadThemeBrushColor(application, "Light", "Launcher.Color.Dialog.Action.Background"));
        Assert.Equal("#FF222B38", ReadThemeBrushColor(application, "Dark", "Launcher.Color.Dialog.Action.Background"));
    }

    /// <summary>
    /// ADR-040（修订）：焦点视觉「默认不显示、键盘导航时显示」。
    /// 框架默认焦点矩形（FocusAdorner）全局清掉；应用自己的 FocusRing 挂在 :focus-visible 上保留 ——
    /// 因此这条契约同时钉住「矩形不出现」和「键盘环仍然存在且覆盖得到动作带的无边框规则」。
    /// </summary>
    [Fact]
    public void FocusVisual_IsKeyboardOnly()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot("Views/MainWindow.Styles.axaml"));
        var styles = document.Descendants().Where(element => element.Name.LocalName == "Style").ToList();

        // 1. 框架默认焦点矩形：全局清掉（它不是本仓语言，且会与 FocusRing 叠成双层框）。
        Assert.Equal("{x:Null}", GetStyleSetters(document, "Control")["FocusAdorner"]);

        // 2. 应用自己的焦点环：仍然挂在 :focus-visible 上（没有被抹掉）。
        Assert.Equal(
            "{DynamicResource Launcher.Color.FocusRing}",
            GetStyleSetters(document, "Button:focus-visible")["BorderBrush"]);
        Assert.Equal(
            "{StaticResource Launcher.Border.Thickness.Focus}",
            GetStyleSetters(document, "Button:focus-visible")["BorderThickness"]);

        // 3. 动作带按钮：可见边框为零，但焦点环的厚度**常驻预留**（静止时描边透明），
        //    所以聚焦只改颜色、不改几何 —— 否则聚焦那一刻按钮会变宽、右对齐的动作带整体位移。
        var bandReserve = GetStyleSetters(document, "StackPanel.confirm-actions Button");
        Assert.Equal("{StaticResource Launcher.Color.Transparent}", bandReserve["BorderBrush"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Focus}", bandReserve["BorderThickness"]);

        var bandFocus = GetStyleSetters(document, "StackPanel.confirm-actions Button:focus-visible");
        Assert.Equal("{DynamicResource Launcher.Color.FocusRing}", bandFocus["BorderBrush"]);
        Assert.DoesNotContain(
            "BorderThickness",
            bandFocus.Keys); // 聚焦不许再动厚度（这条是「宽度不变」的样式侧守卫）

        var bandReserveIndex = styles.FindIndex(element =>
            element.Attribute("Selector")?.Value == "StackPanel.confirm-actions Button");
        var borderlessIndex = styles.FindIndex(element =>
            element.Attribute("Selector")?.Value == "StackPanel.confirm-actions Button.flat-action");
        var bandFocusIndex = styles.FindIndex(element =>
            element.Attribute("Selector")?.Value == "StackPanel.confirm-actions Button:focus-visible");
        Assert.True(
            bandReserveIndex > borderlessIndex,
            "预留厚度的规则必须排在 flat-action 的静止样式之后（Avalonia 取最后一个匹配的 Setter）。");
        Assert.True(bandFocusIndex > bandReserveIndex, "颜色规则必须排在预留厚度之后。");

        // 4. 程序式聚焦不得被判成键盘导航：源码里不能出现 NavigationMethod.Tab。
        foreach (var relativePath in new[] { "Views/OverlayFocusBehavior.cs", "Controls/ConfirmDialog.axaml.cs" })
        {
            var source = File.ReadAllText(TestRepository.FromApplicationRoot(relativePath));
            Assert.DoesNotContain("Focus(NavigationMethod.Tab)", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DialogFamily_ProfileTokens_AreDeclaredOnceInAppResources()
    {
        var appResources = XDocument.Load(TestRepository.FromApplicationRoot("App.axaml"));
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
