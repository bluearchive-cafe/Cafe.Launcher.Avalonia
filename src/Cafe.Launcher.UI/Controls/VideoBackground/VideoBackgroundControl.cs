using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Cafe.Launcher.UI.Controls.VideoBackground;

/// <summary>可合成的视频背景。GL 资源仅在 OpenGlControlBase 的 GL 生命周期内创建和释放。</summary>
internal sealed class VideoBackgroundControl : OpenGlControlBase
{
    internal static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, string?>(nameof(Source));
    internal static readonly StyledProperty<bool> PlaybackPausedProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, bool>(nameof(PlaybackPaused), true);
    internal static readonly StyledProperty<int> VolumeProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, int>(nameof(Volume));
    internal static readonly StyledProperty<Stretch> StretchProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, Stretch>(nameof(Stretch), Stretch.UniformToFill);
    internal static readonly StyledProperty<Color> FillColorProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, Color>(nameof(FillColor), Colors.Black);

    private readonly string libraryPath;
    private readonly Action requestFrame;
    private readonly DispatcherTimer eventTimer;
    private MpvNative? native;
    private MpvNative.GetProc? getProc;
    private MpvNative.UpdateCallback? update;
    private nint player;
    private nint renderer;
    private nint framebuffer;
    private nint renderParameters;
    private string? loadedSource;
    private int pendingFrame;
    private int generation;
    private bool failed;
    private bool firstFrame;
    private bool videoSizeKnown;

    internal VideoBackgroundControl(string libraryPath)
    {
        this.libraryPath = libraryPath;
        IsHitTestVisible = false;
        Focusable = false;
        requestFrame = RequestFrameOnUiThread;
        eventTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        eventTimer.Tick += ReadPlayerEvents;
    }

    internal string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    internal bool PlaybackPaused { get => GetValue(PlaybackPausedProperty); set => SetValue(PlaybackPausedProperty, value); }
    internal int Volume { get => GetValue(VolumeProperty); set => SetValue(VolumeProperty, value); }
    internal Stretch Stretch { get => GetValue(StretchProperty); set => SetValue(StretchProperty, value); }
    internal Color FillColor { get => GetValue(FillColorProperty); set => SetValue(FillColorProperty, value); }

