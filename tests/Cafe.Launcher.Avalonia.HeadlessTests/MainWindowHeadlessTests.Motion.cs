using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public async Task MainWindow_WhenPanelModeChanges_TaskSurfaceAnimatesThenSettles()
    {
        using var context = CreateContext();
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var surface = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("operation-surface"));
        Assert.True(surface.Bounds.Height > 0);
        var installedHeight = surface.Bounds.Height;

        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control;
        // 形变完成后回到自动尺寸、恢复全不透明，且控制态自然高度大于安装态（156 对 132），
        // 证明转换走过了"测新状态自然高度"的管线而非瞬切。
        await HeadlessTestHost.WaitUntilAsync(
            () => double.IsNaN(surface.Height) && surface.Opacity >= 1d,
            TimeSpan.FromSeconds(3),
            "Operation surface did not settle back to auto height and full opacity.");
        Assert.True(
            surface.Bounds.Height > installedHeight,
            $"Control state height {surface.Bounds.Height} did not grow past install state {installedHeight}.");
    }

    [AvaloniaFact]
    public void MainWindow_WhenMotionReduced_TaskSurfaceSwitchesWithoutAnimation()
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = true;
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var surface = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("operation-surface"));
        Assert.True(surface.Bounds.Height > 0);

        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control;
        Dispatcher.UIThread.RunJobs();

        Assert.True(double.IsNaN(surface.Height));
        Assert.Equal(1d, surface.Opacity);
    }

    [AvaloniaFact]
    public async Task MainWindow_BackgroundThreadSwitchAndRuntimeMotionReduction_KeepTaskSurfaceConsistent()
    {
        using var context = CreateContext();
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        // 后台线程（真实应用中进度回调的常见来源）触发面板切换，须经 Dispatcher 汇入 UI 线程。
        await Task.Run(() => context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control);
        var controlState = default(Border);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { });
            controlState = context.Window.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(control => control.Classes.Contains("operation-state")
                    && control.Classes.Contains("control-panel"));
            if (controlState?.IsVisible == true)
            {
                break;
            }

            await Task.Delay(10);
        }

        Assert.True(controlState?.IsVisible == true, "Control state did not surface after background switch.");

        // 运行期关闭动效：形变立即落定（自动高度、全不透明），状态本身不受影响。
        context.ViewModel.IsMotionReduced = true;
        Dispatcher.UIThread.RunJobs();

        var surface = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("operation-surface"));
        Assert.True(double.IsNaN(surface.Height));
        Assert.Equal(1d, surface.Opacity);
        Assert.True(controlState.IsVisible);

        // 关闭窗口触发退订与表面落定，保证无泄漏的取消源残留。
        context.Window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task MainWindow_WhenWallpaperCrossFadeEnds_CrossFadeSourceIsCleared()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var crossFade = context.Window.GetVisualDescendants().OfType<Image>()
            .Single(image => image.Name == "BackgroundCrossFade");

        // 切换到自定义壁纸触发 ADR-016 交叉淡化：旧图所有权在 ViewModel（宽限期后释放），
        // 视图层必须在淡化结束后摘除引用，否则视觉树残留已释放位图——DevTools 悬停/选择
        // 元素读取 Image.Source 的 PixelSize 会抛 ObjectDisposedException 使进程崩溃。
        var wallpaperPath = Path.Combine(
            Path.GetTempPath(),
            $"launcher-wallpaper-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(
            wallpaperPath,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="));
        try
        {
            var settings = new LauncherSettings
            {
                BackgroundSource = BackgroundSources.Custom,
                CustomBackgroundPath = wallpaperPath,
                BackgroundFit = BackgroundFits.UniformToFill,
                ThemeColorMode = ThemeColorModes.Default
            };
            await context.ViewModel.Background.UpdateBackgroundImageAsync(
                settings,
                snapshot: null,
                CancellationToken.None);

            await HeadlessTestHost.WaitUntilAsync(
                () => crossFade.Source is null,
                TimeSpan.FromSeconds(3));

            Assert.Null(crossFade.Source);
        }
        finally
        {
            File.Delete(wallpaperPath);
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_WhenPanelModeSwitchesRapidly_SurfaceSettlesAtLatestStateNaturalHeight()
    {
        using var context = CreateContext();
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var surface = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("operation-surface"));
        var installHeight = surface.Bounds.Height;
        Assert.True(installHeight > 0);

        // 单次切换参考值：控制态自然高度大于安装态，落定后回到自动高度与全不透明。
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control;
        var controlHeight = await WaitUntilOperationSurfaceSettledAsync(surface);
        Assert.True(controlHeight > installHeight,
            $"Control state height {controlHeight} did not grow past install state {installHeight}.");

        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
        var reSettledHeight = await WaitUntilOperationSurfaceSettledAsync(surface);
        Assert.True(Math.Abs(reSettledHeight - installHeight) < 0.5,
            $"Re-settled install height {reSettledHeight} deviates from initial {installHeight}.");

        // 快速连续切换（ADR-016：高频变化以最新状态为准，不排队）：Headless 动画不按墙钟
        // 推进、无法采样形变中途，但最终几何必须收敛到最新状态，且不得残留冻结高度或
        // 下沉透明度。
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control;
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control;

        var finalHeight = await WaitUntilOperationSurfaceSettledAsync(surface);
        Assert.True(Math.Abs(finalHeight - controlHeight) < 0.5,
            $"Height after rapid switches {finalHeight} deviates from control natural height {controlHeight}.");
    }

    [AvaloniaFact]
    public async Task MainWindow_AfterEntranceWindow_EntranceAnchorsAreRetired()
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = false;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var surface = FindOperationSurface(context.Window);
        var shellRoot = FindShellRoot(context.Window);
        Assert.Contains("motion-enter", surface.Classes);
        Assert.Contains("motion-enter", shellRoot.Classes);

        // 入场窗期（快速档/标准档时长）结束后两类锚点必须摘除：壳层与操作表面恢复
        // 全不透明，且操作表面上升位移归零；此后重启动效偏好时 motion-* 选择器不会
        // 重新匹配而重放入场。
        await HeadlessTestHost.WaitUntilAsync(
            () => !surface.Classes.Contains("motion-enter") && !shellRoot.Classes.Contains("motion-enter"),
            TimeSpan.FromSeconds(3));

        Assert.DoesNotContain("motion-enter", surface.Classes);
        Assert.DoesNotContain("motion-enter", shellRoot.Classes);
        Assert.Equal(1d, surface.Opacity);
        Assert.Equal(1d, shellRoot.Opacity);
        var translate = Assert.IsType<TranslateTransform>(surface.RenderTransform);
        Assert.Equal(0, translate.Y);
    }

    [AvaloniaFact]
    public void MainWindow_WhenMotionDisabledDuringEntranceWindow_ShellAnchorRetiresAndOpacityRestores()
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = false;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var shellRoot = FindShellRoot(context.Window);
        Assert.Contains("motion-enter", shellRoot.Classes);

        // 入场窗期内关闭动效：壳层锚点立即摘除、透明度回到 1，
        // 不得停留在 PlayShellEntranceOnce 入场前写入的 0（否则整壳不可见）。
        context.ViewModel.IsMotionReduced = true;
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("motion-enter", shellRoot.Classes);
        Assert.Equal(1d, shellRoot.Opacity);
    }

    [AvaloniaFact]
    public async Task MainWindow_AfterAnchorsRetired_MotionPreferenceToggleDoesNotRestoreEntranceClasses()
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = false;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var surface = FindOperationSurface(context.Window);
        var shellRoot = FindShellRoot(context.Window);
        await HeadlessTestHost.WaitUntilAsync(
            () => !surface.Classes.Contains("motion-enter") && !shellRoot.Classes.Contains("motion-enter"),
            TimeSpan.FromSeconds(3));

        // 运行期关闭再重开动效偏好：锚点类不得回流，否则 motion-enabled 重新匹配会重放入场。
        context.ViewModel.IsMotionReduced = true;
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.IsMotionReduced = false;
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("motion-enter", surface.Classes);
        Assert.DoesNotContain("motion-enter", shellRoot.Classes);
        Assert.Equal(1d, surface.Opacity);
        Assert.Equal(1d, shellRoot.Opacity);
    }

    [AvaloniaFact]
    public async Task MainWindow_WhenPanelModeChangesDuringEntranceWindow_AnchorRetiresEarlyAndSurfaceSettles()
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = false;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var surface = FindOperationSurface(context.Window);
        Assert.Contains("motion-enter", surface.Classes);

        // 入场窗期内的状态切换必须立即摘除锚点：锚点类动画持有 Opacity，会与转换的
        // 下沉写入/恢复段互相覆盖；摘除后由转换动画独占透明度并正常结算。
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control;
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("motion-enter", surface.Classes);

        await WaitUntilOperationSurfaceSettledAsync(surface);
        Assert.Equal(1d, surface.Opacity);
    }

    private static Border FindOperationSurface(Window window) =>
        window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("operation-surface"));

    private static Grid FindShellRoot(Window window) =>
        window.GetVisualDescendants().OfType<Grid>()
            .Single(control => control.Name == "ShellRoot");

    private static async Task<double> WaitUntilOperationSurfaceSettledAsync(Border surface)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { });
            await Task.Delay(10);
            if (double.IsNaN(surface.Height) && surface.Opacity >= 1d)
            {
                return surface.Bounds.Height;
            }
        }

        Assert.Fail("Operation surface did not settle back to auto height and full opacity.");
        return double.NaN;
    }

    [AvaloniaFact]
    public async Task MainWindow_BackgroundThreadSwitch_MorphsToLatestStateNaturalHeight()
    {
        using var context = CreateContext();
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var surface = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("operation-surface"));
        var installHeight = surface.Bounds.Height;
        Assert.True(installHeight > 0);

        // 后台线程（真实进度回调的常见来源）触发切换时，posted 转换必须在可见性绑定
        // 刷新之后测量，收敛高度应落在控制态自然高度而非起飞前高度。
        await Task.Run(() => context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control);

        await HeadlessTestHost.WaitUntilAsync(
            () => double.IsNaN(surface.Height) && surface.Opacity >= 1d,
            TimeSpan.FromSeconds(3),
            "Operation surface did not settle after background switch.");
        Assert.True(
            surface.Bounds.Height > installHeight,
            $"Control state height {surface.Bounds.Height} did not grow past install state {installHeight}.");
    }

    [AvaloniaFact]
    public void ShellLifecycle_FirstLaunchMotionPreference_AppliesSystemResolution()
    {
        // 首启分支不执行完整初始化（快照由向导驱动后再加载）：动效偏好必须按默认
        // System 档先行解析并应用，否则 IsMotionReduced 停留在默认 true，首启向导全程瞬切。
        using var context = CreateContext();
        var runtime = context.Provider
            .GetRequiredService<Cafe.Launcher.Avalonia.Features.Shell.IShellRuntime>();
        var windowsAnimationsEnabled = new WindowsAnimationSettingsProvider()
            .GetWindowsAnimationsEnabled();
        var expectedReduced = Cafe.Launcher.Avalonia.Helpers.MotionSettingsResolver.ShouldReduceMotion(
            MotionModes.System,
            windowsAnimationsEnabled);

        runtime.ApplyFirstLaunchMotionPreference();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(expectedReduced, runtime.IsMotionReduced);
        Assert.Equal(!expectedReduced, context.ViewModel.IsMotionEnabled);
    }
}
