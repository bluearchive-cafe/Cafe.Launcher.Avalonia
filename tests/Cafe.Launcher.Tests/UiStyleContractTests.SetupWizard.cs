using System.Xml.Linq;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

// Setup wizard contracts: overlay inclusion, step swap choreography, review
// rows, game-path status styling, and wizard action/option row tokens.
public sealed partial class UiStyleContractTests
{
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

}
