using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Views;

public partial class MainWindow : Window
{
    private SystemTrayService? systemTray;
    private MainWindowViewModel? configuredViewModel;
    private readonly OperationSurfaceAnimator operationSurfaceAnimator;
    private readonly WindowFilePickerService? filePickerService;
    private readonly WindowMetricsService? windowMetrics;
    private readonly Services.Diagnostics.LocalDiagnostics? diagnostics;

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
        Services.Diagnostics.LocalDiagnostics? diagnostics = null)
    {
        InitializeComponent();
        this.filePickerService = filePickerService;
        this.windowMetrics = windowMetrics;
        this.diagnostics = diagnostics;
        operationSurfaceAnimator = new OperationSurfaceAnimator(diagnostics);
        filePickerService?.Attach(this);
        windowMetrics?.Attach(this);
        PointerPressed += OnPointerPressed;
        KeyDown += OnKeyDown;
        Activated += OnActivated;
        Opened += PlayShellEntranceOnce;
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
        viewModel.Operations.PropertyChanged += OnOperationsPropertyChanged;
        viewModel.PropertyChanged += OnRootMotionPreferenceChanged;
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
    private void MinimizeToTray()
    {
        if (systemTray is not null)
        {
            systemTray.HideWindow();
            return;
        }

        WindowState = WindowState.Minimized;
    }

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

        Close();
    }

    public void SetSystemTray(SystemTrayService trayService)
    {
        systemTray = trayService;
    }

    internal void ApplySavedWindowState(LauncherSettings settings)
    {
        if (!settings.RememberWindowPositionAndSize)
        {
            return;
        }

        if (settings.WindowWidth is double width && double.IsFinite(width) && width > 0)
        {
            Width = Math.Max(MinWidth, width);
        }

        if (settings.WindowHeight is double height && double.IsFinite(height) && height > 0)
        {
            Height = Math.Max(MinHeight, height);
        }

        if (settings.WindowPositionX is int x && settings.WindowPositionY is int y)
        {
            Position = new PixelPoint(x, y);
        }
    }

    internal void CaptureWindowState(LauncherSettings settings)
    {
        if (!settings.RememberWindowPositionAndSize || WindowState != WindowState.Normal)
        {
            return;
        }

        settings.WindowPositionX = Position.X;
        settings.WindowPositionY = Position.Y;
        if (double.IsFinite(Width) && Width > 0)
        {
            settings.WindowWidth = Width;
        }

        if (double.IsFinite(Height) && Height > 0)
        {
            settings.WindowHeight = Height;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsWithinTitleBar(e.Source as Control)
            || IsInteractive(e.Source as Control))
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    /// <summary>
    /// Determines whether a pointer source belongs to the custom title bar.
    /// </summary>
    internal bool IsWithinTitleBar(Control? control)
    {
        while (control is not null)
        {
            if (ReferenceEquals(control, TitleBar))
            {
                return true;
            }

            control = control.Parent as Control;
        }

        return false;
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

    /// <summary>
    /// Determines whether a pointer source belongs to a focusable or scrolling control.
    /// </summary>
    internal static bool IsInteractive(Control? control)
    {
        while (control is not null)
        {
            // Controls that can receive keyboard focus are interactive even when
            // their concrete type is a composite control (for example ColorPicker
            // or ToggleSwitch). Keep ScrollViewer as an explicit exception because
            // it is a pointer-interactive surface but is not normally focusable.
            if (control.Focusable || control is ScrollViewer)
            {
                return true;
            }

            control = control.Parent as Control;
        }

        return false;
    }

    private void PerformClose()
    {
        if (DataContext is MainWindowViewModel vm
            && vm.Settings.Editor.GetSavedSnapshot().CloseBehavior == Models.CloseBehaviors.Minimize)
        {
            if (systemTray is not null)
            {
                systemTray.HideWindow();
            }
            else
            {
                // No tray available — minimize to taskbar instead of calling Hide(),
                // which would make the window unrecoverable without a tray icon.
                WindowState = WindowState.Minimized;
            }

            return;
        }

        RequestShutdown();
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
