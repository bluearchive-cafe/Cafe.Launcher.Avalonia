using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GlassMaterialLab;

/// <summary>
/// 模拟启动器壁纸层的动态背景：连续漂移的色斑 + 伪文字条 + 交叉淡化调色板。
/// 每帧 InvalidateVisual，帧率与真实视频壁纸同量级，用于检验三种玻璃候选在
/// 动态背景下的采样行为（残影 / 拖影 / 帧成本）。
///
/// 画刷全部在首次渲染前创建为常驻对象，仅颜色随调色板更新——动态表面不应
/// 按帧分配渲染资源（组合器需要为每个新画刷同步服务器端资源）。
/// </summary>
public class AnimatedBackdropControl : Control
{
    public static readonly StyledProperty<bool> IsAnimatedProperty =
        AvaloniaProperty.Register<AnimatedBackdropControl, bool>(nameof(IsAnimated), true);

    // 三套调色板：基底、三个漂移色斑、深色端点。切换壁纸 = 在两套之间做 ~900ms 的插值。
    private static readonly Color[][] Palettes =
    [
        [Color.Parse("#1B2A4A"), Color.Parse("#7C3AED"), Color.Parse("#0EA5E9"), Color.Parse("#EC4899"), Color.Parse("#0B1B33")],
        [Color.Parse("#3A2A1B"), Color.Parse("#D97706"), Color.Parse("#10B981"), Color.Parse("#F43F5E"), Color.Parse("#33250B")],
        [Color.Parse("#1B3A2A"), Color.Parse("#14B8A6"), Color.Parse("#84CC16"), Color.Parse("#8B5CF6"), Color.Parse("#0B3325")],
    ];

    private const double CrossfadeMilliseconds = 900;

    // 常驻渲染资源（见类注释）。
    private LinearGradientBrush _baseBrush = null!;
    private GradientStop _baseStart = null!;
    private GradientStop _baseEnd = null!;
    private readonly RadialGradientBrush[] _blobBrushes = new RadialGradientBrush[6];
    private readonly GradientStop[] _blobCenterStops = new GradientStop[6];
    private readonly SolidColorBrush[] _dotBrushes = new SolidColorBrush[3];
    private Pen _linePen = null!;
    private SolidColorBrush _barBrush = null!;
    private bool _resourcesCreated;
    private bool _paletteSyncStale = true;

    private double _phaseSeconds;
    private int _paletteIndex;
    private int _nextPaletteIndex = 1;
    private double _crossfade;
    private bool _crossfadeRunning;
    private bool _loopRunning;
    private TimeSpan _lastFrameTimestamp;

    static AnimatedBackdropControl()
    {
        AffectsRender<AnimatedBackdropControl>(IsAnimatedProperty);
    }

    public bool IsAnimated
    {
        get => GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    public void TriggerCrossfade()
    {
        _paletteIndex = _nextPaletteIndex;
        _nextPaletteIndex = (_nextPaletteIndex + 1) % Palettes.Length;
        _crossfade = 0;
        _crossfadeRunning = true;
        _lastFrameTimestamp = TimeSpan.Zero;
        InvalidateVisual();
        EnsureFrameLoop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsAnimatedProperty && change.NewValue is bool animated && animated)
            EnsureFrameLoop();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _lastFrameTimestamp = TimeSpan.Zero;
        EnsureFrameLoop();
    }

