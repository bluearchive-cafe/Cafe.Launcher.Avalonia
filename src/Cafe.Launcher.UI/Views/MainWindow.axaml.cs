using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Features.Games;
using Cafe.Launcher.UI.Features.Shell;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Models;
using FluentAvalonia.UI.Windowing;

namespace Cafe.Launcher.UI.Views;

internal partial class MainWindow : FAAppWindow
{
    private SystemTrayService? systemTray;
    private MainWindowViewModel? configuredViewModel;
    private readonly OperationSurfaceAnimator operationSurfaceAnimator;
    private readonly WindowFilePickerService? filePickerService;
    private readonly WindowMetricsService? windowMetrics;
    private readonly Cafe.Launcher.Core.Services.Diagnostics.ILauncherDiagnostics? diagnostics;
    private NavigationMethod gameRailNavigationMethod;
    private bool isClosingForShutdown;

    /// <summary>
    /// 无参公共构造：Avalonia 运行时 XAML 加载器可达性要求（AVLN3001，缺失即构建错误），
    /// 无头测试也经此获得无服务实例。生产入口（App.axaml.cs）一律使用注入构造。
    /// </summary>
    public MainWindow() : this(null, null)
    {
    }

    /// <summary>生产入口注入文件选取与窗口度量服务，并在本构造器中挂接本窗口。</summary>
    public MainWindow(
        WindowFilePickerService? filePickerService,
        WindowMetricsService? windowMetrics,
        Cafe.Launcher.Core.Services.Diagnostics.ILauncherDiagnostics? diagnostics = null)
    {
        InitializeComponent();
        // 首页无标题栏底色：标题栏高度与页头一致，原生窗口按钮覆盖在页头右端，
        // 页头为其保留 138 DIP；页头容器以 TitleBar 角色承担空白拖拽，页头内按钮
        // 以 User 角色保留点击与焦点。
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.Height = 48;
        TitleBar.BackgroundColor = Colors.Transparent;
        TitleBar.InactiveBackgroundColor = Colors.Transparent;
        TitleBar.ButtonBackgroundColor = Colors.Transparent;
        TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        // ListBox moves focus without a navigation method when handling the arrow keys.
        GameRail.AddHandler(KeyDownEvent, (_, _) => gameRailNavigationMethod = NavigationMethod.Directional,
            RoutingStrategies.Tunnel);
        GameRail.AddHandler(PointerPressedEvent, (_, _) => gameRailNavigationMethod = NavigationMethod.Pointer,
            RoutingStrategies.Tunnel);
        this.filePickerService = filePickerService;
        this.windowMetrics = windowMetrics;
        this.diagnostics = diagnostics;
        operationSurfaceAnimator = new OperationSurfaceAnimator(diagnostics);
        filePickerService?.Attach(this);
        windowMetrics?.Attach(this);
        KeyDown += OnKeyDown;
        Activated += OnActivated;
        Opened += PlayShellEntranceOnce;
        InitializeVideoBackground();
#if DEBUG
        InitializeVideoBackgroundPrototype();
#endif
    }

