using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace GlassMaterialLab;

public partial class MainWindow : Window
{
    private const int FramesPerStatWindow = 30;

    private TimeSpan _lastFrameTimestamp;
    private bool _meterHasBaseline;
    private int _framesInWindow;
    private double _frameMsTotal;
    private double _frameMsMax;

    public MainWindow()
    {
        // GLASSLAB_SOLO=A/B/C/D/none：跳过主 XAML，程序化构造一个只含单一候选的
        // 窗口（纯色背景、无工具栏、无背景动画）。这是每个候选的最小隔离环境。
        var solo = Environment.GetEnvironmentVariable("GLASSLAB_SOLO");
        if (!string.IsNullOrEmpty(solo))
        {
            BuildSoloContent(solo[0]);
            return;
        }

        InitializeComponent();
        ModeHint.Text = Program.SoftwareRendering
            ? "软件渲染（--software）：检验玻璃回退路径"
            : "GPU 渲染 · Fluid 性能默认值已启用";
        // 候选 C：动画背景默认不强制逐帧重采，先直接暴露静态快照的残影问题；
        // 勾选卡片内的 ForcePermanentRender 后再对比其帧成本。
        JuffBackdrop.CacheRenderResult = false;
        JuffBackdrop.ListenToPropertyChanges = false;
        JuffBackdrop.ForcePermanentRender = false;

        // Spike 诊断开关：
        // GLASSLAB_DISABLE=ABC 隐藏对应卡片（控件仍 attach，不隔离初始化逻辑）；
        // GLASSLAB_KEEP=A / none 则把未列出的卡片从可视树移除，用于真正隔离候选
        // （崩溃定位表明 IsVisible=false 不足以隔离 attach 时的管线注册）；
        // GLASSLAB_STATIC=1 以静态背景启动；GLASSLAB_NO_METER=1 停用 FPS RAF 循环。
        var disabled = Environment.GetEnvironmentVariable("GLASSLAB_DISABLE") ?? string.Empty;
        CardA.IsVisible = !disabled.Contains('A');
        CardB.IsVisible = !disabled.Contains('B');
        CardC.IsVisible = !disabled.Contains('C');
        CardD.IsVisible = !disabled.Contains('D');

        var keep = Environment.GetEnvironmentVariable("GLASSLAB_KEEP");
        keep = string.IsNullOrEmpty(keep) ? "ABCD" : keep;
        if (keep.Equals("none", StringComparison.OrdinalIgnoreCase))
            keep = string.Empty;
        if (keep != "ABCD")
        {
            RemoveUnlessKept(keep, ('A', CardA), ('B', CardB), ('C', CardC), ('D', CardD));
        }
        // GLASSLAB_NO_BACKDROP / GLASSLAB_NO_TOOLBAR：把背景控件 / 工具栏移出树，
        // 用于把崩溃范围压缩到「玻璃卡片 attach」本身。
        if ((Environment.GetEnvironmentVariable("GLASSLAB_NO_BACKDROP") ?? "0") == "1"
            && Backdrop.Parent is Panel backdropParent)
        {
            backdropParent.Children.Remove(Backdrop);
        }
        if ((Environment.GetEnvironmentVariable("GLASSLAB_NO_TOOLBAR") ?? "0") == "1"
            && ToolbarHost.Parent is Panel toolbarParent)
        {
            toolbarParent.Children.Remove(ToolbarHost);
        }

        if ((Environment.GetEnvironmentVariable("GLASSLAB_STATIC") ?? "0") == "1")
        {
            Backdrop.IsAnimated = false;
            AnimatedToggle.IsChecked = false;
        }
        // 视频壁纸模拟：GLASSLAB_VIDEO_SIM=none|surrogate。
        // none      = 只有被排除出采样的「原生表面」→ 玻璃只能看到窗口底色（当前故障形态）；
        // surrogate = 原生表面之下再铺静态替身 → 玻璃应采到替身（视频壁纸的目标形态）。
        ApplyVideoSimulation(Environment.GetEnvironmentVariable("GLASSLAB_VIDEO_SIM"));

        // GLASSLAB_NO_METER=1 停用 FPS RAF 计量循环，用于隔离崩溃变量。
        if ((Environment.GetEnvironmentVariable("GLASSLAB_NO_METER") ?? "0") != "1")
            Opened += static (sender, _) => ((MainWindow)sender!).StartFpsMeter();

        // GLASSLAB_DUMP=1：应用内把窗口场景渲染为 PNG（绕开屏幕捕获/DPI/合成等
        // 外部变量），2s 与 6s 各转储一帧后退出，用于自动化验证场景内容与动画。
        if ((Environment.GetEnvironmentVariable("GLASSLAB_DUMP") ?? "0") == "1")
            Opened += static async (sender, _) => await ((MainWindow)sender!).DumpSequenceAsync();
    }

    private async Task DumpSequenceAsync()
    {
        try
        {
            await Task.Delay(2000);
            DumpSelf("glass-lab-dump-0.png");
            await Task.Delay(4000);
            DumpSelf("glass-lab-dump-1.png");
        }
        finally
        {
            Close();
        }
    }

