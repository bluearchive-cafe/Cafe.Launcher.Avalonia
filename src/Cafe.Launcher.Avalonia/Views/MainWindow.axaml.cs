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
        _ = fadeOut.RunAsync(BackgroundCrossFade, cancellationToken);
    }

    private void OnOperationsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GameOperationsViewModel.PanelMode))
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(AnimateOperationSurfaceTransition);
            return;
        }

        AnimateOperationSurfaceTransition();
    }

    private void OnRootMotionPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsMotionEnabled))
        {
            SettleOperationSurface(OperationSurface);
        }
    }

    /// <summary>
    /// ADR-016 游戏操作表面连续转换：状态在单一任务容器内原地交换后，先把容器临时固定在
    /// 旧高度并测得新状态的自然高度，再以点到点曲线把高度连续过渡过去，同时用短暂透明度
    /// 下沉提示内容更替。新状态触发时立即取消前一段动画并以最新布局为准，不排队；降动效、
    /// 未附着或首帧无尺寸时直接落定。
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

        // 冻结旧视觉尺寸后让可见性绑定推过一轮布局，测得新状态的自然容器高度。
        // 三个状态各自携带 bottom-panel 的 MinHeight（≥132），布局后目标高度必有下界。
        surface.Height = double.NaN;
        surface.UpdateLayout();
        var targetHeight = surface.Bounds.Height;

        surface.Height = fromHeight;
        surface.UpdateLayout();

        operationSurfaceMotionCts?.Cancel();
        operationSurfaceMotionCts?.Dispose();
        var cts = new CancellationTokenSource();
        operationSurfaceMotionCts = cts;
        _ = RunOperationSurfaceTransitionAsync(surface, fromHeight, targetHeight, cts);
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
        finally
        {
            if (ReferenceEquals(operationSurfaceMotionCts, cancellation))
            {
                operationSurfaceMotionCts = null;
                cancellation.Dispose();
                surface.Opacity = 1;
                surface.Height = double.NaN;
            }
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

    /// <summary>透明度快速下沉再回弹，表达"同一对象的内容更替"，非独立对象的入场/退场。</summary>
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
                Setters = { new Setter { Property = Visual.OpacityProperty, Value = 1d } },
            },
            new KeyFrame
            {
                Cue = new Cue(25),
                Setters = { new Setter { Property = Visual.OpacityProperty, Value = OperationSurfaceDipOpacity } },
            },
            new KeyFrame
            {
                Cue = new Cue(100),
                Setters = { new Setter { Property = Visual.OpacityProperty, Value = 1d } },
            },
        },
    };

    private void SettleOperationSurface(Border? surface)
    {
        operationSurfaceMotionCts?.Cancel();
        operationSurfaceMotionCts?.Dispose();
        operationSurfaceMotionCts = null;
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

    private async void CopyErrorDetailsToClipboard(string details)
    {
        if (Clipboard is not null)
        {
            try
            {
                await Clipboard.SetTextAsync(details);
            }
            catch (Exception ex)
            {
                LocalDiagnostics.LogSync(
                    LogEntrySeverity.Warn,
                    "ClipboardCopyFailed",
                    $"Failed to copy error details to clipboard: {ex.Message}");
            }
        }
    }
}