    private void EnsureFrameLoop()
    {
        if (_loopRunning)
            return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;
        _loopRunning = true;
        topLevel.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan timestamp)
    {
        _loopRunning = false;
        var delta = _lastFrameTimestamp == TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(16)
            : timestamp - _lastFrameTimestamp;
        _lastFrameTimestamp = timestamp;

        if (_crossfadeRunning)
        {
            _crossfade += delta.TotalMilliseconds / CrossfadeMilliseconds;
            if (_crossfade >= 1)
            {
                _crossfadeRunning = false;
                _crossfade = 0;
                _paletteIndex = _nextPaletteIndex;
            }
        }

        if (IsAnimated || _crossfadeRunning)
        {
            _phaseSeconds += delta.TotalSeconds;
            InvalidateVisual();
            EnsureFrameLoop();
        }
        else
        {
            _lastFrameTimestamp = TimeSpan.Zero;
        }
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        EnsureResources();
        // 调色板仅在变化时同步：稳态动画零渲染资源写入，交叉淡化期间逐帧插值。
        if (_crossfadeRunning)
        {
            SyncPaletteColors(CurrentPalette());
        }
        else if (_paletteSyncStale)
        {
            SyncPaletteColors(Palettes[_paletteIndex]);
            _paletteSyncStale = false;
        }

        var rect = new Rect(size);

        // 1. 基底渐变。
        context.FillRectangle(_baseBrush, rect);

        // 2. 六个漂移的辐射色斑 —— 模糊后的主要可见内容。
        for (var i = 0; i < 6; i++)
        {
            var cx = size.Width * (0.15 + 0.70 * (0.5 + 0.5 * Math.Sin(_phaseSeconds * (0.31 + i * 0.11) + i * 1.7)));
            var cy = size.Height * (0.12 + 0.76 * (0.5 + 0.5 * Math.Cos(_phaseSeconds * (0.23 + i * 0.09) + i * 2.3)));
            var radius = Math.Min(size.Width, size.Height) * 0.28;
            context.DrawEllipse(_blobBrushes[i], null, new Point(cx, cy), radius, radius);
        }

        // 3. 亮色小点阵 —— 检验小尺度内容在模糊后的残留感。
        for (var i = 0; i < 48; i++)
        {
            var x = size.Width * Pseudo(i, 1);
            var y = size.Height * (0.06 + 0.88 * Pseudo(i, 2))
                    + (IsAnimated ? Math.Sin(_phaseSeconds * 0.8 + i) * 6 : 0);
            var radius = 2 + 6 * Pseudo(i, 3);
            context.DrawEllipse(_dotBrushes[i % 3], null, new Point(x, y), radius, radius);
        }

        // 4. 斜向细线 —— 检验方向性内容模糊后的拖影。
        for (var i = 0; i < 10; i++)
        {
            var offset = size.Width * (i / 10.0)
                         + (IsAnimated ? Math.Sin(_phaseSeconds * 0.7 + i) * 40 : 0);
            context.DrawLine(_linePen, new Point(offset, 0), new Point(offset + size.Height * 0.35, size.Height));
        }

        // 5. 伪文字条 —— 对应真实 UI 里壁纸上的文字/贴图。
        for (var row = 0; row < 14; row++)
        {
            var width = size.Width * (0.18 + 0.30 * Pseudo(row, 5));
            var barRect = new Rect(size.Width * 0.06, size.Height * (0.12 + row * 0.055), width, 9);
            context.FillRectangle(_barBrush, barRect, 4);
        }
    }

    private void EnsureResources()
    {
        if (_resourcesCreated)
            return;

        _baseStart = new GradientStop(Colors.Black, 0);
        _baseEnd = new GradientStop(Colors.Black, 1);
        _baseBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { _baseStart, _baseEnd },
        };

        for (var i = 0; i < 6; i++)
        {
            var centerStop = new GradientStop(Colors.Black, 0);
            _blobCenterStops[i] = centerStop;
            _blobBrushes[i] = new RadialGradientBrush
            {
                GradientStops = { centerStop, new GradientStop(Colors.Transparent, 1) },
            };
        }

        for (var i = 0; i < 3; i++)
            _dotBrushes[i] = new SolidColorBrush(Colors.Black);

        _linePen = new Pen(new SolidColorBrush(WithAlpha(Colors.White, 60)), 1.2);
        _barBrush = new SolidColorBrush(WithAlpha(Colors.White, 44));
        _resourcesCreated = true;
    }

    private void SyncPaletteColors(Color[] palette)
    {
        _baseStart.Color = palette[0];
        _baseEnd.Color = palette[4];
        for (var i = 0; i < 6; i++)
            _blobCenterStops[i].Color = WithAlpha(palette[1 + (i % 3)], 170);
        for (var i = 0; i < 3; i++)
            _dotBrushes[i].Color = WithAlpha(palette[1 + i], 220);
    }

    private Color[] CurrentPalette()
    {
        var current = Palettes[_paletteIndex];
        if (!_crossfadeRunning && _crossfade == 0)
            return current;

        var next = Palettes[_nextPaletteIndex];
        var t = EaseInOut(_crossfade);
        var mixed = new Color[current.Length];
        for (var i = 0; i < current.Length; i++)
            mixed[i] = Lerp(current[i], next[i], t);
        return mixed;
    }

    private static double Pseudo(int index, int salt)
    {
        var value = Math.Sin(index * 12.9898 + salt * 78.233) * 43758.5453;
        return value - Math.Floor(value);
    }

    private static double EaseInOut(double t) => t * t * (3 - 2 * t);

    private static Color Lerp(Color a, Color b, double t) => new(
        (byte)(a.A + (b.A - a.A) * t),
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private static Color WithAlpha(Color color, byte alpha) => new(alpha, color.R, color.G, color.B);
}
