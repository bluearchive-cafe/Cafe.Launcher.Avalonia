using System.Xml.Linq;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

// Setup wizard contracts: overlay inclusion, step swap choreography, review
// rows, game-path status styling, and wizard action/option row tokens.
public sealed partial class UiStyleContractTests
{
    [Fact]
    public void SetupWizardStyles_CoverFilledDisabledAndOptionFocusStates()
    {
        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/SetupWizard.axaml"));
        var disabled = GetStyleSetters(styles, "Button.wizard-action:disabled");
        Assert.Equal("{DynamicResource Launcher.Color.Content.Row}", disabled["Background"]);
        Assert.Equal("{DynamicResource Launcher.Text.Secondary}", disabled["Foreground"]);
        Assert.DoesNotContain("BorderThickness", disabled.Keys);
        var focus = GetStyleSetters(styles, "RadioButton.wizard-option:focus-visible");
        Assert.Equal("{DynamicResource Launcher.Color.FocusRing}", focus["BorderBrush"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Focus}", focus["BorderThickness"]);
        // 自绘模板不继承 Fluent 的按压/禁用外观：两态必须显式补齐。
        var pressed = GetStyleSetters(styles, "RadioButton.wizard-option:pressed");
        Assert.Equal("{DynamicResource Launcher.Color.Button.Secondary.Pressed}", pressed["Background"]);
        var selectedPressed = GetStyleSetters(styles, "RadioButton.wizard-option:checked:pressed");
        Assert.Equal("{DynamicResource Launcher.Color.Primary.Soft}", selectedPressed["Background"]);
        var optionDisabled = GetStyleSetters(styles, "RadioButton.wizard-option:disabled");
        Assert.Equal("{StaticResource Launcher.StateLayer.Disabled.Content}", optionDisabled["Opacity"]);
    }

    [Fact]
    public void SetupWizardOverlay_IsDedicatedViewIncludedByDialogsOverlay()
    {
        var dialogsOverlay = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowDialogsOverlay.axaml"));

        Assert.Single(
            dialogsOverlay.Descendants(),
            element => element.Name.LocalName == "SetupWizardOverlay");
        Assert.DoesNotContain(
            "Dialogs.SetupWizard.NextCommand",
            dialogsOverlay.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void SetupWizardOverlay_UsesFullBleedHeaderStageAndFooter()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SetupWizardOverlay.axaml"));

        // 统一舞台（ADR-059）：无侧栏导航；全幅头部承载标题、步骤计数、跳过与七段进度轨。
        Assert.DoesNotContain(
            document.Descendants(),
            element => HasClass(element, "settings-navigation-pane"));
        var skipButton = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding Dialogs.ShowSetupWizardExitConfirmCommand}");
        var headerBorder = skipButton.Ancestors().Single(element =>
            element.Name.LocalName == "Border"
            && HasClass(element, "wizard-header"));
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

        // 七段进度轨：段位到达态由后置代码翻转；单步的协议更新模式整体折叠。
        var track = Assert.Single(headerBorder.Descendants(), element =>
            element.Name.LocalName == "Grid"
            && element.Attribute("ColumnDefinitions")?.Value == "*,*,*,*,*,*,*");
        Assert.Equal(
            "{Binding !Dialogs.SetupWizard.IsConsentUpdateMode}",
            track.Attribute("IsVisible")?.Value);
        Assert.Equal(
            new[]
            {
                "ProgressSegment0", "ProgressSegment1", "ProgressSegment2",
                "ProgressSegment3", "ProgressSegment4", "ProgressSegment5",
                "ProgressSegment6",
            },
            track.Elements().Select(element =>
                element.Attribute(xNamespace + "Name")?.Value));
        Assert.All(
            track.Elements(),
            segment => Assert.True(HasClass(segment, "wizard-progress-segment")));

        // 表面五段：头部 / 发丝线 / 滚动舞台 / 发丝线 / 全幅动作带。
        var contentBlock = headerBorder.Parent!;
        Assert.Equal("Grid", contentBlock.Name.LocalName);
        Assert.Equal("Auto,Auto,*,Auto,Auto", contentBlock.Attribute("RowDefinitions")?.Value);
        var dividers = contentBlock
            .Elements()
            .Where(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "wizard-divider"))
            .ToList();
        Assert.Equal(2, dividers.Count);
        Assert.Equal("1", dividers[0].Attribute("Grid.Row")?.Value);
        Assert.Equal("3", dividers[1].Attribute("Grid.Row")?.Value);
        var scroll = Assert.Single(contentBlock.Elements(), element =>
            element.Name.LocalName == "ScrollViewer"
            && element.Attribute(xNamespace + "Name")?.Value == "StepScroll");
        Assert.Equal("2", scroll.Attribute("Grid.Row")?.Value);

        // 动作带全幅贴底：不再使用 DialogSurface.Footer（空动作带会残留发丝线）。
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Name.LocalName == "DialogSurface.Footer");
        var footer = Assert.Single(contentBlock.Elements(), element =>
            element.Name.LocalName == "Border"
            && HasClass(element, "wizard-footer"));
        Assert.Equal("4", footer.Attribute("Grid.Row")?.Value);
        var actions = Assert.Single(footer.Elements(), element => element.Name.LocalName == "Grid");
        Assert.Equal("Auto,*,Auto", actions.Attribute("ColumnDefinitions")?.Value);
        Assert.All(
            actions.Elements().Where(element => element.Name.LocalName == "Button"),
            button => Assert.True(HasClass(button, "wizard-action")));
        Assert.Contains(actions.Elements(), element =>
            element.Name.LocalName == "Button"
            && !HasClass(element, "primary-action")
            && element.Attribute("Command")?.Value == "{Binding Dialogs.SetupWizard.PreviousCommand}");
        Assert.Contains(actions.Elements(), element =>
            element.Name.LocalName == "Button"
            && HasClass(element, "primary-action")
            && element.Attribute("Command")?.Value == "{Binding Dialogs.SetupWizard.NextCommand}");
        Assert.Contains(actions.Elements(), element =>
            element.Name.LocalName == "Button"
            && HasClass(element, "primary-action")
            && element.Attribute("Command")?.Value == "{Binding Dialogs.SetupWizard.CompleteCommand}");

