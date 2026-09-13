using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Views;

/// <summary>
/// ADR-016 游戏操作表面连续转换的动效协作者，自窗口 code-behind 抽出以便脱离
/// 窗口实例驱动与测试。状态在单一任务容器内原地交换后，先把容器临时固定在
/// 旧高度并测得新状态的自然高度，再以点到点曲线把高度连续过渡过去，同时用瞬时透明度
/// 下沉与快速恢复提示内容更替。新状态触发时先取消在途动画再测量与起播，始终以最新
/// 布局为准，不排队；降动效、未附着或首帧无尺寸时直接落定。
/// </summary>
internal sealed class OperationSurfaceAnimator
{
    /// <summary>内容更替提示的透明度下沉值（对齐 FluentMotionLab 场景 6 的 Fluent 分支）。</summary>
    private const double DipOpacity = 0.58;

    /// <summary>
    /// 下沉恢复完成的进度点 = 快速档/标准档时长比（167ms/250ms），其后平尾保持到收尾。
    /// 由动效 token 派生，时长档调整时恢复点自动跟随。
    /// </summary>
    private static readonly double DipRecoveryCue =
        MotionTokens.FastDuration.TotalMilliseconds / MotionTokens.NormalDuration.TotalMilliseconds;

    private CancellationTokenSource? transitionCts;

    /// <summary>
    /// 以连续高度形变 + 下沉/恢复过渡到新状态。窗口侧通过 <paramref name="retireEntranceAnchor"/>
    /// 提供一次性入场锚点的即时摘除：锚点的类动画持有 Opacity，会与本转换的下沉/恢复段互相覆盖。
    /// </summary>
    public void Transition(Border surface, bool motionEnabled, Action? retireEntranceAnchor = null)
    {
        var fromHeight = surface.Bounds.Height;
        if (!motionEnabled
            || !surface.IsAttachedToVisualTree()
            || !double.IsFinite(fromHeight)
            || fromHeight <= 0)
        {
            Settle(surface);
            return;
        }

        // 最新状态立即接管：先取消在途动画、交还本地值，否则在途动画以 Animation 优先级
        // 持有 Height，下面的测量会被旧动画的当前帧高度污染（ADR-016：不排队，最新为准）。
        Cancel();

        // 入场窗期内发生状态切换时立即摘除一次性入场锚点：先摘除让本次转换的下沉/恢复段独占
        // 透明度，摘除同时把上升位移归零。锚点已摘除时此调用幂等无副作用。
        retireEntranceAnchor?.Invoke();

        // 冻结旧视觉尺寸后让可见性绑定推过一轮布局，测得新状态的自然容器高度。
        // 三个状态各自携带 bottom-panel 的 MinHeight（≥132），布局后目标高度必有下界。
        surface.Height = double.NaN;
        surface.UpdateLayout();
        var targetHeight = surface.Bounds.Height;

        surface.Height = fromHeight;
        surface.UpdateLayout();

        // 瞬时写入下沉值，保证首个渲染帧即处于下沉态（对齐 FluentMotionLab 场景 6 的
        // Fluent 分支），随后的恢复段动画负责拉回。
        surface.Opacity = DipOpacity;

        var cts = new CancellationTokenSource();
        transitionCts = cts;
        _ = RunTransitionAsync(surface, fromHeight, targetHeight, cts);
    }

    public void Settle(Border? surface)
    {
        Cancel();
        if (surface is not null)
        {
            surface.Opacity = 1;
            surface.Height = double.NaN;
        }
    }

    /// <summary>取消在途动画并把令牌源移出所有权槽；释放与几何结算由各任务收尾或落定路径负责。</summary>
    private void Cancel()
    {
        transitionCts?.Cancel();
        transitionCts = null;
    }

    private async Task RunTransitionAsync(
        Border surface,
        double fromHeight,
        double targetHeight,
        CancellationTokenSource cancellation)
    {
        try
        {
            var token = cancellation.Token;
            await Task.WhenAll(
                CreateHeightAnimation(fromHeight, targetHeight).RunAsync(surface, token),
                CreateDipAnimation().RunAsync(surface, token));
        }
        catch (OperationCanceledException)
        {
            // 更新状态已接管或动效被关闭；几何统一由 finally 的所有权守卫结算。
        }
        catch (Exception exception)
        {
            // 形变失败不得阻断状态切换本身；几何仍由 finally 结算，异常落日志而非静默丢弃。
            await LocalDiagnostics.LogAsync(
                LogEntrySeverity.Warn,
                "OperationSurfaceMotion",
                $"Operation surface transition failed: {exception.Message}");
        }
        finally
        {
            if (ReferenceEquals(transitionCts, cancellation))
            {
                transitionCts = null;
                surface.Opacity = 1;
                surface.Height = double.NaN;
            }

            // 令牌源只由持有它的任务收尾释放，取消方仅负责 Cancel，避免与在途取消回调竞态。
            cancellation.Dispose();
        }
    }

    private static Animation CreateHeightAnimation(double fromHeight, double targetHeight) => new()
    {
        Duration = MotionTokens.NormalDuration,
        Easing = MotionResourceLookup.GetEasing(
            "Launcher.Motion.Easing.PointToPoint",
            static () => new SplineEasing { X1 = 0.55, Y1 = 0.55, X2 = 0, Y2 = 1 }),
        FillMode = FillMode.Forward,
        Children =
        {
            new KeyFrame
            {
                Cue = new Cue(0),
                Setters = { new Setter { Property = Layoutable.HeightProperty, Value = fromHeight } },
            },
            new KeyFrame
            {
                Cue = new Cue(1),
                Setters = { new Setter { Property = Layoutable.HeightProperty, Value = targetHeight } },
            },
        },
    };

    /// <summary>
    /// 透明度下沉的恢复段：切换瞬间容器已写入 0.58 下沉值（见
    /// <see cref="OperationSurfaceAnimator.Transition"/>），本动画以 167ms 进入曲线拉回全
    /// 不透明，随后保持到标准档收尾，使恢复段与高度形变同拍结算，避免动画提前释放后
    /// 回落为下沉值。对齐 FluentMotionLab 场景 6 的 Fluent 分支。
    /// </summary>
    private static Animation CreateDipAnimation() => new()
    {
        Duration = MotionTokens.NormalDuration,
        Easing = MotionResourceLookup.GetEasing(
            "Launcher.Motion.Easing.Enter",
            static () => new SplineEasing { X1 = 0, Y1 = 0, X2 = 0, Y2 = 1 }),
        FillMode = FillMode.Forward,
        Children =
        {
            new KeyFrame
            {
                Cue = new Cue(0),
                Setters = { new Setter { Property = Visual.OpacityProperty, Value = DipOpacity } },
            },
            new KeyFrame
            {
                // 167ms/250ms：快速档处即恢复完成，其后平尾保持。
                Cue = new Cue(DipRecoveryCue),
                Setters = { new Setter { Property = Visual.OpacityProperty, Value = 1d } },
            },
            new KeyFrame
            {
                Cue = new Cue(1),
                Setters = { new Setter { Property = Visual.OpacityProperty, Value = 1d } },
            },
        },
    };
}
