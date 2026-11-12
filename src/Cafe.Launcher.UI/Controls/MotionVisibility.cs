using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Cafe.Launcher.UI.Helpers;

namespace Cafe.Launcher.UI.Controls;

internal static class MotionVisibility
{
    public static readonly AttachedProperty<bool> IsOpenProperty =
        AvaloniaProperty.RegisterAttached<MotionVisibilityOwner, Control, bool>("IsOpen");

    public static readonly AttachedProperty<bool> IsMotionEnabledProperty =
        AvaloniaProperty.RegisterAttached<MotionVisibilityOwner, Control, bool>("IsMotionEnabled");

    private static readonly ConditionalWeakTable<Control, MotionState> States = new();

    static MotionVisibility()
    {
        IsOpenProperty.Changed.AddClassHandler<Control>(OnIsOpenChanged);
        IsMotionEnabledProperty.Changed.AddClassHandler<Control>(OnIsMotionEnabledChanged);
    }

    public static bool GetIsOpen(Control control) =>
        control.GetValue(IsOpenProperty);

    public static void SetIsOpen(Control control, bool value) =>
        control.SetValue(IsOpenProperty, value);

    public static bool GetIsMotionEnabled(Control control) =>
        control.GetValue(IsMotionEnabledProperty);

    public static void SetIsMotionEnabled(Control control, bool value) =>
        control.SetValue(IsMotionEnabledProperty, value);

    internal static Task WaitForPendingExitAsync(Control control) =>
        States.TryGetValue(control, out var state)
            ? state.PendingExit
            : Task.CompletedTask;

    private static void OnIsOpenChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        var state = States.GetValue(control, static item => new MotionState(item));
        if (args.GetNewValue<bool>())
        {
            state.Open();
            return;
        }

        state.Close(GetIsMotionEnabled(control));
    }

    private static void OnIsMotionEnabledChanged(
        Control control,
        AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>() || !States.TryGetValue(control, out var state))
        {
            return;
        }

        if (GetIsOpen(control))
        {
            // 入场锚点只服务一次打开：关闭动效时摘掉，重开动效不会让已展开的叠层
            // 重新匹配 motion-enter 选择器而重放入场（§2.7 最新状态为准、不重放）。
            state.SettleEntrance();
            return;
        }

        state.Close(isMotionEnabled: false);
    }

    private sealed class MotionState(Control control)
    {
        private CancellationTokenSource? exitCancellation;

        public Task PendingExit { get; private set; } = Task.CompletedTask;

        public void Open()
        {
            CancelPendingExit();
            var wasPresented = control.IsAttachedToVisualTree() && control.IsEffectivelyVisible;
            control.IsVisible = true;
            control.Classes.Remove("motion-exit");
            if (!GetIsMotionEnabled(control))
            {
                control.Classes.Remove("motion-enter");
                return;
            }

            if (!wasPresented)
            {
                // 首次呈现前挂锚点时，样式动画会随首次布局立即落到终态（实测：叠层
                // scrim 与表面都不动）；先跑一遍布局让表面完成样式化，再挂锚点。
                control.UpdateLayout();
            }

            control.Classes.Add("motion-enter");
        }

        public void SettleEntrance()
        {
            control.Classes.Remove("motion-enter");
        }

        public void Close(bool isMotionEnabled)
        {
            CancelPendingExit();
            control.Classes.Remove("motion-enter");
            if (!isMotionEnabled)
            {
                control.Classes.Remove("motion-exit");
                control.IsVisible = false;
                PendingExit = Task.CompletedTask;
                return;
            }

            control.Classes.Add("motion-exit");
            exitCancellation = new CancellationTokenSource();
            PendingExit = CompleteExitAsync(exitCancellation);
        }

        private void CancelPendingExit()
        {
            var cancellation = exitCancellation;
            exitCancellation = null;
            if (cancellation is not null)
            {
                cancellation.Cancel();
                cancellation.Dispose();
            }

            PendingExit = Task.CompletedTask;
        }

        private async Task CompleteExitAsync(CancellationTokenSource cancellation)
        {
            var token = cancellation.Token;
            try
            {
                await Task.Delay(AnimationTimings.ExitAnimationDuration, token);
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(exitCancellation, cancellation))
                {
                    return;
                }

                control.IsVisible = false;
                control.Classes.Remove("motion-exit");
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == token)
            {
            }
            finally
            {
                if (ReferenceEquals(exitCancellation, cancellation))
                {
                    exitCancellation = null;
                    cancellation.Dispose();
                }
            }
        }
    }

    private sealed class MotionVisibilityOwner;
}
