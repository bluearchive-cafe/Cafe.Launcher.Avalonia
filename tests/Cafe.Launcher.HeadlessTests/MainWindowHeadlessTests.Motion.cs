using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public async Task MainWindow_WhenPanelModeChanges_TaskSurfaceKeepsItsHeight()
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
        // 统一面板不再随安装/控制状态改变几何；仍须恢复自动尺寸与全不透明。
        await HeadlessTestHost.WaitUntilAsync(
            () => double.IsNaN(surface.Height) && surface.Opacity >= 1d,
            TimeSpan.FromSeconds(3),
            "Operation surface did not settle back to auto height and full opacity.");
        Assert.Equal(installedHeight, surface.Bounds.Height);
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

        // 状态切换保持同一个面板的自然高度与全不透明。
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control;
        var controlHeight = await WaitUntilOperationSurfaceSettledAsync(surface);
        Assert.Equal(installHeight, controlHeight);

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
    public async Task MainWindow_FirstSettingsOpen_ShowsSelectedSectionAtFullOpacity()
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = false;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        // 首次打开前设置内容尚未进入可视树。锚点若在此前挂上又摘除，样式动画会停在
        // 起势帧 Opacity=0 且不回退——整页空白，只有切换分类才重建。
        await Task.Delay(250);
        await Dispatcher.UIThread.InvokeAsync(() => { });

        context.ViewModel.WindowChrome.ShowSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var section = context.Window.GetVisualDescendants()
            .OfType<UserControl>()
            .Single(control => control.Classes.Contains("motion-content") && control.IsVisible);
        await HeadlessTestHost.WaitUntilAsync(
            () => section.Opacity >= 1d && !section.Classes.Contains("motion-enter"),
            TimeSpan.FromSeconds(3),
            "First settings open left the selected section hidden.");
    }

    [AvaloniaFact]
    public async Task MainWindow_FirstSettingsOpen_RealizesSurfaceBeforeAttachingEntranceAnchor()
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = false;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var overlay = context.Window.GetVisualDescendants().OfType<Grid>()
            .Single(control => control.Classes.Contains("settings-overlay"));
        var surface = overlay.GetVisualDescendants()
            .OfType<Cafe.Launcher.UI.Controls.DialogSurface>()
            .Single();
        var arrangedWhenAnchorAttached = false;
        var anchorAttached = false;
        overlay.Classes.CollectionChanged += (_, args) =>
        {
            if (anchorAttached
                || args.Action != NotifyCollectionChangedAction.Add
                || !Equals(args.NewItems?[0], "motion-enter"))
            {
                return;
            }

            anchorAttached = true;
            arrangedWhenAnchorAttached = surface.Bounds.Width > 0 && surface.Bounds.Height > 0;
        };

        // 首次打开前叠层从未呈现。若锚点与「转为可见」同批次生效，样式动画会随首次
        // 布局直接落到终态——scrim 与表面都纹丝不动，只有第二次打开才播放入场。
        // 因此锚点必须在表面完成布局之后才挂上（见 MotionVisibility.Open）。
        await Task.Delay(250);
        await Dispatcher.UIThread.InvokeAsync(() => { });
        context.ViewModel.WindowChrome.ShowSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(anchorAttached, "First settings open did not attach the entrance anchor.");
        Assert.True(
            arrangedWhenAnchorAttached,
            "Entrance anchor was attached before the settings surface was laid out.");

        var translate = Assert.IsType<TranslateTransform>(surface.RenderTransform);
        await HeadlessTestHost.WaitUntilAsync(
            () => surface.Opacity >= 1d && translate.Y == 0d,
            TimeSpan.FromSeconds(3),
            "Settings surface entrance did not settle at full opacity.");
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
    public async Task MainWindow_SettingsSectionEntrance_RetiresAnchor_AndMotionToggleDoesNotReplay()
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = false;
        OpenSettings(context);

        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Game;
        Dispatcher.UIThread.RunJobs();

        var section = context.Window.GetVisualDescendants()
            .OfType<UserControl>()
            .Single(control => control.Classes.Contains("motion-content") && control.IsVisible);
        Assert.Contains("motion-enter", section.Classes);

        // 分类淡化的入场锚点必须在快速档后摘除：随后重开动效偏好时，
        // motion-content.motion-enabled 不得因为锚点仍在而重播（§2.7）。
        await HeadlessTestHost.WaitUntilAsync(
            () => !section.Classes.Contains("motion-enter"),
            TimeSpan.FromSeconds(3),
            "Settings section entrance anchor did not retire.");

        context.ViewModel.IsMotionReduced = true;
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.IsMotionReduced = false;
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("motion-enter", section.Classes);
        var overlay = context.Window.GetVisualDescendants().OfType<Grid>()
            .Single(control => control.Classes.Contains("settings-overlay"));
        Assert.DoesNotContain("motion-enter", overlay.Classes);
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
    public async Task MainWindow_BackgroundThreadSwitch_KeepsStablePanelHeight()
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
        Assert.Equal(installHeight, surface.Bounds.Height);
    }

    [AvaloniaFact]
    public void ShellLifecycle_FirstLaunchMotionPreference_AppliesSystemResolution()
    {
        // 首启分支不执行完整初始化（快照由向导驱动后再加载）：动效偏好必须按默认
        // System 档先行解析并应用，否则 IsMotionReduced 停留在默认 true，首启向导全程瞬切。
        using var context = CreateContext();
        var runtime = context.Provider
            .GetRequiredService<Cafe.Launcher.UI.Features.Shell.ShellLifecycle>();
        var systemAnimationsEnabled = new SystemAnimationSettingsProvider()
            .GetSystemAnimationsEnabled();
        var expectedReduced = Cafe.Launcher.UI.Helpers.MotionSettingsResolver.ShouldReduceMotion(
            MotionModes.System,
            systemAnimationsEnabled);

        runtime.ApplyFirstLaunchMotionPreference();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(expectedReduced, runtime.IsMotionReduced);
        Assert.Equal(!expectedReduced, context.ViewModel.IsMotionEnabled);
    }
}
