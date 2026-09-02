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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Views;

public partial class MainWindow : Window
{
    /// <summary>内容更替提示的透明度下沉值（对齐 FluentMotionLab 场景 6 的 Fluent 分支）。</summary>
    private const double OperationSurfaceDipOpacity = 0.58;

    /// <summary>
    /// 下沉恢复完成的进度点 = 快速档/标准档时长比（167ms/250ms），其后平尾保持到收尾。
    /// 由动效 token 派生，时长档调整时恢复点自动跟随。
    /// </summary>
    private static readonly double OperationSurfaceDipRecoveryCue =
        MotionTokens.FastDuration.TotalMilliseconds / MotionTokens.NormalDuration.TotalMilliseconds;

    private SystemTrayService? systemTray;
    private MainWindowViewModel? configuredViewModel;
    private CancellationTokenSource? operationSurfaceMotionCts;
    private readonly Func<string, Task<string?>> pickGameFolderAsync;
    private readonly Func<Task<string?>> pickBackgroundImageAsync;
    private readonly Func<Task<string?>> pickBackgroundFolderAsync;
    private readonly Func<string, Task<string?>> pickLogExportDirectoryAsync;
    private readonly Action<string> openDirectory;

    public MainWindow()
    {
        InitializeComponent();
        pickGameFolderAsync = PickGameFolderAsync;
        pickBackgroundImageAsync = PickBackgroundImageAsync;
        pickBackgroundFolderAsync = PickBackgroundFolderAsync;
        pickLogExportDirectoryAsync = PickLogExportDirectoryAsync;
        openDirectory = OpenDirectory;
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
        viewModel.Settings.PickGameFolderAsync = pickGameFolderAsync;
        viewModel.Settings.PickBackgroundImageAsync = pickBackgroundImageAsync;
        viewModel.Settings.PickBackgroundFolderAsync = pickBackgroundFolderAsync;
        viewModel.Background.PickBackgroundImageAsync = pickBackgroundImageAsync;
        viewModel.Background.PickBackgroundFolderAsync = pickBackgroundFolderAsync;
        viewModel.LogViewer.PickExportDirectoryAsync = pickLogExportDirectoryAsync;
        viewModel.LogViewer.OpenExportDirectory = openDirectory;
        viewModel.Debug.PickExportDirectoryAsync = pickLogExportDirectoryAsync;
        viewModel.Debug.OpenDirectory = openDirectory;
        viewModel.Operations.MinimizeRequested += MinimizeWindow;
        viewModel.WindowChrome.MinimizeRequested += MinimizeWindow;
        viewModel.WindowChrome.CloseRequested += PerformClose;
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
        Dispatcher.UIThread.Post(AnimateOperationSurfaceTransition);
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

        SettleOperationSurface(OperationSurface);
    }

    /// <summary>
    /// ADR-016 游戏操作表面连续转换：状态在单一任务容器内原地交换后，先把容器临时固定在
    /// 旧高度并测得新状态的自然高度，再以点到点曲线把高度连续过渡过去，同时用瞬时透明度
    /// 下沉与快速恢复提示内容更替。新状态触发时先取消在途动画再测量与起播，始终以最新
    /// 布局为准，不排队；降动效、未附着或首帧无尺寸时直接落定。
    /// </summary>
    private void AnimateOperationSurfaceTransition()
    {
        var surface = OperationSurface;
        var fromHeight = surface.Bounds.Height;
        if (configuredViewModel is not { IsMotionEnabled: true }
            || !surface.IsAttachedToVisualTree()
            || !double.IsFinite(fromHeight)
            || fromHeight <= 0)
        {
            SettleOperationSurface(surface);
            return;
        }

        // 最新状态立即接管：先取消在途动画、交还本地值，否则在途动画以 Animation 优先级
        // 持有 Height，下面的测量会被旧动画的当前帧高度污染（ADR-016：不排队，最新为准）。
        CancelOperationSurfaceMotion();

        // 入场窗期内发生状态切换时立即摘除一次性入场锚点：锚点的类动画持有 Opacity，会与
        // 下面写入的下沉值及恢复段互相覆盖；先摘除让本次转换的下沉/恢复段独占透明度，
        // 摘除同时把上升位移归零。锚点已摘除时此调用幂等无副作用。
        RetireOperationSurfaceEntranceAnchorNow();

        // 冻结旧视觉尺寸后让可见性绑定推过一轮布局，测得新状态的自然容器高度。
        // 三个状态各自携带 bottom-panel 的 MinHeight（≥132），布局后目标高度必有下界。
        surface.Height = double.NaN;
        surface.UpdateLayout();
        var targetHeight = surface.Bounds.Height;

        surface.Height = fromHeight;
        surface.UpdateLayout();

        // 瞬时写入下沉值，保证首个渲染帧即处于下沉态（对齐 FluentMotionLab 场景 6 的
        // Fluent 分支），随后的恢复段动画负责拉回。
        surface.Opacity = OperationSurfaceDipOpacity;

        var cts = new CancellationTokenSource();
        operationSurfaceMotionCts = cts;
        _ = RunOperationSurfaceTransitionAsync(surface, fromHeight, targetHeight, cts);
    }