    /// <summary>首帧进入合成路径后通知窗口隐藏被完全遮挡的静态壁纸。</summary>
    internal event Action? FirstFrameReady;
    /// <summary>在 UI 调度中报告当前文件的播放失败；调用方切回静态背景。</summary>
    internal event Action<string, Exception>? PlaybackFailed;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            failed = false;
            firstFrame = false;
            videoSizeKnown = false;
            generation++;
            RequestNextFrameRendering();
        }
        else if (change.Property == PlaybackPausedProperty || change.Property == VolumeProperty
                 || change.Property == StretchProperty || change.Property == FillColorProperty)
        {
            try { ApplyPlaybackProperties(); }
            catch (Exception exception) { ReportFailure(exception); }
        }
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        // The current source is initialized in OnOpenGlRender, so rapid draft edits
        // cannot initialize a source that has already been superseded.
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (failed || string.IsNullOrWhiteSpace(Source)) return;
        try
        {
            if (native is null) InitializePlayer(gl);
            if (native is null) return;
            if (loadedSource != Source)
            {
                loadedSource = Source;
                native.Load(player, loadedSource);
            }
            var size = GetFramebufferSize(Bounds.Size, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
            Marshal.StructureToPtr(new MpvNative.Framebuffer(fb, size.Width, size.Height), framebuffer, false);
            MpvNative.Check(native.Draw(renderer, renderParameters), "render");
            if (!firstFrame && videoSizeKnown)
            {
                firstFrame = true;
                var currentGeneration = generation;
                Dispatcher.UIThread.Post(() =>
                {
                    if (currentGeneration == generation && renderer != 0 && !failed) FirstFrameReady?.Invoke();
                });
            }
        }
        catch (Exception exception)
        {
            ReleasePlayer();
            ReportFailure(exception);
        }
    }

    private void InitializePlayer(GlInterface gl)
    {
        native = new MpvNative(libraryPath);
        player = native.NewPlayer();
        if (player == 0) throw new InvalidOperationException("mpv_create failed.");
        ConfigurePlayer(native, player, PlaybackPaused);
        MpvNative.Check(native.InitializePlayer(player), "initialize");
        getProc = (_, name) => gl.GetProcAddress(name);
        var init = native.Allocate(new MpvNative.GlInit(Marshal.GetFunctionPointerForDelegate(getProc)));
        MpvNative.Check(native.NewRender(out renderer, player,
            native.Parameters(new(1, native.String("opengl")), new(2, init))), "render_context_create");
        framebuffer = native.Allocate(new MpvNative.Framebuffer(0, 1, 1));
        renderParameters = native.Parameters(new(3, framebuffer), new(4, native.Allocate(1)), new(12, native.Allocate(0)));
        update = _ => QueueFrame();
        native.SetUpdateCallback(renderer, Marshal.GetFunctionPointerForDelegate(update), 0);
        ApplyPlaybackProperties();
        eventTimer.Start();
    }

    /// <summary>Configures the background player's playback and media access before initialization.</summary>
    internal static void ConfigurePlayer(MpvNative native, nint player, bool playbackPaused)
    {
        foreach (var option in new[]
        {
            ("config", "no"), ("load-scripts", "no"), ("vo", "libmpv"),
            ("hwdec", "auto"), ("loop-file", "inf"), ("volume", "0"),
            ("pause", playbackPaused ? "yes" : "no"), ("sub-auto", "no"),
            // Backgrounds are local media, never playlists or network clients.
            // Block mpv references and FFmpeg nested protocols independently.
            ("access-references", "no"), ("autoload-files", "no"),
            ("demuxer-lavf-o", "protocol_whitelist=[file]"),
            ("stream-lavf-o", "protocol_whitelist=[file]"),
        })
        {
            MpvNative.Check(native.SetOption(player, option.Item1, option.Item2), option.Item1);
        }
    }

    private void ApplyPlaybackProperties()
    {
        if (native is null || player == 0) return;
        MpvNative.Check(native.SetProperty(player, "pause", PlaybackPaused ? "yes" : "no"), "pause");
        MpvNative.Check(native.SetProperty(player, "volume", Math.Clamp(Volume, 0, 100).ToString(CultureInfo.InvariantCulture)), "volume");
        MpvNative.Check(native.SetProperty(player, "keepaspect", Stretch == Stretch.Fill ? "no" : "yes"), "keepaspect");
        MpvNative.Check(native.SetProperty(player, "panscan", Stretch == Stretch.UniformToFill ? "1" : "0"), "panscan");
        MpvNative.Check(native.SetProperty(player, "background-color", FormatFillColor(FillColor)), "background-color");
    }

    private void QueueFrame()
    {
        if (Interlocked.Exchange(ref pendingFrame, 1) == 0) Dispatcher.UIThread.Post(requestFrame, DispatcherPriority.Render);
    }

    private void RequestFrameOnUiThread()
    {
        Interlocked.Exchange(ref pendingFrame, 0);
        if (renderer != 0 && !failed && this.IsAttachedToVisualTree()) RequestNextFrameRendering();
    }

    private void ReadPlayerEvents(object? sender, EventArgs e)
    {
        if (native is null || player == 0) return;
        if (!videoSizeKnown && int.TryParse(native.Read(player, "video-params/w"), out var width) && width > 0)
        {
            videoSizeKnown = true;
            RequestNextFrameRendering();
        }
        for (var i = 0; i < 32; i++)
        {
            var playerEvent = Marshal.PtrToStructure<MpvNative.PlayerEvent>(native.WaitEvent(player, 0));
            if (playerEvent.Id == 0) break;
            // MPV_EVENT_END_FILE: an error reason is followed by the libmpv error code.
            if (playerEvent.Id == 7 && playerEvent.Data != 0 && Marshal.ReadInt32(playerEvent.Data) == 4)
            {
                ReportFailure(new InvalidOperationException($"libmpv playback failed: {Marshal.ReadInt32(playerEvent.Data, sizeof(int))}"));
                break;
            }
        }
    }

    private void ReportFailure(Exception exception)
    {
        if (failed || Source is not { } source) return;
        failed = true;
        var currentGeneration = generation;
        Dispatcher.UIThread.Post(() =>
        {
            if (currentGeneration == generation && Source == source) PlaybackFailed?.Invoke(source, exception);
        });
    }

    protected override void OnOpenGlDeinit(GlInterface gl) => ReleasePlayer();

    private void ReleasePlayer()
    {
        generation++;
        eventTimer.Stop();
        if (native is null) return;
        if (renderer != 0)
        {
            native.SetUpdateCallback(renderer, 0, 0);
            native.DestroyRender(renderer);
            renderer = 0;
        }
        if (player != 0)
        {
            native.DestroyPlayer(player);
            player = 0;
        }
        native.Dispose();
        native = null;
        loadedSource = null;
        GC.KeepAlive(getProc);
        GC.KeepAlive(update);
        getProc = null;
        update = null;
    }

    /// <summary>与 Avalonia OpenGlControlBase 的物理 FBO 截断规则一致。</summary>
    internal static PixelSize GetFramebufferSize(Size size, double scaling) =>
        new(Math.Max(1, (int)(size.Width * scaling)), Math.Max(1, (int)(size.Height * scaling)));

    /// <summary>Avalonia may stringify named colors as "Black"; mpv requires numeric color syntax.</summary>
    internal static string FormatFillColor(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