    private void DumpSelf(string name)
    {
        var scale = VisualRoot is TopLevel topLevel ? topLevel.RenderScaling : 1.0;
        var size = Bounds.Size;
        var pixelSize = new PixelSize((int)(size.Width * scale), (int)(size.Height * scale));
        using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * scale, 96 * scale));
        bitmap.Render(this);
        var directory = Path.Combine(Path.GetTempPath(), "opencode");
        Directory.CreateDirectory(directory);
        using (var stream = File.Create(Path.Combine(directory, name)))
        {
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        }
        // 同步记录当时的真实帧节奏读数，作为屏幕呈现节奏的确定性证据。
        File.WriteAllText(Path.Combine(directory, name.Replace(".png", "-fps.txt")), FpsText.Text);
    }

    private void BuildSoloContent(char which)
    {
        Title = $"Glass Material Lab — SOLO {which}";
        Width = 900;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.Parse("#101014"));

        Control? surface = which switch
        {
            'A' => new ExperimentalAcrylicBorder
            {
                CornerRadius = new CornerRadius(16),
                Material = new ExperimentalAcrylicMaterial
                {
                    BackgroundSource = AcrylicBackgroundSource.Digger,
                    TintColor = Color.Parse("#E614141C"),
                    TintOpacity = 0.55,
                    MaterialOpacity = 0.35,
                },
            },
            'B' => new global::Fluid.Avalonia.Acrylic.AcrylicSurface
            {
                CornerRadius = new CornerRadius(16),
                BlurRadius = 18,
                Vibrancy = 1.15,
                TintColor = Color.Parse("#5914141C"),
                HighlightEnabled = false,
                ShadowEnabled = false,
            },
            'C' => new global::JuffMa.Controls.Acrylic.AcrylicBackdropControl
            {
                CornerRadius = new CornerRadius(16),
                Material = new ExperimentalAcrylicMaterial
                {
                    TintColor = Color.Parse("#E614141C"),
                    TintOpacity = 0.55,
                    MaterialOpacity = 0.35,
                },
                CacheRenderResult = false,
                ListenToPropertyChanges = false,
                ForcePermanentRender = false,
            },
            'D' => new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.Parse("#E6202020")),
                BorderBrush = new SolidColorBrush(Color.Parse("#1FFFFFFF")),
                BorderThickness = new Thickness(1),
            },
            _ => null,
        };

        if (surface is null)
        {
            Content = new TextBlock
            {
                Text = "GLASSLAB_SOLO=none — bare baseline",
                FontSize = 20,
                Foreground = Brushes.White,
                Margin = new Thickness(40),
            };
            return;
        }

        var host = new Grid { Margin = new Thickness(40) };
        host.Children.Add(surface);
        Content = host;
    }

    private static void RemoveUnlessKept(string keep, params (char Letter, Control Card)[] cards)
    {
        foreach (var (letter, card) in cards)
        {
            if (keep.Contains(letter) || card.Parent is not Panel panel)
                continue;
            panel.Children.Remove(card);
        }
    }

    private void ApplyVideoSimulation(string? mode)
    {
        VideoSimSurface.IsVisible = false;
        VideoSimSurrogate.IsVisible = false;
        Backdrop.IsVisible = true;
        switch (mode?.ToLowerInvariant())
        {
            case "none":
                VideoSimSurface.IsVisible = true;
                Backdrop.IsVisible = false;
                break;
            case "surrogate":
                VideoSimSurface.IsVisible = true;
                VideoSimSurrogate.IsVisible = true;
                Backdrop.IsVisible = false;
                break;
        }
    }

    private void OnAnimatedToggleClick(object? sender, RoutedEventArgs e)
        => Backdrop.IsAnimated = AnimatedToggle.IsChecked == true;

    private void OnCrossfadeClick(object? sender, RoutedEventArgs e)
        => Backdrop.TriggerCrossfade();

    private void OnJuffForceClick(object? sender, RoutedEventArgs e)
        => JuffBackdrop.ForcePermanentRender = JuffForce.IsChecked == true;

    private void StartFpsMeter()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;
        _meterHasBaseline = false;
        _framesInWindow = 0;
        _frameMsTotal = 0;
        _frameMsMax = 0;
        topLevel.RequestAnimationFrame(OnMeterFrame);
    }

    private void OnMeterFrame(TimeSpan timestamp)
    {
        // RAF 仅在产生帧时回调，帧间隔即当前场景的实际帧时间。
        if (_meterHasBaseline)
        {
            var deltaMs = (timestamp - _lastFrameTimestamp).TotalMilliseconds;
            if (deltaMs > 0)
            {
                _framesInWindow++;
                _frameMsTotal += deltaMs;
                if (deltaMs > _frameMsMax)
                    _frameMsMax = deltaMs;
            }
        }

        _lastFrameTimestamp = timestamp;
        _meterHasBaseline = true;

        // 每 ~30 帧汇总一次，避免文本刷新本身干扰读数。
        if (_framesInWindow >= FramesPerStatWindow)
        {
            var fps = 1000.0 * _framesInWindow / _frameMsTotal;
            var avg = _frameMsTotal / _framesInWindow;
            FpsText.Text = $"FPS {fps:0}｜平均 {avg:0.0} ms｜峰值 {_frameMsMax:0.0} ms";
            _framesInWindow = 0;
            _frameMsTotal = 0;
            _frameMsMax = 0;
        }

        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnMeterFrame);
    }
}
