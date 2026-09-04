using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

// Motion contracts: conditional enter/exit animations, MotionVisibility-driven
// overlays, and transform placement inside the single operation task container.
public sealed partial class UiStyleContractTests
{
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
}
