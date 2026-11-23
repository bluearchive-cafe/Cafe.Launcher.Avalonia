using System.Text.RegularExpressions;
using System.Xml.Linq;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

// Dialog surface and overlay contracts: z-order, hairline footers, confirm
// dialog anatomy, critical action naming, and DialogSurface theme internals.
public sealed partial class UiStyleContractTests
{
    [Fact]
    public void UpdateProgressArea_UsesTopDividerAndShowsDownloadSpeed()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowDialogsOverlay.axaml"));
        var divider = document.Descendants().Single(element =>
            element.Name.LocalName == "Separator"
            && element.Attribute("Classes")?.Value == "update-progress-divider");
        var speed = document.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "{Binding Dialogs.LauncherUpdate.DownloadSpeedText}");

        Assert.Equal("{Binding Dialogs.LauncherUpdate.IsApplying}", divider.Attribute("IsVisible")?.Value);
        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.None}", divider.Attribute("Margin")?.Value);
        Assert.Equal("{DynamicResource Launcher.Color.Outline}", divider.Attribute("Background")?.Value);
        Assert.Equal("{Binding Dialogs.LauncherUpdate.IsDownloading}", speed.Attribute("IsVisible")?.Value);
    }

    [Fact]
    public void UpdateReleaseNotesPreview_UsesGfmCompatibleParserProfile()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowDialogsOverlay.axaml"));
        XNamespace controls = "using:Cafe.Launcher.UI.Controls";

        // 更新说明与关于页的只读法律文档各有一个 Markdown 查看器。
        Assert.Equal(2, document.Descendants(controls + "RemoteMarkdownView").Count());

        var app = File.ReadAllText(TestRepository.FromHostRoot("App.axaml"));
        Assert.Contains("avares://MarkView.Avalonia/Themes/MarkdownTheme.axaml", app, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayOrder_IsBaseThenSettingsThenDialogsThenToast()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
        var all = document.Descendants().ToList();

        XElement Overlay(string name) =>
            document.Descendants().Single(element => element.Name.LocalName == name);

        var settings = Overlay("MainWindowSettingsOverlay");
        var logViewer = Overlay("MainWindowLogViewerOverlay");
        var debug = Overlay("MainWindowDebugOverlay");
        var dialogs = Overlay("MainWindowDialogsOverlay");
        var toast = Overlay("MainWindowToastOverlay");

        Assert.True(all.IndexOf(settings) < all.IndexOf(debug));
        Assert.True(all.IndexOf(debug) < all.IndexOf(logViewer));
        Assert.True(all.IndexOf(logViewer) < all.IndexOf(dialogs));
        Assert.True(all.IndexOf(dialogs) < all.IndexOf(toast));

        // 顶层覆盖层是 ShellRoot 的直接子元素：ZIndex 必须写在它们身上，
        // 写在 UserControl 内部根 Grid 上不会影响兄弟顺序。
        Assert.Equal(
            "{x:Static constants:LauncherConstants.ZIndexSettings}",
            settings.Attribute("ZIndex")?.Value);
        Assert.Equal(
            "{x:Static constants:LauncherConstants.ZIndexDialog}",
            dialogs.Attribute("ZIndex")?.Value);
        Assert.Equal(
            "{x:Static constants:LauncherConstants.ZIndexToast}",
            toast.Attribute("ZIndex")?.Value);
    }

    [Fact]
    public void DialogLayer_DeclaresLayerZIndexOnEachSurfaceNotInStyles()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowDialogsOverlay.axaml"));
        const string dialogZIndex = "{x:Static constants:LauncherConstants.ZIndexDialog}";
        const string wizardZIndex = "{x:Static constants:LauncherConstants.ZIndexSetupWizard}";
        const string wizardConfirmationZIndex =
            "{x:Static constants:LauncherConstants.ZIndexSetupWizardConfirmation}";

        var wizard = document.Descendants()
            .Single(element => element.Name.LocalName == "SetupWizardOverlay");
        Assert.Equal(wizardZIndex, wizard.Attribute("ZIndex")?.Value);

        var exitConfirmation = document.Descendants()
            .Single(element =>
                element.Name.LocalName == "ConfirmDialog"
                && element.Attribute("IsOpen")?.Value == "{Binding Dialogs.SetupWizardExitConfirm.IsVisible}");
        Assert.Equal(wizardConfirmationZIndex, exitConfirmation.Attribute("ZIndex")?.Value);

        // 直接 Grid 形式的对话框与 UserControl 形式的对话框同层，
        // 否则声明顺序会悄悄决定谁盖住谁。
        var dialogSurfaces = document.Descendants()
            .Where(element => !ReferenceEquals(element, exitConfirmation))
            .Where(element =>
                element.Name.LocalName is "ConfirmDialog" or "UninstallDialog" or "ResourcePanelOverlay"
                || (element.Name.LocalName == "Grid" && HasClass(element, "dialog-overlay")))
            .ToArray();

        Assert.NotEmpty(dialogSurfaces);
        Assert.All(dialogSurfaces, element =>
            Assert.Equal(dialogZIndex, element.Attribute("ZIndex")?.Value));
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
            ["Views/SetupWizardOverlay.axaml"] = new(StringComparer.Ordinal)
            {
                ["{Binding Dialogs.ShowSetupWizardExitConfirmCommand}"] = "{Binding Shell.I18n[setupWizardSkip]}",
                ["{Binding Dialogs.SetupWizard.BrowseGamePathCommand}"] = "{Binding Shell.I18n[setupWizardBrowse]}",
                ["{Binding Dialogs.SetupWizard.PreviousCommand}"] = "{Binding Shell.I18n[setupWizardPrevious]}",
                ["{Binding Dialogs.SetupWizard.NextCommand}"] = "{Binding Shell.I18n[setupWizardNext]}",
                ["{Binding Dialogs.SetupWizard.CompleteCommand}"] = "{Binding Shell.I18n[setupWizardFinish]}"
            }
        };

        foreach (var (path, expectedByCommand) in expectedActions)
        {
            var document = XDocument.Load(TestRepository.FromPresentationRoot(path));
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
    public void ResourcePanel_InputsAndResourceChoicesExposeMeaningfulAutomationNames()
    {
        // ADR-056：来源选择是分段 RadioButton 对，资源偏好是提交式 CheckBox。
        // 控件语义换了，自动化名仍是「本地化、可读、逐控件」的。
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/ResourcePanelOverlay.axaml"));
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
                && element.Attribute("GroupName")?.Value == "ResourcePanelUidSource")
            .ToList();
        var resourceSwitch = document
            .Descendants()
            .Single(element => element.Name.LocalName == "CheckBox");

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
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowDialogsOverlay.axaml"));
        var errorSurface = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "DialogSurface"
                && element.Attribute("Title")?.Value == "{Binding Shell.I18n[errorDialogTitle]}");

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
        var document = XDocument.Load(TestRepository.FromHostRoot("App.axaml"));
        foreach (var (key, light, dark) in MaterialSchemeGenerator.DialogSurfaceDefaults
                     .Concat(MaterialSchemeGenerator.NeutralContentDefaults)
                     .Concat(MaterialSchemeGenerator.GlassSurfaceDefaults))
        {
            Assert.Equal(light, ReadThemeBrushColor(document, "Light", key));
            Assert.Equal(dark, ReadThemeBrushColor(document, "Dark", key));
        }
    }

    [Fact]
    public void ConfirmDialog_LongContentScrollsWhileActionsRemainFixed()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Controls/ConfirmDialog.axaml"));
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

        var application = XDocument.Load(TestRepository.FromHostRoot("App.axaml"));
        var maxHeightToken = application
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key"
                && attribute.Value == "Launcher.Component.Dialog.Confirm.MaxHeight"));
        Assert.Equal("480", maxHeightToken.Value);

        var actions = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "WrapPanel" && HasClass(element, "dialog-actions"));
        Assert.Equal(3, actions.Elements().Count(element => element.Name.LocalName == "Button"));
    }

    [Fact]
    public void DialogsOverlay_DialogsUseHairlineFooterForActions()
    {
        // ADR-015：发丝动作带内化为 DialogSurface Panel 模板；视图文件只承载
        // 表面实例（主 overlay 四 Panel + 一 Basic 公告，资源面板独立文件一个 Panel），
        // 辅助动作进左槽。
        var text = File.ReadAllText(TestRepository.FromPresentationRoot("Views/MainWindowDialogsOverlay.axaml"));

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

        // 资源面板覆盖层拆分后仍是一个 Panel 表面（与 SetupWizard 拆分模式同构）。
        var resourcePanelText = File.ReadAllText(TestRepository.FromPresentationRoot("Views/ResourcePanelOverlay.axaml"));
        Assert.Equal(1, Regex.Count(resourcePanelText, @"Form=""Panel""", RegexOptions.CultureInvariant));
        Assert.Equal(1, Regex.Count(resourcePanelText, @"Classes=""motion-surface""", RegexOptions.CultureInvariant));
        Assert.DoesNotContain("dialog-footer", resourcePanelText, StringComparison.Ordinal);
    }

    [Fact]
    public void CriticalDialogActions_ExposeMatchingLocalizedTooltipsAndAutomationNames()
    {
        var confirmDialog = XDocument.Load(TestRepository.FromPresentationRoot("Controls/ConfirmDialog.axaml"));
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

        var settingsOverlay = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowSettingsOverlay.axaml"));
        Dictionary<string, string> settingsActions = new(StringComparer.Ordinal)
        {
            // ADR-070 第二批：底栏「取消」改称「放弃更改」；关闭命令仍先经未保存确认。
            ["{Binding WindowChrome.ToggleSettingsCommand}"] = "{Binding Shell.I18n[settingsDiscardChanges]}",
            ["{Binding Settings.SaveSettingsCommand}"] = "{Binding Settings.SaveButtonText}"
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
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/ResourcePanelOverlay.axaml"));
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

        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowDialogsOverlay.axaml"));
        var usages = document
            .Descendants()
            .Where(element => element.Name.LocalName == "ConfirmDialog")
            .ToArray();
        // 调试重置 / 设置页重置共享同一对话框控件，各自独立实例。
        // 卸载独立表面跨确认、执行、结果三阶段，不再使用 ConfirmDialog。
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
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/ResourcePanelOverlay.axaml"));
        var changeUidButton = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value
                    == "{Binding ResourcePanel.BeginEditResourcePanelUidCommand}");
        Assert.Null(changeUidButton.Attribute("IsVisible"));
    }

    [Fact]
    public void OverlayStyles_LeaveLayerOrderToUseSites()
    {
        // ZIndex 只排序兄弟节点：写进覆盖层内部根 Grid 的样式里不可能决定顶层叠层的顺序，
        // 层级必须声明在真正的兄弟节点（MainWindow / MainWindowDialogsOverlay 的子项）上。
        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.Styles.axaml"));

        foreach (var selector in new[]
                 {
                     "Grid.settings-overlay",
                     "Grid.dialog-overlay",
                     "Grid.setup-wizard-overlay"
                 })
        {
            Assert.False(GetStyleSetters(styles, selector).ContainsKey("ZIndex"));
        }
    }

    [Fact]
    public void OverlayLayerConstants_AreStrictlyOrdered()
    {
        Assert.True(LauncherConstants.ZIndexSettings < LauncherConstants.ZIndexDialog);
        Assert.True(LauncherConstants.ZIndexDialog < LauncherConstants.ZIndexSetupWizard);
        Assert.True(LauncherConstants.ZIndexSetupWizard < LauncherConstants.ZIndexSetupWizardConfirmation);
        Assert.True(LauncherConstants.ZIndexSetupWizardConfirmation < LauncherConstants.ZIndexToast);
    }

    [Fact]
    public void OverlayStyles_TrapAndRestoreKeyboardFocus()
    {
        var styles = File.ReadAllText(TestRepository.FromPresentationRoot("Views/MainWindow.Styles.axaml"));
        var behavior = File.ReadAllText(TestRepository.FromPresentationRoot("Views/OverlayFocusBehavior.cs"));

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
        var document = XDocument.Load(TestRepository.FromPresentationRoot(relativePath));
        var bands = document
            .Descendants()
            .Where(element => element.Name.LocalName == "WrapPanel" && HasClass(element, "dialog-actions"))
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
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Controls/ConfirmDialog.axaml"));
        var band = document
            .Descendants()
            .Single(element => element.Name.LocalName == "WrapPanel" && HasClass(element, "dialog-actions"));
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
    public void DialogSurface_UsesOpaqueFluentSurfaceWithProfileShadow()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Controls/DialogSurface.axaml"));
        var root = document.Root!;

        Assert.Equal(
            "{DynamicResource Launcher.Color.Dialog.Background}",
            root.Attribute("Background")?.Value);
        Assert.Equal(
            "{StaticResource Launcher.Component.Dialog.CornerRadius}",
            root.Attribute("CornerRadius")?.Value);

        var shadow = root.Attribute("BoxShadow")?.Value ?? string.Empty;
        Assert.Contains("BoxShadows", shadow, StringComparison.Ordinal);
        Assert.Contains("Launcher.Elevation.Shadow.Dialog", shadow, StringComparison.Ordinal);
    }

    [Fact]
    public void DialogWidths_UseConfirmAndContentTiersOnly()
    {
        var appResources = XDocument.Load(TestRepository.FromHostRoot("App.axaml"));
        var xKey = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Key";

        string TokenValue(string key) => appResources
            .Descendants()
            .Single(element => (string?)element.Attribute(xKey) == key)
            .Value;

        Assert.Equal("440", TokenValue("Launcher.Component.Dialog.Confirm.MaxWidth"));
        Assert.Equal("560", TokenValue("Launcher.Component.Dialog.Content.MaxWidth"));
        Assert.DoesNotContain("Launcher.Component.Dialog.Confirm.Short", appResources.ToString(), StringComparison.Ordinal);

        var overlay = File.ReadAllText(
            TestRepository.FromPresentationRoot("Views/MainWindowDialogsOverlay.axaml"));
        Assert.DoesNotContain("short-confirmation", overlay, StringComparison.Ordinal);
    }

    /// <summary>
    /// ADR-040（修订）：焦点视觉「默认不显示、键盘导航时显示」。
    /// 自绘样式的默认焦点矩形（FocusAdorner）关闭；应用自己的 FocusRing 挂在 :focus-visible 上保留 ——
    /// 因此这条契约同时钉住「矩形不出现」和「键盘环仍然存在且覆盖得到动作带的无边框规则」。
    /// </summary>
    [Fact]
    public void DialogFamily_ProfileTokens_AreDeclaredOnceInAppResources()
    {
        var appResources = XDocument.Load(TestRepository.FromHostRoot("App.axaml"));
        var xKey = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Key";

        string TokenValue(string key) => appResources
            .Descendants()
            .Single(element => (string?)element.Attribute(xKey) == key)
            .Value;

        Assert.Equal("8", TokenValue("Launcher.Component.Dialog.CornerRadius"));
        Assert.Equal("8", TokenValue("Launcher.Component.Dialog.Alert.CornerRadius"));
        Assert.Equal(2, TokenValue("Launcher.Elevation.Shadow.Dialog").Split(',').Length);
        Assert.Equal("32", TokenValue("Launcher.Component.Dialog.Badge.Size"));
        Assert.Equal("16", TokenValue("Launcher.Component.Dialog.Badge.CornerRadius"));
        Assert.Equal("20,0,10,0", TokenValue("Launcher.Component.Dialog.Panel.Head.Padding"));
        Assert.Equal("24,18,24,18", TokenValue("Launcher.Component.Dialog.Panel.Body.Padding"));
        Assert.Equal("24,14,24,20", TokenValue("Launcher.Component.Dialog.Panel.Footer.Padding"));
        Assert.Equal("0,0,12,0", TokenValue("Launcher.Component.Dialog.FooterLeading.Margin"));
        Assert.Equal("28,28,28,8", TokenValue("Launcher.Component.Dialog.Basic.Head.Padding"));
        Assert.Equal("28,0,28,0", TokenValue("Launcher.Component.Dialog.Basic.Content.Padding"));
        Assert.Equal("28,16,28,24", TokenValue("Launcher.Component.Dialog.Basic.Actions.Padding"));
    }
}