        // 头部与动作带贴表面边缘：留白全部由样式消费 token（视图禁止内联 Padding）。
        var wizardStyles = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/SetupWizard.axaml"));
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Header.Padding}",
            GetStyleSetters(wizardStyles, "Border.wizard-header")["Padding"]);
        var footerStyle = GetStyleSetters(wizardStyles, "Border.wizard-footer");
        Assert.Equal("{StaticResource Launcher.Component.Wizard.Footer.Height}", footerStyle["Height"]);
        Assert.Equal("{StaticResource Launcher.Component.Wizard.Footer.Padding}", footerStyle["Padding"]);
        var segmentStyle = GetStyleSetters(wizardStyles, "Border.wizard-progress-segment");
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Progress.Height}",
            segmentStyle["Height"]);
        // 未到达段取发丝线一级的弱底，才在 Dialog.Header 底色上可辨。
        Assert.Equal(
            "{DynamicResource Launcher.Color.Dialog.Section.Divider}",
            segmentStyle["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Primary}",
            GetStyleSetters(wizardStyles, "Border.wizard-progress-segment.reached")["Background"]);
    }

    [Fact]
    public void SetupWizardOverlay_UsesSetupWizardLayerBetweenDialogsAndToast()
    {
        var overlay = XDocument.Load(TestRepository.FromPresentationRoot("Views/SetupWizardOverlay.axaml"));
        var host = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowDialogsOverlay.axaml"));

        Assert.Contains(
            overlay.Descendants(),
            element => HasClass(element, "setup-wizard-overlay"));

        // 层级落在宿主里的兄弟节点（SetupWizardOverlay 元素）上，而非其内部根 Grid。
        var wizard = host
            .Descendants()
            .Single(element => element.Name.LocalName == "SetupWizardOverlay");
        Assert.Equal(
            "{x:Static constants:LauncherConstants.ZIndexSetupWizard}",
            wizard.Attribute("ZIndex")?.Value);
    }

    [Fact]
    public void SetupWizardWelcome_PreviewsTheFiveConfigurationSteps()
    {
        // 欢迎页是首步：左引导只承担欢迎语义（HandWave 徽章 + 标题 + 说明），
        // 右面板按后续步骤顺序列五行「图标块 + 名称 + 一句话」，行间发丝线分界。
        var overlay = XDocument.Load(TestRepository.FromPresentationRoot("Views/SetupWizardOverlay.axaml"));
        var xNamespace = overlay.Root?.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNamespace);
        var welcomeStep = overlay
            .Descendants()
            .Single(element => element.Attribute(xNamespace + "Name")?.Value == "WizardStep0");

        var badgeIcon = Assert.Single(welcomeStep.Descendants(), element =>
            element.Name.LocalName == "MaterialIcon"
            && element.Ancestors().Any(ancestor => HasClass(ancestor, "wizard-context-icon")));
        Assert.Equal("HandWave", badgeIcon.Attribute("Kind")?.Value);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Primary}",
            badgeIcon.Attribute("Foreground")?.Value);
        Assert.Contains(welcomeStep.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "{Binding Shell.I18n[setupWizardWelcomeText]}");

        var panel = Assert.Single(welcomeStep.Elements(), element =>
            element.Name.LocalName == "Border"
            && HasClass(element, "wizard-panel"));
        var planCaption = Assert.Single(panel.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && HasClass(element, "caption")
            && element.Attribute("Text")?.Value == "{Binding Shell.I18n[setupWizardWelcomePlan]}");
        Assert.NotNull(planCaption);

        var rows = panel
            .Descendants()
            .Where(element => HasClass(element, "wizard-welcome-row"))
            .ToList();
        Assert.Equal(5, rows.Count);
        Assert.Equal(
            new[] { "Translate", "ScriptText", "FolderSearch", "CloudDownload", "SwapHorizontal" },
            rows.Select(row => Assert.Single(
                    row.Descendants(),
                    element => element.Name.LocalName == "MaterialIcon"
                        && element.Ancestors().Any(ancestor => HasClass(ancestor, "wizard-welcome-icon")))
                .Attribute("Kind")?.Value));
        Assert.Equal(
            new[]
            {
                "{Binding Shell.I18n[setupWizardLanguage]}",
                "{Binding Shell.I18n[agreementTitle]}",
                "{Binding Shell.I18n[setupWizardGamePath]}",
                "{Binding Shell.I18n[setupWizardDownloadSource]}",
                "{Binding Shell.I18n[setupWizardProxy]}",
            },
            rows.Select(row => Assert.Single(row.Descendants(), element =>
                    element.Name.LocalName == "TextBlock"
                    && HasClass(element, "wizard-welcome-name"))
                .Attribute("Text")?.Value));
        Assert.Equal(
            new[]
            {
                "{Binding Shell.I18n[setupWizardLanguageHint]}",
                "{Binding Shell.I18n[setupWizardWelcomeAgreementDescription]}",
                "{Binding Shell.I18n[setupWizardWelcomeGamePathDescription]}",
                "{Binding Shell.I18n[setupWizardWelcomeDownloadSourceDescription]}",
                "{Binding Shell.I18n[setupWizardWelcomeProxyDescription]}",
            },
            rows.Select(row => Assert.Single(row.Descendants(), element =>
                    element.Name.LocalName == "TextBlock"
                    && HasClass(element, "wizard-welcome-desc"))
                .Attribute("Text")?.Value));
        // 行间隔用发丝线，首行之前不画线：五行之间恰好四条。
        Assert.Equal(
            4,
            panel.Descendants().Count(element =>
                element.Name.LocalName == "Border" && HasClass(element, "wizard-divider")));

        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/SetupWizard.axaml"));
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Welcome.Row.MinHeight}",
            GetStyleSetters(styles, "Grid.wizard-welcome-row")["MinHeight"]);
        var iconChip = GetStyleSetters(styles, "Border.wizard-welcome-icon");
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Welcome.Icon.Size}",
            iconChip["Width"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Welcome.Icon.Size}",
            iconChip["Height"]);
        Assert.Equal("{StaticResource Launcher.Radius.Sm}", iconChip["CornerRadius"]);
        Assert.Equal("{DynamicResource Launcher.Color.Content.Row}", iconChip["Background"]);
        Assert.Equal(
            "{StaticResource Launcher.Typography.FontSize.Body.Md}",
            GetStyleSetters(styles, "TextBlock.wizard-welcome-name")["FontSize"]);
        Assert.Equal(
            "{StaticResource Launcher.Typography.FontSize.Body.Sm}",
            GetStyleSetters(styles, "TextBlock.wizard-welcome-desc")["FontSize"]);
    }

    [Fact]
    public void SetupWizard_UsesUnifiedTwoColumnStage()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SetupWizardOverlay.axaml"));
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
        // 无头带折叠：向导自绘头部，跳过钮保留文字语义。
        Assert.Null(dialog.Attribute("Title"));

        // 统一舞台：居中双栏内容，不再有侧栏导航列表。
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
            .Where(element => HasClass(element, "wizard-step"))
            .ToList();
        Assert.Equal(7, steps.Count);
        Assert.Equal(
            new[] { "WizardStep0", "WizardStep1", "WizardStep2", "WizardStep3", "WizardStep4", "WizardStep5", "WizardStep6" },
            steps.Select(step => step.Attribute(xNamespace + "Name")?.Value));
        // 左引导徽章一步一义：挥手/翻译/文档/路径检索/云端下载/路由切换/清单勾选。
        // 右面板内的状态图标不计入左引导的单一徽章结构。
        Assert.Equal(
            new[] { "HandWave", "Translate", "ScriptText", "FolderSearch", "CloudDownload", "SwapHorizontal", "ClipboardCheck" },
            steps.Select(step => Assert.Single(
                    step.Descendants(),
                    element => element.Name.LocalName == "MaterialIcon"
                        && element.Ancestors().Any(ancestor => HasClass(ancestor, "wizard-context-icon")))
                .Attribute("Kind")?.Value));
        Assert.All(steps, step =>
        {
            // 七步同构：固定 860 舞台 = 300 引导 + 32 间距 + 528 右侧面板。
            Assert.Equal("Grid", step.Name.LocalName);
            Assert.Equal("Auto,*", step.Attribute("ColumnDefinitions")?.Value);
            Assert.Equal("{StaticResource Launcher.Spacing.Xxxl}", step.Attribute("ColumnSpacing")?.Value);
            Assert.Equal(
                "{StaticResource Launcher.Component.Wizard.Stage.Width}",
                step.Attribute("Width")?.Value);
            Assert.Equal("Center", step.Attribute("HorizontalAlignment")?.Value);
            Assert.Equal("Stretch", step.Attribute("VerticalAlignment")?.Value);

            // 左引导只承载图标、标题与说明；差异全部落在右侧面板。
            var context = Assert.Single(step.Elements(), element =>
                element.Name.LocalName == "StackPanel"
                && HasClass(element, "wizard-context"));
            Assert.Single(context.Elements(), element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "wizard-context-icon"));
            Assert.Single(context.Elements(), element =>
                element.Name.LocalName == "TextBlock"
                && HasClass(element, "wizard-step-title"));
            Assert.Single(context.Elements(), element =>
                element.Name.LocalName == "TextBlock"
                && HasClass(element, "wizard-step-hint"));
            var panel = Assert.Single(step.Elements(), element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "wizard-panel")
                && element.Attribute("Grid.Column")?.Value == "1");
            // 正文留白落在面板上：契约步骤可绑定视口高度而不被外边距撑出外层滚动条。
            Assert.Equal(
                "{StaticResource Launcher.Component.Wizard.Body.Margin}",
                panel.Attribute("Margin")?.Value);
            Assert.NotEmpty(panel.Elements());
        });

        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/SetupWizard.axaml"));
        var contextStyle = GetStyleSetters(styles, "StackPanel.wizard-context");
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Context.Width}",
            contextStyle["Width"]);
        Assert.Equal("Center", contextStyle["VerticalAlignment"]);
        var iconStyle = GetStyleSetters(styles, "Border.wizard-context-icon");
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Context.Icon.Size}",
            iconStyle["Width"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Context.Icon.Size}",
            iconStyle["Height"]);
        Assert.Equal("{StaticResource Launcher.Radius.Full}", iconStyle["CornerRadius"]);
        Assert.Equal("{DynamicResource Launcher.Color.Primary.Soft}", iconStyle["Background"]);
        Assert.Equal("Left", iconStyle["HorizontalAlignment"]);
        var panelStyle = GetStyleSetters(styles, "Border.wizard-panel");
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Thickness.Xl}",
            panelStyle["Padding"]);
        Assert.Equal("{DynamicResource Launcher.Color.Card.Background}", panelStyle["Background"]);
        Assert.Equal("{DynamicResource Launcher.Color.Card.Border}", panelStyle["BorderBrush"]);
        Assert.Equal("True", panelStyle["ClipToBounds"]);
    }

    [Fact]
    public void SetupWizardAgreement_UsesDocumentPanelWithConsentFooter()
    {
        // 统一舞台（ADR-059）：与其他步骤同构——左引导承载标题、版次与语言声明，
        // 右面板为文档阅读器（铺满剩余高度）加固定底栏同意项。
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SetupWizardOverlay.axaml"));
        var xNamespace = document.Root?.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNamespace);
        var agreementStep = document
            .Descendants()
            .Single(element =>
                element.Attribute(xNamespace + "Name")?.Value == "WizardStep2");
        Assert.Null(agreementStep.Attribute("RowDefinitions"));
        // 面板绑定视口高度：文档阅读器获得有界高度，只在自己的滚动区里滚动。
        Assert.Equal(
            "{Binding $parent[ScrollViewer].Viewport.Height}",
            agreementStep.Attribute("Height")?.Value);

        var context = Assert.Single(agreementStep.Elements(), element =>
            element.Name.LocalName == "StackPanel"
            && HasClass(element, "wizard-context"));
        Assert.Single(context.Descendants(), element =>
            element.Name.LocalName == "Border"
            && HasClass(element, "wizard-agreement-version")
            && element.Descendants().Any(child =>
                child.Attribute("Text")?.Value == "{Binding Dialogs.SetupWizard.AgreementVersionText}"));
        Assert.Contains(context.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "{Binding Shell.I18n[agreementLanguageNote]}");
        // 语言声明图标顶对齐说明首行：说明换行时不得整块居中；额外上边距会把
        // 图标墨迹推离 13px 行的视觉中心。
        var languageNoteIcon = Assert.Single(context.Descendants(), element =>
            element.Name.LocalName == "MaterialIcon"
            && element.Attribute("Kind")?.Value == "InformationOutline");
        Assert.Equal("Top", languageNoteIcon.Attribute("VerticalAlignment")?.Value);
        Assert.Null(languageNoteIcon.Attribute("Margin"));

        var documentPanel = Assert.Single(agreementStep.Elements(), element =>
            element.Name.LocalName == "Border"
            && HasClass(element, "wizard-panel")
            && HasClass(element, "wizard-agreement-panel"));
        var panelGrid = Assert.Single(documentPanel.Elements(), element => element.Name.LocalName == "Grid");
        Assert.Equal("*,Auto", panelGrid.Attribute("RowDefinitions")?.Value);

        var reader = Assert.Single(documentPanel.Descendants(), element =>
            element.Name.LocalName == "ScrollViewer"
            && element.Attribute(xNamespace + "Name")?.Value == "AgreementScrollViewer");
        Assert.Null(reader.Attribute("MaxHeight"));
        Assert.Single(reader.Descendants(), element =>
            element.Name.LocalName == "ReleaseNotesMarkdownViewer"
            && element.Attribute("Markdown")?.Value == "{Binding Dialogs.SetupWizard.AgreementMarkdown}"
            && element.Attribute("FontFamily")?.Value
                == "{StaticResource Launcher.Typography.FontFamily.Document}");

        var consentFooter = Assert.Single(documentPanel.Descendants(), element =>
            element.Name.LocalName == "Border"
            && HasClass(element, "wizard-agreement-footer"));
        Assert.Equal("1", consentFooter.Attribute("Grid.Row")?.Value);
        var checkbox = Assert.Single(consentFooter.Descendants(), element =>
            element.Name.LocalName == "CheckBox");
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Agreement.Consent.Padding}",
            checkbox.Attribute("Margin")?.Value);
        Assert.Equal(
            "{Binding Dialogs.SetupWizard.IsAgreementAccepted, Mode=TwoWay}",
            checkbox.Attribute("IsChecked")?.Value);
        Assert.Equal(
            "{Binding Dialogs.SetupWizard.HasReadAgreement}",
            checkbox.Attribute("IsEnabled")?.Value);
        Assert.Equal(
            "{Binding Shell.I18n[agreementCheckbox]}",
            checkbox.Attributes()
                .Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                .Value);
        Assert.Single(checkbox.Elements(), element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "{Binding Shell.I18n[agreementCheckbox]}"
            && element.Attribute("TextWrapping")?.Value == "Wrap");
        // 阅读提示不再占据复选框下方的版面，只作为自动化帮助文本保留。
        Assert.Equal(
            "{Binding Shell.I18n[agreementScrollHint]}",
            checkbox.Attributes()
                .Single(attribute => attribute.Name.LocalName == "AutomationProperties.HelpText")
                .Value);
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding Shell.I18n[agreementScrollHint]}");

        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/SetupWizard.axaml"));
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Thickness.None}",
            GetStyleSetters(styles, "Border.wizard-agreement-panel")["Padding"]);
        // 底栏必须保持透明：卡片圆角不裁剪子元素，不透明底会盖住卡片描边与圆角。
        Assert.Equal(
            "{StaticResource Launcher.Color.Transparent}",
            GetStyleSetters(styles, "Border.wizard-agreement-footer")["Background"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Agreement.Version.Padding}",
            GetStyleSetters(styles, "Border.wizard-agreement-version")["Padding"]);
        // 旧的双卡（文档卡 + 同意侧栏）解剖已随统一舞台退场，不得回归。
        Assert.DoesNotContain(
            styles.Descendants().Where(element => element.Name.LocalName == "Style"),
            style => style.Attribute("Selector")?.Value is
                "Border.wizard-agreement-document" or "Border.wizard-agreement-consent");
    }

    [Fact]
    public void SetupWizard_StepsSwitchThroughSequentialFadeSwap()
    {
        // ADR-017：步骤切换 = 旧内容先淡出、新内容按方向滑入的顺序换页，由后置代码编排；
        // 步骤面板不走 MotionVisibility 与声明式动画类，可见性完全由后置代码接管。
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SetupWizardOverlay.axaml"));
        var xNamespace = document.Root?.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNamespace);
        var controls = document.Root?.GetNamespaceOfPrefix("controls");
        Assert.NotNull(controls);
        var steps = document
            .Descendants()
            .Where(element => HasClass(element, "wizard-step"))
            .ToList();
        Assert.Equal(7, steps.Count);

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
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SetupWizardOverlay.axaml"));
        var skipButton = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding Dialogs.ShowSetupWizardExitConfirmCommand}");
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
    public void SetupWizardHeader_SkipButtonRendersAsQuietTextButton()
    {
        // 跳过引导是头带的次要逃逸口：不再使用共享次级按钮的填充底，
        // 改为与复核页编辑钮同构的安静文本钮，避免与标题和主/次动作争层级。
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SetupWizardOverlay.axaml"));
        var skipButton = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding Dialogs.ShowSetupWizardExitConfirmCommand}");
        Assert.True(HasClass(skipButton, "wizard-skip"));
        Assert.DoesNotContain(
            "flat-action",
            skipButton.Attribute("Classes")?.Value ?? string.Empty,
            StringComparison.Ordinal);
        var label = Assert.Single(skipButton.Descendants(), element => element.Name.LocalName == "TextBlock");
        Assert.True(HasClass(label, "wizard-skip-label"));

        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/SetupWizard.axaml"));
        var skipStyle = GetStyleSetters(styles, "Button.wizard-skip");
        Assert.Equal("{StaticResource Launcher.Color.Transparent}", skipStyle["Background"]);
        Assert.Equal("{DynamicResource Launcher.Text.Secondary}", skipStyle["Foreground"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Content.Row.Hover}",
            GetStyleSetters(styles, "Button.wizard-skip:pointerover")["Background"]);
        Assert.Equal(
            "{StaticResource Launcher.Typography.FontSize.Body.Sm}",
            GetStyleSetters(styles, "TextBlock.wizard-skip-label")["FontSize"]);
    }

    [Fact]
    public void SetupWizardCompletion_ContextIconCarriesSuccessSemantics()
    {
        // 统一舞台（ADR-059）：完成语义由复核页左引导的上下文徽章（Success.Soft 底 +
        // Success 勾）承载，步骤标题回归中性——原 wizard-complete 标题色已退场，不得回归。
        // 各面板标题仍静态绑定自身资源键——共享 StepTitle 绑定会在 Step 变化的 t=0 让全部
        // 标题（含淡出中的旧面板）同帧跳到新标题，破坏中点换面的次序语义。
        var overlay = XDocument.Load(TestRepository.FromPresentationRoot("Views/SetupWizardOverlay.axaml"));
        var expectedTitleKeys = new[]
        {
            "setupWizardWelcomeTitle",
            "setupWizardLanguage",
            "agreementTitle",
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
            Assert.False(HasClass(headline, "wizard-complete"));
        }

        Assert.DoesNotContain(
            overlay.Descendants(),
            element => element.Name.LocalName == "TextBlock"
                && (element.Attribute("Text")?.Value.Contains("SetupWizard.StepTitle") ?? false));

        // 徽章结构：WizardStep6 左引导内单个 Success 上下文徽章，内含 Success 清单勾选。
        var xNamespace = overlay.Root?.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNamespace);
        var reviewStep = overlay
            .Descendants()
            .Single(element =>
                element.Attribute(xNamespace + "Name")?.Value == "WizardStep6");
        var successBadge = Assert.Single(reviewStep.Descendants(), element =>
            element.Name.LocalName == "Border"
            && HasClass(element, "wizard-context-icon")
            && HasClass(element, "success"));
        var badgeIcon = Assert.Single(successBadge.Descendants(), element =>
            element.Name.LocalName == "MaterialIcon");
        Assert.Equal("ClipboardCheck", badgeIcon.Attribute("Kind")?.Value);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Success}",
            badgeIcon.Attribute("Foreground")?.Value);

        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/SetupWizard.axaml"));
        var badgeStyle = GetStyleSetters(styles, "Border.wizard-context-icon");
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Context.Icon.Size}",
            badgeStyle["Width"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Context.Icon.Size}",
            badgeStyle["Height"]);
        Assert.Equal("{StaticResource Launcher.Radius.Full}", badgeStyle["CornerRadius"]);
        Assert.Equal("{DynamicResource Launcher.Color.Primary.Soft}", badgeStyle["Background"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Success.Soft}",
            GetStyleSetters(styles, "Border.wizard-context-icon.success")["Background"]);
        Assert.DoesNotContain(
            styles.Descendants().Where(element => element.Name.LocalName == "Style"),
            style => style.Attribute("Selector")?.Value?.Contains("wizard-complete") == true
                || style.Attribute("Selector")?.Value?.Contains("wizard-hero-badge") == true);
    }

    [Fact]
    public void SetupWizard_Review_UsesIconSummaryRows()
    {
        // 复核页：摘要行统一由 ItemsControl 按 SummaryItems 展开（图标 chip + 标签/值两行 +
        // 尾部「修改」文本钮），行间 Dialog.Section.Divider 发丝线；info-strip 着色卡不再承载摘要。
        var overlay = XDocument.Load(TestRepository.FromPresentationRoot("Views/SetupWizardOverlay.axaml"));
        var xNamespace = overlay.Root?.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNamespace);
        var reviewStep = overlay
            .Descendants()
            .Single(element =>
                element.Attribute(xNamespace + "Name")?.Value == "WizardStep6");
        var itemsControl = Assert.Single(reviewStep.Descendants(), element =>
            element.Name.LocalName == "ItemsControl"
            && element.Attribute("ItemsSource")?.Value == "{Binding Dialogs.SetupWizard.SummaryItems}");
        var template = Assert.Single(itemsControl.Descendants(), element =>
            element.Name.LocalName == "DataTemplate");
        var rowHost = Assert.Single(template.Elements(), element => element.Name.LocalName == "StackPanel");
        var divider = Assert.Single(rowHost.Elements(), element =>
            element.Name.LocalName == "Border"
            && HasClass(element, "wizard-divider"));
        Assert.Equal("{Binding HasDivider}", divider.Attribute("IsVisible")?.Value);

        var row = Assert.Single(rowHost.Elements(), element =>
            element.Name.LocalName == "Grid"
            && HasClass(element, "wizard-summary-row"));
        var chip = Assert.Single(row.Elements(), element =>
            element.Name.LocalName == "Border"
            && HasClass(element, "wizard-summary-icon"));
        var chipIcon = Assert.Single(chip.Descendants(), element =>
            element.Name.LocalName == "MaterialIcon");
        Assert.Equal("{Binding Icon}", chipIcon.Attribute("Kind")?.Value);

        var text = Assert.Single(row.Elements(), element => element.Name.LocalName == "StackPanel");
        var label = Assert.Single(text.Elements(), element =>
            element.Name.LocalName == "TextBlock"
            && HasClass(element, "caption"));
        Assert.Equal("{Binding Label}", label.Attribute("Text")?.Value);
        var value = Assert.Single(text.Elements(), element =>
            element.Name.LocalName == "TextBlock"
            && HasClass(element, "value"));
        Assert.Equal("{Binding Value}", value.Attribute("Text")?.Value);
        Assert.Equal("CharacterEllipsis", value.Attribute("TextTrimming")?.Value);

        var edit = Assert.Single(row.Elements(), element =>
            element.Name.LocalName == "Button"
            && HasClass(element, "wizard-summary-edit"));
        Assert.Equal("{Binding EditCommand}", edit.Attribute("Command")?.Value);
        var editContent = Assert.Single(edit.Elements(), element =>
            element.Name.LocalName == "StackPanel"
            && HasClass(element, "button-content"));
        Assert.Single(editContent.Elements(), element =>
            element.Name.LocalName == "MaterialIcon"
            && element.Attribute("Kind")?.Value == "Pencil");
        Assert.Single(editContent.Elements(), element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "{Binding EditLabel}");
        Assert.Equal("{Binding EditStep}", edit.Attribute("CommandParameter")?.Value);
        Assert.Equal(
            "{Binding EditLabel}",
            edit.Attributes()
                .Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                .Value);

        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/SetupWizard.axaml"));
        var rowStyle = GetStyleSetters(styles, "Grid.wizard-summary-row");
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Summary.Row.MinHeight}",
            rowStyle["MinHeight"]);
        var chipStyle = GetStyleSetters(styles, "Border.wizard-summary-icon");
        Assert.Equal("{StaticResource Launcher.Component.Wizard.Summary.IconChip.Size}", chipStyle["Width"]);
        Assert.Equal("{StaticResource Launcher.Component.Wizard.Summary.IconChip.Size}", chipStyle["Height"]);
        Assert.Equal("{StaticResource Launcher.Radius.Sm}", chipStyle["CornerRadius"]);
        Assert.Equal("{DynamicResource Launcher.Color.Field.Background}", chipStyle["Background"]);
        var dividerStyle = GetStyleSetters(styles, "Border.wizard-divider");
        Assert.Equal("{StaticResource Launcher.Component.Wizard.Divider.Height}", dividerStyle["Height"]);
        Assert.Equal("{DynamicResource Launcher.Color.Dialog.Section.Divider}", dividerStyle["Background"]);
        var editStyle = GetStyleSetters(styles, "Button.wizard-summary-edit");
        Assert.Equal("{StaticResource Launcher.Control.Height.Setting}", editStyle["Height"]);
        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.Sm}", editStyle["Padding"]);
        Assert.DoesNotContain("Width", editStyle.Keys);
        Assert.Equal("{StaticResource Launcher.Color.Transparent}", editStyle["Background"]);
        Assert.Equal(
            "{StaticResource Launcher.Typography.FontSize.Body.Sm}",
            GetStyleSetters(styles, "TextBlock.wizard-summary-edit-label")["FontSize"]);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Content.Row.Hover}",
            GetStyleSetters(styles, "Button.wizard-summary-edit:pointerover")["Background"]);
    }

    [Fact]
    public void SetupWizard_FieldPanels_LabelInputsAndExplainRecheck()
    {
        // 样张字段解剖：控件上方标注本地化字段名，语言选择拉伸铺满面板，路径字段补重新检查提示。
        var overlay = XDocument.Load(TestRepository.FromPresentationRoot("Views/SetupWizardOverlay.axaml"));
        var xNamespace = overlay.Root?.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNamespace);

        var languageStep = overlay
            .Descendants()
            .Single(element => element.Attribute(xNamespace + "Name")?.Value == "WizardStep1");
        var languageSelect = Assert.Single(languageStep.Descendants(), element =>
            element.Name.LocalName == "ComboBox");
        Assert.Equal("Stretch", languageSelect.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal(
            "{Binding Shell.I18n[setupWizardLanguageField]}",
            languageSelect.Attributes()
                .Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                .Value);
        Assert.Contains(languageStep.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && HasClass(element, "caption")
            && element.Attribute("Text")?.Value == "{Binding Shell.I18n[setupWizardLanguageField]}");
        Assert.Contains(languageStep.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && HasClass(element, "wizard-step-hint")
            && element.Attribute("Text")?.Value == "{Binding Shell.I18n[setupWizardLanguageHint]}");

        var pathStep = overlay
            .Descendants()
            .Single(element => element.Attribute(xNamespace + "Name")?.Value == "WizardStep3");
        Assert.Contains(pathStep.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && HasClass(element, "caption")
            && element.Attribute("Text")?.Value == "{Binding Shell.I18n[setupWizardGamePathLocation]}");
        Assert.Contains(pathStep.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && HasClass(element, "wizard-step-hint")
            && element.Attribute("Text")?.Value == "{Binding Shell.I18n[setupWizardGamePathRecheckHint]}");
    }

    [Fact]
    public void SetupWizardGamePath_ShowsStatusWithSemanticStateStyles()
    {
        var overlay = XDocument.Load(TestRepository.FromPresentationRoot("Views/SetupWizardOverlay.axaml"));
        var gamePathPanel = overlay
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBox"
                && element.Attribute("Text")?.Value
                    == "{Binding Dialogs.SetupWizard.GamePath, Mode=TwoWay}")
            .Ancestors()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "wizard-panel"));
        // M3 状态行：图标 + 文本承载同一状态，整行随路径为空一起隐藏。
        var statusRow = Assert.Single(gamePathPanel.Descendants(), element =>
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
        Assert.Equal(
            "{Binding Dialogs.SetupWizard.IsGamePathNotWritable}",
            status.Attribute("Classes.notwritable")?.Value);
        Assert.Equal("CharacterEllipsis", status.Attribute("TextTrimming")?.Value);
        Assert.Equal("1", status.Attribute("MaxLines")?.Value);

        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/SetupWizard.axaml"));
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
        Assert.Equal(
            "{StaticResource Launcher.Color.Danger}",
            GetStyleSetters(styles, "TextBlock.wizard-game-path-status.notwritable")["Foreground"]);

        // 状态图标与文本共用语义色：检测中 Sync、就绪 CheckCircle、损坏/不可访问
        // AlertCircle（与其他错误表面同族）、不可写入 Lock。
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
                Assert.Equal("AlertCircle", icon.Attribute("Kind")?.Value);
                Assert.Equal(
                    "{Binding Dialogs.SetupWizard.IsGamePathCorruptedInstallation}",
                    icon.Attribute("IsVisible")?.Value);
                Assert.Equal(
                    "{StaticResource Launcher.Color.Danger}",
                    icon.Attribute("Foreground")?.Value);
            },
            icon =>
            {
                Assert.Equal("AlertCircle", icon.Attribute("Kind")?.Value);
                Assert.Equal(
                    "{Binding Dialogs.SetupWizard.IsGamePathInaccessible}",
                    icon.Attribute("IsVisible")?.Value);
                Assert.Equal(
                    "{StaticResource Launcher.Color.Danger}",
                    icon.Attribute("Foreground")?.Value);
            },
            icon =>
            {
                Assert.Equal("Lock", icon.Attribute("Kind")?.Value);
                Assert.Equal(
                    "{Binding Dialogs.SetupWizard.IsGamePathNotWritable}",
                    icon.Attribute("IsVisible")?.Value);
                Assert.Equal(
                    "{StaticResource Launcher.Color.Danger}",
                    icon.Attribute("Foreground")?.Value);
            });
    }

    [Fact]
    public void SetupWizard_ChoiceSteps_UseGroupedRadioButtons()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SetupWizardOverlay.axaml"));
        var xNamespace = document.Root?.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNamespace);
        var downloadSourceStep = document
            .Descendants()
            .Single(element =>
                element.Attribute(xNamespace + "Name")?.Value == "WizardStep4");
        var proxyStep = document
            .Descendants()
            .Single(element =>
                element.Attribute(xNamespace + "Name")?.Value == "WizardStep5");
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
        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/SetupWizard.axaml"));
        var action = GetStyleSetters(styles, "Button.wizard-action");
        Assert.Equal("{StaticResource Launcher.Component.Button.Theme}", action["Theme"]);
        // 上一步与下一步同宽：动作带两枚动作共用 100 DIP 最小宽度，任意文案下等高宽。
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Action.MinWidth}",
            action["MinWidth"]);
        Assert.DoesNotContain("Background", action.Keys);
        Assert.DoesNotContain("CornerRadius", action.Keys);
        Assert.Equal("{DynamicResource Launcher.Color.Button.Secondary.Hover}",
            GetStyleSetters(styles, "Button.wizard-action:not(.primary-action):pointerover")["Background"]);
        Assert.Equal("{DynamicResource Launcher.Color.Button.Secondary.Pressed}",
            GetStyleSetters(styles, "Button.wizard-action:not(.primary-action):pressed")["Background"]);
    }

    [Fact]
    public void SetupWizard_OptionRowsUseTokenizedMinimumTargets()
    {
        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/SetupWizard.axaml"));

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
        // 选中行取 Primary.Soft 底 + 主色描边，作为单一强调（M3 选中态）。
        var selected = GetStyleSetters(styles, "RadioButton.wizard-option:checked");
        Assert.Equal("{DynamicResource Launcher.Color.Primary.Soft}", selected["Background"]);
        Assert.Equal("{DynamicResource Launcher.Color.Primary.Border}", selected["BorderBrush"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Default}", selected["BorderThickness"]);

        // 单选圈同步样张：20 圆 + 2 描边，选中换主色并显示 10 圆点；
        // 自绘模板只服务向导选项行，不继承共享 RadioButton 主题。
        Assert.Equal("{StaticResource WizardOptionTemplate}", option["Template"]);
        var xNamespace = styles.Root?.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNamespace);
        var optionTemplate = styles
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ControlTemplate"
                && element.Attribute(xNamespace + "Key")?.Value == "WizardOptionTemplate");
        Assert.Equal("RadioButton", optionTemplate.Attribute("TargetType")?.Value);
        var ring = Assert.Single(optionTemplate.Descendants(), element =>
            element.Name.LocalName == "Border"
            && element.Attribute(xNamespace + "Name")?.Value == "WizardRadioRing");
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Option.Radio.BorderThickness}",
            ring.Attribute("BorderThickness")?.Value);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Field.Border}",
            ring.Attribute("BorderBrush")?.Value);
        var ringSlot = ring.Parent!;
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Option.Radio.Size}",
            ringSlot.Attribute("Width")?.Value);
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Option.Radio.Size}",
            ringSlot.Attribute("Height")?.Value);
        var dot = Assert.Single(optionTemplate.Descendants(), element =>
            element.Name.LocalName == "Border"
            && element.Attribute(xNamespace + "Name")?.Value == "WizardRadioDot");
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Option.Radio.Dot.Size}",
            dot.Attribute("Width")?.Value);
        Assert.Equal(
            "{StaticResource Launcher.Component.Wizard.Option.Radio.Dot.Size}",
            dot.Attribute("Height")?.Value);
        Assert.Equal("False", dot.Attribute("IsVisible")?.Value);
        Assert.Equal(
            "{DynamicResource Launcher.Color.Primary}",
            GetStyleSetters(styles, "RadioButton.wizard-option:checked /template/ Border#WizardRadioRing")["BorderBrush"]);
        Assert.Equal(
            "True",
            GetStyleSetters(styles, "RadioButton.wizard-option:checked /template/ Border#WizardRadioDot")["IsVisible"]);
    }
}