    /// <summary>取消在途动画并把令牌源移出所有权槽；释放与几何结算由各任务收尾或落定路径负责。</summary>
    private void CancelOperationSurfaceMotion()
    {
        operationSurfaceMotionCts?.Cancel();
        operationSurfaceMotionCts = null;
    }

    private async Task RunOperationSurfaceTransitionAsync(
        Border surface,
        double fromHeight,
        double targetHeight,
        CancellationTokenSource cancellation)
    {
        try
        {
            var token = cancellation.Token;
            await Task.WhenAll(
                CreateOperationHeightAnimation(fromHeight, targetHeight).RunAsync(surface, token),
                CreateOperationDipAnimation().RunAsync(surface, token));
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
            if (ReferenceEquals(operationSurfaceMotionCts, cancellation))
            {
                operationSurfaceMotionCts = null;
                surface.Opacity = 1;
                surface.Height = double.NaN;
            }

            // 令牌源只由持有它的任务收尾释放，取消方仅负责 Cancel，避免与在途取消回调竞态。
            cancellation.Dispose();
        }
    }

    private static Animation CreateOperationHeightAnimation(double fromHeight, double targetHeight) => new()
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
    /// <see cref="AnimateOperationSurfaceTransition"/>），本动画以 167ms 进入曲线拉回全
    /// 不透明，随后保持到标准档收尾，使恢复段与高度形变同拍结算，避免动画提前释放后
    /// 回落为下沉值。对齐 FluentMotionLab 场景 6 的 Fluent 分支。
    /// </summary>
    private static Animation CreateOperationDipAnimation() => new()
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
                Setters = { new Setter { Property = Visual.OpacityProperty, Value = OperationSurfaceDipOpacity } },
            },
            new KeyFrame
            {
                // 167ms/250ms：快速档处即恢复完成，其后平尾保持。
                Cue = new Cue(OperationSurfaceDipRecoveryCue),
                Setters = { new Setter { Property = Visual.OpacityProperty, Value = 1d } },
            },
            new KeyFrame
            {
                Cue = new Cue(1),
                Setters = { new Setter { Property = Visual.OpacityProperty, Value = 1d } },
            },
        },
    };

    private void SettleOperationSurface(Border? surface)
    {
        CancelOperationSurfaceMotion();
        if (surface is not null)
        {
            surface.Opacity = 1;
            surface.Height = double.NaN;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        UnconfigureViewModel();
        base.OnClosed(e);
    }

    private void UnconfigureViewModel()
    {
        if (configuredViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.Background.PreviousWallpaperFadingOut -= FadeOutPreviousWallpaper;
        viewModel.Operations.MinimizeRequested -= MinimizeWindow;
        viewModel.WindowChrome.MinimizeRequested -= MinimizeWindow;
        viewModel.WindowChrome.CloseRequested -= PerformClose;
        viewModel.WindowChrome.RestoreRequested -= ShowWindow;
        viewModel.Dialogs.ErrorCopyDetailsRequested -= CopyErrorDetailsToClipboard;
        viewModel.Operations.PropertyChanged -= OnOperationsPropertyChanged;
        viewModel.PropertyChanged -= OnRootMotionPreferenceChanged;
        SettleOperationSurface(OperationSurface);
        viewModel.RemoteContent.SetBannerPointerOver(false);
        viewModel.RemoteContent.SetBannerFocusWithin(false);

        if (viewModel.Settings.PickGameFolderAsync == pickGameFolderAsync)
        {
            viewModel.Settings.PickGameFolderAsync = null;
        }

        if (viewModel.Settings.PickBackgroundImageAsync == pickBackgroundImageAsync)
        {
            viewModel.Settings.PickBackgroundImageAsync = null;
        }

        if (viewModel.Settings.PickBackgroundFolderAsync == pickBackgroundFolderAsync)
        {
            viewModel.Settings.PickBackgroundFolderAsync = null;
        }

        if (viewModel.Background.PickBackgroundImageAsync == pickBackgroundImageAsync)
        {
            viewModel.Background.PickBackgroundImageAsync = null;
        }

        if (viewModel.Background.PickBackgroundFolderAsync == pickBackgroundFolderAsync)
        {
            viewModel.Background.PickBackgroundFolderAsync = null;
        }

        if (viewModel.LogViewer.PickExportDirectoryAsync == pickLogExportDirectoryAsync)
        {
            viewModel.LogViewer.PickExportDirectoryAsync = null;
        }

        if (viewModel.LogViewer.OpenExportDirectory == openDirectory)
        {
            viewModel.LogViewer.OpenExportDirectory = null;
        }

        if (viewModel.Debug.PickExportDirectoryAsync == pickLogExportDirectoryAsync)
        {
            viewModel.Debug.PickExportDirectoryAsync = null;
        }

        if (viewModel.Debug.OpenDirectory == openDirectory)
        {
            viewModel.Debug.OpenDirectory = null;
        }

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

    private async Task<string?> PickGameFolderAsync(string currentPath)
    {
        if (!StorageProvider.CanPickFolder)
        {
            return null;
        }

        var startLocation = string.IsNullOrWhiteSpace(currentPath)
            ? null
            : await StorageProvider.TryGetFolderFromPathAsync(currentPath);

        var pickerTitle = (DataContext as MainWindowViewModel)?.Shell.GameFolderPickerTitle ?? "";
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = pickerTitle,
            AllowMultiple = false,
            SuggestedStartLocation = startLocation
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickBackgroundImageAsync()
    {
        if (!StorageProvider.CanOpen)
        {
            return null;
        }

        var imagePickerTitle = (DataContext as MainWindowViewModel)?.Background.BackgroundImagePickerTitle ?? "";
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = imagePickerTitle,
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp" },
                }
            }
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickBackgroundFolderAsync()
    {
        if (!StorageProvider.CanPickFolder)
        {
            return null;
        }

        var folderPickerTitle = (DataContext as MainWindowViewModel)?.Background.BackgroundFolderPickerTitle ?? "";
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = folderPickerTitle,
            AllowMultiple = false
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickLogExportDirectoryAsync(string defaultPath)
    {
        Directory.CreateDirectory(defaultPath);
        if (!StorageProvider.CanPickFolder)
        {
            return defaultPath;
        }

        var startLocation = await StorageProvider.TryGetFolderFromPathAsync(defaultPath);
        var pickerTitle = (DataContext as MainWindowViewModel)?.Shell.I18n["logExportFolderPickerTitle"] ?? "";
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = pickerTitle,
            AllowMultiple = false,
            SuggestedStartLocation = startLocation
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private static void OpenDirectory(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
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

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.TryShutdown();
            return;
        }

        Close();
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
                await LocalDiagnostics.LogAsync(
                    LogEntrySeverity.Warn,
                    "ClipboardCopyFailed",
                    $"Failed to copy error details to clipboard: {ex.Message}");
            }
        }
    }
}
