using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Cafe.Launcher.UI.Helpers;

namespace Cafe.Launcher.UI.Controls;

/// <summary>
/// 一次性入场锚点：触发时挂上 <c>motion-enter</c>，快速档结束后摘除。
/// 与壳层、操作表面同型（ADR-016）——入场选择器不得常驻，否则运行期重新开启
/// 动效偏好时，已显示的内容会随着 motion-enabled 重新匹配而重放入场（§2.7）。
/// 与 <see cref="MotionVisibility"/> 的分工：后者管理叠层的开合与可见性，本类只负责
/// 常驻控件的单次显现触发（设置分类、Toast 卡片）。
/// </summary>
internal static class MotionEntrance
{
    /// <summary>当前触发面（设置分类、Toast）的入场动画均为快速档，锚点窗期与之对齐。</summary>
    private static readonly TimeSpan AnchorDuration = MotionTokens.FastDuration;

    public static readonly AttachedProperty<bool> IsTriggeredProperty =
        AvaloniaProperty.RegisterAttached<MotionEntranceOwner, Control, bool>("IsTriggered");

    private static readonly ConditionalWeakTable<Control, EntranceState> States = new();

    static MotionEntrance()
    {
        IsTriggeredProperty.Changed.AddClassHandler<Control>(OnIsTriggeredChanged);
    }

    public static bool GetIsTriggered(Control control) =>
        control.GetValue(IsTriggeredProperty);

    public static void SetIsTriggered(Control control, bool value) =>
        control.SetValue(IsTriggeredProperty, value);

    private static void OnIsTriggeredChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        var state = States.GetValue(control, static item => new EntranceState(item));
        state.SetTriggered(args.GetNewValue<bool>());
    }

    private sealed class EntranceState(Control control)
    {
        private CancellationTokenSource? retirement;
        private bool triggered;
        private bool waitingForAttach;

        public void SetTriggered(bool value)
        {
            triggered = value;
            CancelRetirement();
            control.Classes.Remove("motion-enter");
            if (!value)
            {
                ResetPresentation();
                return;
            }

            TryApplyAnchor();
        }

        /// <summary>
        /// 锚点必须在控件真正呈现后才挂上：隐藏中挂类，动画会随首次布局落到终态或停在
        /// 起势帧，两种情况都不会在真正显现时重放。尚未挂树时等一次挂树再判定；已挂树但
        /// 仍被隐藏（如启动时默认分类、叠层还没打开）则放弃本次入场——Avalonia 不为
        /// IsEffectivelyVisible 发属性通知，祖先转为可见时收不到回调，只能等下一次触发。
        /// </summary>
        private void TryApplyAnchor()
        {
            if (!triggered)
            {
                return;
            }

            if (!control.IsAttachedToVisualTree())
            {
                StartWaitingForAttach();
                return;
            }

            StopWaitingForAttach();
            if (!control.IsEffectivelyVisible)
            {
                return;
            }

            control.Classes.Add("motion-enter");
            StartRetirement();
        }

        private void StartWaitingForAttach()
        {
            if (waitingForAttach)
            {
                return;
            }

            waitingForAttach = true;
            control.AttachedToVisualTree += OnAttachedToVisualTree;
        }

        private void StopWaitingForAttach()
        {
            if (!waitingForAttach)
            {
                return;
            }

            waitingForAttach = false;
            control.AttachedToVisualTree -= OnAttachedToVisualTree;
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args) =>
            TryApplyAnchor();

        private void StartRetirement()
        {
            var cancellation = new CancellationTokenSource();
            retirement = cancellation;
            _ = RetireAsync(cancellation);
        }

        private void CancelRetirement()
        {
            StopWaitingForAttach();
            var cancellation = retirement;
            retirement = null;
            if (cancellation is null)
            {
                return;
            }

            cancellation.Cancel();
            cancellation.Dispose();
        }

        private async Task RetireAsync(CancellationTokenSource cancellation)
        {
            var token = cancellation.Token;
            try
            {
                await Task.Delay(AnchorDuration, token);
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(retirement, cancellation))
                {
                    return;
                }

                control.Classes.Remove("motion-enter");
                ResetPresentation();
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == token)
            {
            }
            finally
            {
                if (ReferenceEquals(retirement, cancellation))
                {
                    retirement = null;
                    cancellation.Dispose();
                }
            }
        }

        /// <summary>
        /// 摘类后把入场动画改写的呈现属性复位。若动画在首帧渲染前就被摘除，Avalonia 会停在
        /// 起势帧且不回退（壳层与操作表面同一处置）：设置分类会整页停留在 Opacity=0，
        /// Toast 会停在滑入位移上。复位对已落定的动画无副作用。
        /// </summary>
        private void ResetPresentation()
        {
            control.Opacity = 1;
            if (control.RenderTransform is TranslateTransform translate)
            {
                translate.X = 0;
                translate.Y = 0;
            }
        }
    }

    private sealed class MotionEntranceOwner;
}
