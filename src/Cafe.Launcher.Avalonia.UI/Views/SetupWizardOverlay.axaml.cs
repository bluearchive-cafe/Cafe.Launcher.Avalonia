using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Views;

/// <summary>
/// 设置向导覆盖层：步骤切换按实验台 <c>ChangeWizardAsync</c>/<c>FadeSwapAsync</c> 的顺序换页实现——
/// 空间档 333ms 对半分，旧内容先以退出加速曲线淡出（无位移），中点瞬时换内容并把滚动复位到
/// 顶部，起势帧消化目标面板的首次布局后，新内容再以进入减速曲线同步淡入并按方向滑入 ±14px，
/// 收尾多留一拍缓冲再结算；壳层（步骤进度与动作钮）在换面中点随显示步翻转，不先于内容跳变。
/// 最新状态优先、可中断、不排队；降动效、未附着或无可见面板时直接换内容定格。
/// </summary>
public partial class SetupWizardOverlay : UserControl
{
    private const string StepForwardOffsetKey = "Launcher.Motion.Offset.StepForward";
    private const string StepBackwardOffsetKey = "Launcher.Motion.Offset.StepBackward";
    private const string EnterEasingKey = "Launcher.Motion.Easing.Enter";
    private const string ExitEasingKey = "Launcher.Motion.Easing.Exit";

    /// <summary>起势帧额外让出的时间：Render 优先级排空后再等约一帧，保证 pose 已呈现（实验台 NextFrameAsync）。</summary>
    private static readonly TimeSpan PoseFrameDelay = TimeSpan.FromMilliseconds(16);

    /// <summary>收尾缓冲：超出名义时长的额外等待，保证动画最后一拍落完再结算（实验台 WaitAsync）。</summary>
    private static readonly TimeSpan SettleGrace = TimeSpan.FromMilliseconds(20);

    private readonly StackPanel[] stepPanels;
    private CancellationTokenSource? stepMotionCts;
    private SetupWizardViewModel? wizard;
    private MainWindowViewModel? mainWindow;
    private bool attachedToVisualTree;