    private async void OnGameRailSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox rail || e.Source != rail
            || e.AddedItems.Count != 1 || e.AddedItems[0] is not GameNavigationItem entry
            || DataContext is not MainWindowViewModel { Games: { } games } viewModel)
        {
            return;
        }

        var focusMethod = gameRailNavigationMethod;
        // Let the native list finish updating selection before a switch can disable it or roll it back.
        await Dispatcher.Yield(DispatcherPriority.Input);
        if (!rail.IsLoaded || DataContext != viewModel || rail.SelectedItem != entry || games.ActiveEntry == entry)
        {
            return;
        }

        var hadFocus = rail.IsKeyboardFocusWithin;
        await games.SelectGameCommand.ExecuteAsync(entry);
        if (!rail.IsLoaded || DataContext != viewModel)
        {
            return;
        }

        // A blocked or failed switch leaves the displayed game as the selected entry.
        rail.SelectedItem = games.ActiveEntry;
        if (hadFocus && rail.IsEffectivelyEnabled
            && (rail.IsKeyboardFocusWithin || FocusManager?.GetFocusedElement() is null))
        {
            rail.ContainerFromItem(games.ActiveEntry)?.Focus(focusMethod);
        }
    }

    /// <summary>ADR-016: plays the one-shot whole-content fade-in; interactive from the first frame.</summary>
    private void PlayShellEntranceOnce(object? sender, EventArgs e)
    {
        Opened -= PlayShellEntranceOnce;
        RetireOperationSurfaceEntranceAnchor();
        RetireShellEntranceAnchor();
        var viewModel = configuredViewModel ?? DataContext as MainWindowViewModel;
        if (viewModel is not { IsMotionEnabled: true })
        {
            return;
        }

        // 在首个渲染帧之前压暗，再进入动画，避免“先全亮一帧再变暗”的闪白。
        ShellRoot.Opacity = 0;
        Dispatcher.UIThread.Post(
            () => ShellRoot.Classes.Add("motion-enter"),
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// ADR-016：底部操作表面的 motion-enter 仅作一次性入场锚点。入场窗期（Normal 档时长，
    /// 自窗口打开起必已覆盖入场全程）结束后摘除该类，避免运行期开启动效偏好时
    /// motion-bottom.motion-enabled.motion-enter 选择器重新匹配而重放入场。
    /// 摘除时同时恢复不透明度并把上升位移归零：若窗口在首帧渲染前被遮挡/合成暂停，动画
    /// 可能只应用了起势帧（Opacity=0、Y=+12）就随摘类停止且不回退，表面会不可见或整体
    /// 渲染在布局位置之下、底缘溢出客户区被窗口裁切。复位对正在升入或已落定的动画均无副作用。
    /// </summary>
    private void RetireOperationSurfaceEntranceAnchor()
    {
        var timer = new DispatcherTimer { Interval = MotionTokens.NormalDuration };
        timer.Tick += (sender, _) =>
        {
            if (sender is DispatcherTimer oneShot)
            {
                oneShot.Stop();
            }

            RetireOperationSurfaceEntranceAnchorNow();
        };
        timer.Start();
    }

    /// <summary>摘除操作表面入场锚点类，恢复不透明度并把上升位移归零；可重复调用（幂等）。</summary>
    private void RetireOperationSurfaceEntranceAnchorNow()
    {
        OperationSurface.Classes.Remove("motion-enter");
        OperationSurface.Opacity = 1;
        if (OperationSurface.RenderTransform is TranslateTransform entranceTranslate)
        {
            entranceTranslate.Y = 0;
        }
    }

    /// <summary>
    /// ADR-016：壳层 motion-enter 与操作表面锚点同理，仅作一次性入场。入场窗期（快速档
    /// 时长）结束后摘除，避免运行期重启动效偏好时 motion-shell.motion-enabled.motion-enter
    /// 选择器重新匹配而重放整壳淡入。
    /// </summary>
    private void RetireShellEntranceAnchor()
    {
        var timer = new DispatcherTimer { Interval = MotionTokens.FastDuration };
        timer.Tick += (sender, _) =>
        {
            if (sender is DispatcherTimer oneShot)
            {
                oneShot.Stop();
            }

            RetireShellEntranceAnchorNow();
        };
        timer.Start();
    }

    /// <summary>
    /// 摘除壳层入场锚点类并恢复透明度。PlayShellEntranceOnce 在入场前把 ShellRoot.Opacity
    /// 压到 0，摘类移除动画后若不显式回到 1，壳层会停留在全透明（如入场窗期内关闭动效）。
    /// 可重复调用（幂等）。
    /// </summary>
    private void RetireShellEntranceAnchorNow()
    {
        ShellRoot.Classes.Remove("motion-enter");
        ShellRoot.Opacity = 1;
    }

    public void ConfigureViewModel(MainWindowViewModel viewModel)
    {
        UnconfigureViewModel();
        configuredViewModel = viewModel;
        viewModel.Operations.MinimizeRequested += MinimizeToTray;
        viewModel.Operations.ExitRequested += ExitAfterLaunch;
        viewModel.Operations.ShowRequested += ShowWindow;
        viewModel.WindowChrome.MinimizeRequested += MinimizeWindow;
        viewModel.WindowChrome.CloseRequested += PerformClose;
        viewModel.WindowChrome.ShutdownRequested += RequestShutdown;
        viewModel.WindowChrome.RestoreRequested += ShowWindow;
        viewModel.Dialogs.ErrorCopyDetailsRequested += CopyErrorDetailsToClipboard;
        viewModel.Background.PreviousWallpaperFadingOut += FadeOutPreviousWallpaper;
        viewModel.Background.PropertyChanged += OnVideoBackgroundSettingChanged;
        viewModel.Operations.PropertyChanged += OnOperationsPropertyChanged;
        viewModel.PropertyChanged += OnRootMotionPreferenceChanged;
        viewModel.Settings.PropertyChanging += OnSettingsPreviewChanging;
        viewModel.Settings.PropertyChanged += OnSettingsPreviewChanged;
        UpdateVideoBackground();
    }

    /// <summary>
    /// ADR-016 壁纸交叉淡化：旧图先整层写入覆盖区（不经过过渡，避免“反向淡入”），
    /// 随后一次性淡出；取消令牌保证降动效切换立即停止。
    /// </summary>
    private void FadeOutPreviousWallpaper(IImage previousImage, CancellationToken cancellationToken)
    {
        BackgroundCrossFade.Source = previousImage;
        BackgroundCrossFade.Opacity = 1;

        var fadeOut = new Animation
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
                    Setters = { new Setter { Property = Visual.OpacityProperty, Value = 1d } },
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter { Property = Visual.OpacityProperty, Value = 0d } },
                },
            },
        };
        _ = RunPreviousWallpaperFadeAsync(fadeOut, previousImage, cancellationToken);
    }

    /// <summary>
    /// 淡出结束（完成或被取消）后立即摘除旧图引用，并把释放权归还 ViewModel：只有视图
    /// 确认 Source 已置空（Background.OnWallpaperOverlayReleased），ViewModel 才释放位图。
    /// 视觉树残留已释放位图时，渲染帧读取 Image.Source 的 PixelSize 会抛
    /// ObjectDisposedException 使进程崩溃。仅当覆盖层仍归属本次旧图时才清理，
    /// 避免竞态清掉快速连续切换时新一轮写入的旧图。
    /// </summary>
    private async Task RunPreviousWallpaperFadeAsync(
        Animation fadeOut,
        IImage previousImage,
        CancellationToken cancellationToken)
    {
        try
        {
            await fadeOut.RunAsync(BackgroundCrossFade, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 降动效切换或新一轮淡化取消本段动画；清理仍需进行，落入 finally。
        }
        finally
        {
            if (ReferenceEquals(BackgroundCrossFade.Source, previousImage))
            {
                BackgroundCrossFade.Source = null;
                BackgroundCrossFade.Opacity = 1;
            }

            configuredViewModel?.Background.OnWallpaperOverlayReleased(previousImage);
        }
    }

    private void OnOperationsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GameOperationsViewModel.PanelMode))
        {
            return;
        }

        // PanelMode 是 setter 里最先抛出的属性：同步执行会在 IsXxxPanelVisible 绑定刷新前
        // 测量（此刻旧状态仍可见，量得的是旧自然高度）。统一推迟一拍，让新状态的可见性
        // 先落位再测量；后台线程变更本就须经 Dispatcher 汇入，同走此路径。
        Dispatcher.UIThread.Post(
            () => operationSurfaceAnimator.Transition(
                OperationSurface,
                configuredViewModel is { IsMotionEnabled: true },
                RetireOperationSurfaceEntranceAnchorNow));
    }

    private void OnRootMotionPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsMotionEnabled))
        {
            return;
        }

        UpdateVideoPlayback();

        // 运行期关闭动效时立即结算壳层入场：入场动画随选择器失配被移除，必须同步清掉
        // 锚点类并恢复本地透明度，否则壳层停留在 PlayShellEntranceOnce 写入的透明度 0。
        // 摘类同时保证之后重开动效时 motion-shell 选择器不会重新匹配而重放入场。
        if (configuredViewModel is { IsMotionEnabled: false })
        {
            RetireShellEntranceAnchorNow();
        }

        operationSurfaceAnimator.Settle(OperationSurface);
    }

    protected override void OnClosed(EventArgs e)
    {
        UnconfigureViewModel();
        filePickerService?.Detach(this);
        windowMetrics?.Detach(this);
        base.OnClosed(e);
    }

    private void UnconfigureViewModel()
    {
        if (configuredViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.Background.PreviousWallpaperFadingOut -= FadeOutPreviousWallpaper;
        viewModel.Background.PropertyChanged -= OnVideoBackgroundSettingChanged;
        RemoveVideoBackground();
        viewModel.Operations.MinimizeRequested -= MinimizeToTray;
        viewModel.Operations.ExitRequested -= ExitAfterLaunch;
        viewModel.Operations.ShowRequested -= ShowWindow;
        viewModel.WindowChrome.MinimizeRequested -= MinimizeWindow;
        viewModel.WindowChrome.CloseRequested -= PerformClose;
        viewModel.WindowChrome.ShutdownRequested -= RequestShutdown;
        viewModel.WindowChrome.RestoreRequested -= ShowWindow;
        viewModel.Dialogs.ErrorCopyDetailsRequested -= CopyErrorDetailsToClipboard;
        viewModel.Operations.PropertyChanged -= OnOperationsPropertyChanged;
        viewModel.PropertyChanged -= OnRootMotionPreferenceChanged;
        viewModel.Settings.PropertyChanging -= OnSettingsPreviewChanging;
        viewModel.Settings.PropertyChanged -= OnSettingsPreviewChanged;
        operationSurfaceAnimator.Settle(OperationSurface);
        viewModel.RemoteContent.SetBannerPointerOver(false);
        viewModel.RemoteContent.SetBannerFocusWithin(false);

        configuredViewModel = null;
    }

    private void OnBannerPointerEntered(object? sender, PointerEventArgs e) =>
        configuredViewModel?.RemoteContent.SetBannerPointerOver(true);

    private void OnBannerPointerExited(object? sender, PointerEventArgs e) =>
        configuredViewModel?.RemoteContent.SetBannerPointerOver(false, hideControls: true);

    private void OnBannerGotFocus(object? sender, FocusChangedEventArgs e) =>
        configuredViewModel?.RemoteContent.SetBannerFocusWithin(true);

    private void OnBannerLostFocus(object? sender, FocusChangedEventArgs e) =>
        configuredViewModel?.RemoteContent.SetBannerFocusWithin(false);

    private void OnActivated(object? sender, EventArgs e)
    {
        configuredViewModel?.RefreshSystemMotionPreference();
    }

    private void MinimizeWindow() => WindowState = WindowState.Minimized;

    /// <summary>
    /// Game-launch minimize path. The launch toast reports "minimized to tray", so hide the
    /// window whenever a tray icon exists; without one a hidden window could not be restored,
    /// so fall back to taskbar minimize (same rule as <see cref="PerformClose"/>).
    /// The title-bar minimize button keeps <see cref="MinimizeWindow"/> — it asks for a
    /// taskbar minimize, not a tray hide.
    /// </summary>
    private void MinimizeToTray() =>
        PerformWindowAction(WindowClosePolicy.DecideMinimize(systemTray is not null));

    /// <summary>
    /// Game-launch exit path, taken when the user picks "exit the launcher" as the post-launch
    /// behavior. Deliberately does not go through <see cref="PerformClose"/>: that path routes the
    /// close through the saved CloseBehavior, so a user who keeps "minimize to tray" there would
    /// turn this setting into a silent no-op.
    /// </summary>
    private void ExitAfterLaunch() => RequestShutdown();

    private void RequestShutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.TryShutdown();
            return;
        }

        CloseForShutdown();
    }

    internal void CloseForShutdown()
    {
        isClosingForShutdown = true;
        try
        {
            Close();
        }
        finally
        {
            isClosingForShutdown = false;
        }
    }

    public void SetSystemTray(SystemTrayService trayService)
    {
        systemTray = trayService;
    }

    public void ApplySavedWindowState(LauncherSettings settings)
    {
        var restore = WindowGeometryPolicy.Restore(settings, MinWidth, MinHeight);
        if (restore.Width is { } width)
        {
            Width = width;
        }

        if (restore.Height is { } height)
        {
            Height = height;
        }

        if (restore.Position is { } position)
        {
            Position = position;
        }
    }

    /// <summary>
    /// 在 UI 线程采集普通状态下的窗口几何值快照。落盘路径只接受这份快照：
    /// 设置写入方的改动委托在事务锁竞争时可能在线程池执行，不能让它回头读窗口属性。
    /// </summary>
    internal WindowGeometry? TryCaptureGeometry() =>
        WindowGeometry.TryCapture(WindowState == WindowState.Normal, Position, Width, Height);

    /// <summary>
    /// Avalonia's drawn caption button calls Close(), while native close requests come
    /// through the platform. Both follow the user's close preference. App shutdown
    /// has its own close reason or calls CloseForShutdown when there is no lifetime.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            base.OnClosing(e);
            return;
        }

        // Uninstall owns the filesystem: every close path (including programmatic Close)
        // stays refused until it completes.
        if (WindowClosePolicy.IsCloseRefused(viewModel.Operations.IsUninstallExecuting))
        {
            e.Cancel = true;
        }
        else if (!isClosingForShutdown && e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            viewModel.WindowChrome.CloseCommand.Execute(null);
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// Native minimize/restore no longer passes through a caption command, so pause and resume
    /// the banner carousel from the actual window state instead.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WindowStateProperty || configuredViewModel is not { } viewModel)
        {
            return;
        }

        if (WindowState == WindowState.Minimized)
        {
            viewModel.WindowChrome.NotifyWindowMinimized();
        }
        else if (WindowState == WindowState.Normal)
        {
            viewModel.WindowChrome.NotifyWindowRestored();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (vm.TryHandleEscape())
        {
            e.Handled = true;
        }
    }

    private void PerformClose()
    {
        var action = DataContext is MainWindowViewModel viewModel
            ? WindowClosePolicy.Decide(
                viewModel.Settings.Editor.GetSavedSnapshot().CloseBehavior,
                viewModel.Operations.IsUninstallExecuting,
                systemTray is not null)
            : WindowCloseAction.Shutdown;

        PerformWindowAction(action);
    }

    /// <summary>
    /// 把判定结果落到窗口。没有托盘时不能 <c>Hide()</c>：隐藏后的窗口无法恢复，
    /// 因此退到任务栏最小化（与关闭、启动后最小化两条路径共用同一规则）。
    /// </summary>
    private void PerformWindowAction(WindowCloseAction action)
    {
        switch (action)
        {
            case WindowCloseAction.HideToTray when systemTray is not null:
                systemTray.HideWindow();
                break;
            case WindowCloseAction.HideToTray:
            case WindowCloseAction.MinimizeToTaskbar:
                WindowState = WindowState.Minimized;
                break;
            case WindowCloseAction.Shutdown:
                RequestShutdown();
                break;
        }
    }

    public void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void CopyErrorDetailsToClipboard(string details)
    {
        // ErrorCopyDetailsRequested 是 Action<string> 事件；异步主体自带 try/catch，
        // 丢弃 Task 不会产生未观察异常。
        _ = CopyErrorDetailsToClipboardAsync(details);
    }

    private async Task CopyErrorDetailsToClipboardAsync(string details)
    {
        if (Clipboard is not null)
        {
            try
            {
                await Clipboard.SetTextAsync(details);
            }
            catch (Exception ex)
            {
                _ = diagnostics?.WarningAsync(
                    "ClipboardCopyFailed",
                    $"Failed to copy error details to clipboard: {ex.Message}");
            }
        }
    }
}
