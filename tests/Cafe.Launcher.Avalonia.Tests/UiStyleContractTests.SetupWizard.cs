using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

// Setup wizard contracts: overlay inclusion, step swap choreography, review
// rows, game-path status styling, and wizard action/option row tokens.
public sealed partial class UiStyleContractTests
{
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
        Assert.Equal(
            "{Binding Dialogs.SetupWizard.IsGamePathNotWritable}",
            status.Attribute("Classes.notwritable")?.Value);
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
        Assert.Equal(
            "{StaticResource Launcher.Color.Danger}",
            GetStyleSetters(styles, "TextBlock.wizard-game-path-status.notwritable")["Foreground"]);

        // 状态图标与文本共用语义色：检测中 Sync、就绪 CheckCircle、损坏/不可访问 Alert、
        // 不可写入 Lock。
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
}