    public SetupWizardOverlay()
    {
        InitializeComponent();
        stepPanels = [WizardStep0, WizardStep1, WizardStep2, WizardStep3, WizardStep4];
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        attachedToVisualTree = true;
        if (wizard is not null)
        {
            ShowStepInstantly(wizard.Step);
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        attachedToVisualTree = false;
        CancelStepMotion();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (wizard is not null)
        {
            wizard.PropertyChanged -= OnWizardPropertyChanged;
            wizard = null;
        }

        if (mainWindow is not null)
        {
            mainWindow.PropertyChanged -= OnRootPropertyChanged;
            mainWindow = null;
        }

        mainWindow = DataContext as MainWindowViewModel;
        wizard = mainWindow?.Dialogs.SetupWizard;

        if (wizard is not null)
        {
            wizard.PropertyChanged += OnWizardPropertyChanged;
        }

        if (mainWindow is not null)
        {
            mainWindow.PropertyChanged += OnRootPropertyChanged;
        }

        ShowStepInstantly(wizard?.Step ?? 0);
    }

    private void OnRootPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsMotionEnabled))
        {
            return;
        }

        // 动效偏好切换时立即落定，避免半程动画停留在中间视觉。
        CancelStepMotion();
        if (wizard is not null)
        {
            ShowStepInstantly(wizard.Step);
        }
    }

    private void OnWizardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SetupWizardViewModel.Step) || wizard is null)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            var target = wizard.Step;
            Dispatcher.UIThread.Post(() => RunStepTransition(target));
            return;
        }

        RunStepTransition(wizard.Step);
    }

    private void RunStepTransition(int targetStep)
    {
        if (targetStep < 0 || targetStep >= stepPanels.Length)
        {
            return;
        }

        var toIndex = targetStep;
        var fromIndex = Array.FindIndex(stepPanels, static panel => panel.IsVisible);
        if (fromIndex == toIndex)
        {
            SettleStep(stepPanels[toIndex]);
            return;
        }

        CancelStepMotion();
        var isMotionEnabled = mainWindow?.IsMotionEnabled ?? false;
        if (!isMotionEnabled || !attachedToVisualTree)
        {
            // 降动效/未附着：幂等换面（含滚动复位）后直接定格。
            ShowStepInstantly(toIndex);
            return;
        }

        var cancellation = new CancellationTokenSource();
        stepMotionCts = cancellation;
        _ = RunStepTransitionAsync(fromIndex, toIndex, cancellation);
    }

    /// <summary>等待一个渲染帧：Render 优先级任务排空后再让出约一帧时间（实验台 NextFrameAsync 同机制）。</summary>
    private static async Task WaitPoseFrameAsync(CancellationToken token)
    {
        // Render 排空是纯队列操作，无长时等待，不参与取消（None 为有意传递）。
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render, CancellationToken.None);
        await Task.Delay(PoseFrameDelay, token);
    }

    private async Task RunStepTransitionAsync(int fromIndex, int toIndex, CancellationTokenSource cancellation)
    {
        try
        {
            var token = cancellation.Token;
            // 空间档 333ms 对半分（实验台 Fluent Profile.Normal=333，向导属空间连续家族）；
            // 250ms 档的半程在前载 Enter 曲线下可见运动只摊约 3 帧，观感顿挫。
            var half = TimeSpan.FromTicks(MotionTokens.SpatialDuration.Ticks / 2);
            var forward = toIndex > fromIndex;
            var enterOffset = MotionResourceLookup.GetDouble(
                forward ? StepForwardOffsetKey : StepBackwardOffsetKey,
                forward ? 14d : -14d);
            var enterEasing = MotionResourceLookup.GetEasing(
                EnterEasingKey,
                static () => new SplineEasing { X1 = 0, Y1 = 0, X2 = 0, Y2 = 1 });
            var exitEasing = MotionResourceLookup.GetEasing(
                ExitEasingKey,
                static () => new SplineEasing { X1 = 1, Y1 = 0, X2 = 1, Y2 = 1 });

            // 阶段一：旧内容淡出（无位移），期间禁用命中测试。
            if (fromIndex >= 0)
            {
                var from = stepPanels[fromIndex];
                from.IsHitTestVisible = false;
                await CreateOpacityAnimation(from.Opacity, 0d, half, exitEasing).RunAsync(from, token);
            }

            // 中点瞬时换内容：不可见时翻转可见性，滚动复位到顶部；壳层（进度与动作钮）
            // 同帧随显示步翻转，保证文本与内容同步切换。
            SwapToPanel(toIndex);

            // 起势帧（实验台 NextFrameAsync 同机制）：先把入场 pose（透明 0 / 位移 ±14）
            // 渲染出来，令目标面板首次可见引发的全量布局与文本 shaping 消化在这一不可见帧
            // 内，动画再从安静的 UI 线程起播。Enter 曲线极端前载，缺这一帧时首拍时序误差
            // 会被放大成肉眼可见的跳变。
            var to = stepPanels[toIndex];
            var toTransform = (TranslateTransform)to.RenderTransform!;
            to.Opacity = 0d;
            toTransform.X = enterOffset;
            await WaitPoseFrameAsync(token);

            // 阶段二：透明度与位移都经 DoubleTransition 驱动（实验台 AnimateEntranceAsync
            // 同机制；Animation.RunAsync 在非 Visual 的 Transform 上不产生动画），同一批
            // 注册保证淡入与滑入逐帧同步。
            to.Transitions =
            [
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = half,
                    Easing = enterEasing,
                },
            ];
            toTransform.Transitions =
            [
                new DoubleTransition
                {
                    Property = TranslateTransform.XProperty,
                    Duration = half,
                    Easing = enterEasing,
                },
            ];
            to.Opacity = 1d;
            toTransform.X = 0d;

            // 收尾缓冲（实验台 WaitAsync 同机制）：多等一拍再交由所有权守卫结算，避免
            // 最后一拍未落完就被清 transitions 截断。
            await Task.Delay(half + SettleGrace, token);
        }
        catch (OperationCanceledException)
        {
            // 更新状态已接管或动效被关闭；视觉统一由所有权守卫结算。
        }
        finally
        {
            if (ReferenceEquals(stepMotionCts, cancellation))
            {
                stepMotionCts = null;
                cancellation.Dispose();
                SettleStep(stepPanels[toIndex]);
            }
        }
    }

    private void ShowStepInstantly(int stepIndex)
    {
        var target = Math.Clamp(stepIndex, 0, stepPanels.Length - 1);
        SwapToPanel(target);
        SettleStep(stepPanels[target]);
    }

    /// <summary>幂等换面：全部非目标面板隐藏并复位视觉，目标面板可见；滚动回到顶部；壳层随显示步翻转。</summary>
    private void SwapToPanel(int toIndex)
    {
        for (var i = 0; i < stepPanels.Length; i++)
        {
            var panel = stepPanels[i];
            if (i == toIndex)
            {
                panel.IsVisible = true;
            }
            else
            {
                panel.IsVisible = false;
                ResetStepVisual(panel);
            }
        }

        StepScroll.Offset = Vector.Zero;
        ApplyChromeState();
    }

    /// <summary>
    /// 壳层按显示步翻转：步骤进度文本与动作钮可见集（上一步出现、下一步↔完成切换）在换面
    /// 中点与内容面板同帧切换——实验台 <c>ChangeWizardAsync</c> 的 swap 回调同语义。若绑定
    /// 逻辑 <c>Step</c>，文本会在退场前就跳到新步骤，形成"先变标题、后过渡"的两段感。
    /// 下一步的启用态不在此列：<c>CanGoNext</c> 是当前步校验的实时反馈，保持绑定即时生效。
    /// </summary>
    private void ApplyChromeState()
    {
        if (wizard is null)
        {
            return;
        }

        StepProgressText.Text = wizard.StepProgress;
        PreviousButton.IsVisible = wizard.CanGoPrevious;
        NextButton.IsVisible = !wizard.IsLastStep;
        FinishButton.IsVisible = wizard.IsLastStep;
    }

    private void SettleStep(StackPanel panel)
    {
        panel.IsVisible = true;
        ResetStepVisual(panel);
    }

    private static void ResetStepVisual(StackPanel panel)
    {
        // 先清过渡再写终值（实验台 SetFinalVisual 同序），避免残留的插值拍覆盖终值。
        panel.Transitions = null;
        panel.Opacity = 1d;
        panel.IsHitTestVisible = true;
        if (panel.RenderTransform is TranslateTransform transform)
        {
            transform.Transitions = null;
            transform.X = 0d;
        }
    }

    private void CancelStepMotion()
    {
        stepMotionCts?.Cancel();
        stepMotionCts?.Dispose();
        stepMotionCts = null;
    }

    private static Animation CreateOpacityAnimation(double from, double to, TimeSpan duration, Easing easing) => new()
    {
        Duration = duration,
        Easing = easing,
        FillMode = FillMode.Forward,
        Children =
        {
            new KeyFrame
            {
                Cue = new Cue(0),
                Setters = { new Setter { Property = Visual.OpacityProperty, Value = from } },
            },
            new KeyFrame
            {
                Cue = new Cue(1),
                Setters = { new Setter { Property = Visual.OpacityProperty, Value = to } },
            },
        },
    };
}
