using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.ViewModels;
using Cafe.Launcher.Avalonia.Views;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void SetupWizard_InJapaneseAtMinimumWindowSize_KeepsScrollableContentAndNavigationReachable()
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Shell.ApplyLanguage(
            LauncherLanguages.Japanese,
            context.ViewModel.Settings,
            context.ViewModel.ResourcePanel,
            hasSnapshot: false);
        Dispatcher.UIThread.RunJobs();

        var wizard = context.Window.GetVisualDescendants().OfType<SetupWizardOverlay>().Single();
        var content = wizard.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("scroll-pad"));
        var next = GetWizardNextButton(context.Window, context.ViewModel);

        Assert.True(content.IsEffectivelyVisible);
        Assert.True(content.Viewport.Height > 0);
        Assert.True(next.IsEffectivelyVisible);
        AssertControlInsideWindow(next, context.Window);
    }

    [AvaloniaFact]
    public async Task SetupWizard_WhenGamePathStatusChanges_UpdatesStatusLineAndNextAvailability()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        // Simulate first-launch trigger (settings.json missing)
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.IsSetupWizardVisible);
        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsFirstStep);

        var installationBasePath = Path.Combine(context.TempDir, "available-installation");
        context.ViewModel.Dialogs.SetupWizard.GamePath = installationBasePath;

        // Step 0 → 1 detects only the preconfigured test path.
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, context.ViewModel.Dialogs.SetupWizard.Step);
        Assert.Equal(installationBasePath, context.ViewModel.Dialogs.SetupWizard.GamePath);
        Assert.True(GetWizardGamePathStatus(context.Window).IsEffectivelyVisible);

        await WaitForGamePathStatusAsync(
            context.ViewModel.Dialogs.SetupWizard,
            SetupWizardGamePathStatus.AvailableForInstallation);
        Dispatcher.UIThread.RunJobs();

        var statusLine = GetWizardGamePathStatus(context.Window);
        var nextButton = GetWizardNextButton(context.Window, context.ViewModel);
        Assert.Equal(
            context.ViewModel.Shell.I18n["setupWizardGamePathAvailable"],
            statusLine.Text);
        Assert.True(context.ViewModel.Dialogs.SetupWizard.CanGoNext);
        Assert.True(nextButton.IsEnabled);

        var corruptedInstallationPath = new GameInstallationPath().NormalizeGamePath(
            Path.Combine(context.TempDir, "corrupted-installation"));
        Directory.CreateDirectory(corruptedInstallationPath);
        await File.WriteAllTextAsync(
            Path.Combine(corruptedInstallationPath, GamePaths.ManifestFileName),
            "{}");
        context.ViewModel.Dialogs.SetupWizard.GamePath = corruptedInstallationPath;
        await WaitForGamePathStatusAsync(
            context.ViewModel.Dialogs.SetupWizard,
            SetupWizardGamePathStatus.CorruptedInstallation);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            context.ViewModel.Shell.I18n["setupWizardGamePathCorrupted"],
            statusLine.Text);
        Assert.False(context.ViewModel.Dialogs.SetupWizard.CanGoNext);
        Assert.False(nextButton.IsEnabled);

        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, context.ViewModel.Dialogs.SetupWizard.Step);
    }

    [AvaloniaFact]
    public async Task SetupWizard_ReviewList_EditButtonsNavigateToTheirSteps()
    {
        using var context = CreateContext();
        var wizard = context.ViewModel.Dialogs.SetupWizard;
        wizard.GamePath = Path.Combine(context.TempDir, "available-installation");

        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        wizard.NextCommand.Execute(null);
        await WaitForGamePathStatusAsync(wizard, SetupWizardGamePathStatus.AvailableForInstallation);
        // 循环体本身无 await：命令一旦被门控卡住即为死循环，预算兜底快速失败。
        var advanceDeadline = DateTime.UtcNow.AddSeconds(5);
        while (!wizard.IsLastStep)
        {
            if (DateTime.UtcNow >= advanceDeadline)
            {
                Assert.Fail("向导未在 5 秒预算内推进到最后一步。");
            }

            wizard.NextCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }
        Dispatcher.UIThread.RunJobs();

        var editButtons = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control => AutomationProperties.GetName(control)
                == context.ViewModel.Shell.I18n["setupWizardEditStep"])
            .ToArray();

        Assert.Equal(4, editButtons.Length);

        foreach (var (editButton, expectedStep) in editButtons.Zip([0, 2, 1, 3]))
        {
            new ButtonAutomationPeer(editButton).Invoke();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(expectedStep, wizard.Step);

            // 内层 WaitForGamePathStatusAsync 每次自带 2 秒预算，外层给足整体预算。
            var stepAdvanceDeadline = DateTime.UtcNow.AddSeconds(10);
            while (!wizard.IsLastStep)
            {
                if (DateTime.UtcNow >= stepAdvanceDeadline)
                {
                    Assert.Fail("向导未在 10 秒预算内推进到最后一步。");
                }

                if (wizard.IsStep1)
                {
                    await WaitForGamePathStatusAsync(
                        wizard,
                        SetupWizardGamePathStatus.AvailableForInstallation);
                }

                wizard.NextCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
            }
        }
    }

    [AvaloniaFact]
    public void SetupWizard_RadioChoices_KeepGroupsIndependent()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Dialogs.SetupWizard.Step = 2;
        Dispatcher.UIThread.RunJobs();

        var cafe = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n["downloadSourceCafe"]);
        var official = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n["downloadSourceOfficial"]);

        official.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsPatchUrlGroupOfficial);
        Assert.False(context.ViewModel.Dialogs.SetupWizard.IsPatchUrlGroupCafe);
        Assert.False(cafe.IsChecked);
        Assert.True(official.IsChecked);

        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var auto = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n["proxyAuto"]);
        var direct = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n["proxyDirect"]);
        var system = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n["proxySystem"]);

        direct.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsProxyDirect);
        Assert.False(context.ViewModel.Dialogs.SetupWizard.IsProxySystem);
        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsPatchUrlGroupOfficial);
        Assert.False(auto.IsChecked);
        Assert.True(direct.IsChecked);
        Assert.False(system.IsChecked);

        system.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsProxySystem);
        Assert.False(context.ViewModel.Dialogs.SetupWizard.IsProxyAuto);
        Assert.False(context.ViewModel.Dialogs.SetupWizard.IsProxyDirect);
        Assert.True(system.IsChecked);
    }

    [AvaloniaFact]
    public void SetupWizard_OptionRows_StretchToAlignRadioCircles()
    {
        // ADR-017：wizard-option 行等宽拉伸（HorizontalAlignment=Stretch）——
        // 列宽大于行宽时行若按内容自适应，非左对齐排布会使圆圈左缘错位。
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Dialogs.SetupWizard.Step = 2;
        Dispatcher.UIThread.RunJobs();

        var radios = context.Window.GetVisualDescendants().OfType<RadioButton>()
            .Where(control => control.Classes.Contains("wizard-option") && control.IsEffectivelyVisible)
            .ToList();
        Assert.Equal(2, radios.Count);
        Assert.All(
            radios,
            radio => Assert.Equal(radios[0].Bounds.Width, radio.Bounds.Width));
        Assert.All(
            radios,
            radio => Assert.Equal(radios[0].Bounds.X, radio.Bounds.X));

        var circleOffsets = radios.Select(radio =>
        {
            var circle = radio.GetVisualDescendants()
                .First(child => child.Name == "OuterEllipse");
            return radio.TranslatePoint(circle.Bounds.Position, radio)!.Value.X;
        }).ToList();
        Assert.Equal(circleOffsets[0], circleOffsets[1]);
    }

    [AvaloniaFact]
    public void SetupWizard_StepSwitch_LeavesOnlyFinalStepVisible()
    {
        // ADR-017：步骤切换 = 顺序换页（后置代码编排）；降动效下瞬切换面。
        // 快速连续切换后最新状态生效：任何时刻只有一个步骤面板可见且视觉已定格。
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = true;
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        var overlay = context.Window.GetVisualDescendants()
            .First(control => control.Classes.Contains("setup-wizard-overlay"));
        var steps = overlay.GetVisualDescendants()
            .OfType<StackPanel>()
            .Where(control => control.Classes.Contains("wizard-step"))
            .ToList();
        Assert.Equal(5, steps.Count);

        foreach (var stepIndex in new[] { 3, 1, 4 })
        {
            context.ViewModel.Dialogs.SetupWizard.Step = stepIndex;
            Dispatcher.UIThread.RunJobs();

            var visibleStep = Assert.Single(steps, control => control.IsVisible);
            Assert.Equal(stepIndex, steps.IndexOf(visibleStep));
            Assert.Equal(1d, visibleStep.Opacity);
            var transform = Assert.IsType<TranslateTransform>(visibleStep.RenderTransform);
            Assert.Equal(0d, transform.X);
        }
    }

    [AvaloniaFact]
    public async Task SetupWizard_StepSwitchWithMotion_SequentialSwapSettlesOnFinalStep()
    {
        // ADR-017 + FluentMotionLab ChangeWizardAsync：旧内容先淡出、新内容按方向滑入；
        // 快速连点只保留最新状态，最终目标面板必须定格在 Opacity=1、X=0。
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = false;
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        var overlay = context.Window.GetVisualDescendants()
            .First(control => control.Classes.Contains("setup-wizard-overlay"));
        var steps = overlay.GetVisualDescendants()
            .OfType<StackPanel>()
            .Where(control => control.Classes.Contains("wizard-step"))
            .ToList();
        Assert.Equal(5, steps.Count);

        foreach (var stepIndex in new[] { 2, 0, 4 })
        {
            await AssertEntranceIsAnimatedAsync(context.ViewModel.Dialogs.SetupWizard, steps, stepIndex);
            var visibleStep = Assert.Single(steps, control => control.IsVisible);
            Assert.Equal(stepIndex, steps.IndexOf(visibleStep));
            Assert.Equal(1d, visibleStep.Opacity);
        }
    }

    /// <summary>
    /// 断言目标面板的入场经历真实过渡而非瞬变。中间帧用「属性变更推送」观察：动画逐帧写入
    /// Opacity / X 时同步触发 PropertyChanged，不存在轮询采样窗口——慢机上轮询一旦被调度停顿
    /// 盖过入场窗口（前载曲线下约 100ms），会从「未入场」直接观察到「已落定」而漏采全部中间帧
    /// （CI 曾实际发生）。帧泵极端停顿导致一帧都未落入窗口时靠重试区分：偶发重试通过即环境
    /// 调度抖动，连续多次失败才判定为入场瞬变回归。
    /// </summary>
    private static async Task AssertEntranceIsAnimatedAsync(
        SetupWizardViewModel wizard,
        List<StackPanel> steps,
        int stepIndex)
    {
        const int MaxAttempts = 3;
        var attempts = new List<string>();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            // 重试时目标面板已落定可见，先换到锚点步令其退场，否则 from==to 走瞬时换面，
            // 观察不到入场；锚点间隔两步，重试覆盖的仍是一次带方向变化的完整入场。
            if (steps.FindIndex(panel => panel.IsVisible) == stepIndex)
            {
                var anchor = (stepIndex + 2) % steps.Count;
                wizard.Step = anchor;
                await WaitUntilStepSettledAsync(steps, anchor, $"步骤 {stepIndex} 重试前的锚点步 {anchor}");
            }

            var sawFade = false;
            var sawSlide = false;

            void ObserveAnimatedValue(object? sender, AvaloniaPropertyChangedEventArgs e)
            {
                // 精确判定：入场为前载曲线，起势帧与落定结算精确写 0 / ±14 / 1，
                // 开区间只计入真实插值中间帧，排除两端的确定态。
                if (e.Property == Visual.OpacityProperty
                    && e.NewValue is double opacity
                    && opacity is > 0.05 and < 0.95)
                {
                    sawFade = true;
                }

                if (e.Property == TranslateTransform.XProperty
                    && e.NewValue is double x
                    && Math.Abs(x) is > 0.5 and < 13.5)
                {
                    sawSlide = true;
                }
            }

            var target = steps[stepIndex];
            // X 的动画写入发生在 RenderTransform 实例上而非面板上，两个对象都要订阅。
            var targetTransform = Assert.IsType<TranslateTransform>(target.RenderTransform);
            target.PropertyChanged += ObserveAnimatedValue;
            targetTransform.PropertyChanged += ObserveAnimatedValue;
            try
            {
                wizard.Step = stepIndex;
                await WaitUntilStepSettledAsync(steps, stepIndex, $"步骤 {stepIndex}（第 {attempt} 次尝试）");
            }
            finally
            {
                target.PropertyChanged -= ObserveAnimatedValue;
                targetTransform.PropertyChanged -= ObserveAnimatedValue;
            }

            if (sawFade && sawSlide)
            {
                return;
            }

            attempts.Add($"第 {attempt} 次尝试 fade={sawFade} slide={sawSlide}");
        }

        Assert.Fail(
            $"步骤 {stepIndex} 连续 {MaxAttempts} 次未观察到入场中间帧（{string.Join("；", attempts)}）。" +
            "多次重试仍复现疑似入场瞬变回归，而非环境调度抖动。");
    }

    /// <summary>入场序列（退场对半 + 起势帧 + 入场对半 + 收尾缓冲）由真实帧时钟驱动，给硬性预算防挂死。</summary>
    private static async Task WaitUntilStepSettledAsync(List<StackPanel> steps, int stepIndex, string phase)
    {
        var settleDeadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            if (DateTime.UtcNow >= settleDeadline)
            {
                Assert.Fail($"{phase} 的入场动画未在 5 秒预算内落定。");
            }

            var settled = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var panel = steps[stepIndex];
                return panel.IsVisible
                    && panel.Opacity == 1d
                    && panel.RenderTransform is TranslateTransform settledTransform
                    && settledTransform.X == 0d;
            });
            if (settled)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => { });
            await Task.Delay(1);
        }
    }

    [AvaloniaFact]
    public void SetupWizard_StepSwitch_ResetsScrollToTop()
    {
        // 五步共用一个 ScrollViewer：换面时滚动必须复位到顶部，不得把旧偏移带入新步骤。
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        var overlay = context.Window.GetVisualDescendants()
            .First(control => control.Classes.Contains("setup-wizard-overlay"));
        var scroll = overlay.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("scroll-pad"));
        // 压缩视口强制内容溢出，使偏移可被置为非零。
        scroll.MaxHeight = 120;
        Dispatcher.UIThread.RunJobs();
        scroll.Offset = new Vector(0, 80);
        Dispatcher.UIThread.RunJobs();
        Assert.True(scroll.Offset.Y > 0, "测试前置：内容需在压缩视口内溢出以产生非零滚动偏移。");

        context.ViewModel.Dialogs.SetupWizard.Step = 4;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0d, scroll.Offset.Y);
    }

    [AvaloniaTheory]
    [InlineData(LauncherLanguages.English)]
    [InlineData(LauncherLanguages.SimplifiedChinese)]
    [InlineData(LauncherLanguages.TraditionalChinese)]
    [InlineData(LauncherLanguages.Japanese)]
    public async Task SetupWizard_WhenLanguageChanges_LocalizesStatusLineAndStepTitle(
        string language)
    {
        using var context = CreateContext();
        var installationBasePath = Path.Combine(context.TempDir, "available-installation");
        context.ViewModel.Dialogs.SetupWizard.GamePath = installationBasePath;
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Dialogs.SetupWizard.Language = language;
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        await WaitForGamePathStatusAsync(
            context.ViewModel.Dialogs.SetupWizard,
            SetupWizardGamePathStatus.AvailableForInstallation);
        Dispatcher.UIThread.RunJobs();

        var statusLine = GetWizardGamePathStatus(context.Window);
        var stepHeadline = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("wizard-step-title") && control.IsEffectivelyVisible);
        var progress = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Text == context.ViewModel.Dialogs.SetupWizard.StepProgress
                && control.IsEffectivelyVisible);

        Assert.Equal(
            context.ViewModel.Shell.I18n["setupWizardGamePathAvailable"],
            statusLine.Text);
        Assert.Equal(statusLine.Text, AutomationProperties.GetName(statusLine));
        // 居中单列解剖：步骤标题随语言本地化，进度行始终可见。
        Assert.Equal(
            context.ViewModel.Shell.I18n["setupWizardGamePath"],
            stepHeadline.Text);
        Assert.Equal("2 / 5", progress.Text);
    }

    [AvaloniaFact]
    public void SetupWizard_WhenEscapeIsPressed_RequiresExitConfirmation()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        var firstHandled = context.ViewModel.TryHandleEscape();
        Dispatcher.UIThread.RunJobs();

        Assert.True(firstHandled);
        Assert.True(context.ViewModel.Dialogs.IsSetupWizardVisible);
        Assert.True(context.ViewModel.Dialogs.IsSetupWizardExitConfirmVisible);

        var secondHandled = context.ViewModel.TryHandleEscape();
        Dispatcher.UIThread.RunJobs();

        Assert.True(secondHandled);
        Assert.True(context.ViewModel.Dialogs.IsSetupWizardVisible);
        Assert.False(context.ViewModel.Dialogs.IsSetupWizardExitConfirmVisible);
    }

    [AvaloniaFact]
    public async Task SetupWizard_WhenExitIsConfirmed_AppliesSkipAndClosesWizard()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.TryHandleEscape();
        Dispatcher.UIThread.RunJobs();

        await context.ViewModel.Dialogs.ConfirmSetupWizardExitCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(context.ViewModel.Dialogs.IsSetupWizardExitConfirmVisible);
        Assert.False(context.ViewModel.Dialogs.IsSetupWizardVisible);
    }

    [AvaloniaFact]
    public async Task SetupWizard_WhenSkipped_HidesOverlay()
    {
        using var context = CreateContext();
        LauncherSettings? applied = null;
        context.ViewModel.Dialogs.SetupWizard.SettingsApplied += settings =>
        {
            applied = settings;
            // Simulate the parent ViewModel's behavior: hide wizard on completion
            context.ViewModel.Dialogs.IsSetupWizardVisible = false;
            return Task.CompletedTask;
        };
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        await context.ViewModel.Dialogs.SetupWizard.SkipCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(context.ViewModel.Dialogs.IsSetupWizardVisible);
        Assert.NotNull(applied);
        Assert.Equal("auto", applied!.Language);
    }

    [AvaloniaFact]
    public async Task SetupWizard_WhenCompleted_BuildsSettingsAndHidesOverlay()
    {
        using var context = CreateContext();
        LauncherSettings? applied = null;
        context.ViewModel.Dialogs.SetupWizard.SettingsApplied += settings =>
        {
            applied = settings;
            // Simulate the parent ViewModel's behavior: hide wizard on completion
            context.ViewModel.Dialogs.IsSetupWizardVisible = false;
            return Task.CompletedTask;
        };
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        // Navigate to step 1 (GamePath) and set a path
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.Dialogs.SetupWizard.GamePath = @"C:\Games\YostarGames\BlueArchive_JP";
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsLastStep);

        await context.ViewModel.Dialogs.SetupWizard.CompleteCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(context.ViewModel.Dialogs.IsSetupWizardVisible);
        Assert.NotNull(applied);
        Assert.Contains(@"BlueArchive_JP", applied!.GamePath);
    }

    private static TextBlock GetWizardGamePathStatus(MainWindow window) =>
        window.GetVisualDescendants().OfType<TextBlock>().Single(control =>
            control.Classes.Contains("wizard-game-path-status"));

    private static Button GetWizardNextButton(MainWindow window, MainWindowViewModel viewModel) =>
        window.GetVisualDescendants().OfType<Button>().Single(control =>
            ReferenceEquals(control.Command, viewModel.Dialogs.SetupWizard.NextCommand));

    private static async Task WaitForGamePathStatusAsync(
        SetupWizardViewModel viewModel,
        SetupWizardGamePathStatus expectedStatus)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.PropertyName == nameof(SetupWizardViewModel.GamePathStatus)
                && viewModel.GamePathStatus == expectedStatus)
            {
                completion.TrySetResult();
            }
        };
        viewModel.PropertyChanged += handler;
        try
        {
            if (viewModel.GamePathStatus == expectedStatus)
            {
                return;
            }

            await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            viewModel.PropertyChanged -= handler;
        }
    }
}
