#if DEBUG
using MpvPrototypeNative = Cafe.Launcher.UI.Controls.VideoBackground.MpvNative;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;

namespace Cafe.Launcher.UI.Controls.VideoBackgroundPrototype;

/// <summary>显式启用的 Debug 视频背景实验；原生资源只在 GL 生命周期内创建/释放。</summary>
[ExcludeFromCodeCoverage(Justification = "Opt-in Debug-only GPU experiment; headless software rendering cannot execute this render path.")]
internal sealed class VideoBackgroundPrototypeControl : OpenGlControlBase
{
    private readonly string libraryPath;
    private readonly string videoPath;
    private readonly StreamWriter samples;
    private readonly Process process = Process.GetCurrentProcess();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DispatcherTimer timer;
    private MpvPrototypeNative? native;
    private MpvPrototypeNative.GetProc? getProc;
    private MpvPrototypeNative.UpdateCallback? update;
    private nint player;
    private nint renderContext;
    private nint framebuffer;
    private nint renderParameters;
    private int redrawPending;
    private bool stopped;
    private bool paused;
    private readonly string? probeMode = Environment.GetEnvironmentVariable("CAFE_VIDEO_PROTOTYPE_PROBE_MODE");
    private readonly int glFinishEveryFrames =
        int.TryParse(Environment.GetEnvironmentVariable("CAFE_VIDEO_PROTOTYPE_GLFINISH_FRAMES"), out var finishCadence)
            ? Math.Max(0, finishCadence) : 0;
    private readonly bool skipDraw = Environment.GetEnvironmentVariable("CAFE_VIDEO_PROTOTYPE_SKIP_DRAW") == "1";
    private readonly bool continuousRedraw =
        Environment.GetEnvironmentVariable("CAFE_VIDEO_PROTOTYPE_CONTINUOUS_REDRAW") == "1";
    private Finish? finish;
    private bool recycleRequested;
    private bool mediaReleased;

    internal bool ReusesSurface => probeMode == "ReuseSurface";
    internal bool UsesProduction => probeMode == "Production";
    internal Func<(int Players, int Renderers, int Allocations)>? SampleNativeResources { get; set; }
    internal Func<long>? SampleFrameCount { get; set; }
    internal Func<string, string>? SamplePlayerProperty { get; set; }

    /// <summary>对照实验只重建 libmpv，不反复分离 Avalonia 的 GL 承载面。</summary>
    internal void SetMediaReleased(bool released)
    {
        mediaReleased = released;
        if (released) SetPaused(true);
        RequestNextFrameRendering();
    }
    private long frames;
    private long previousFrames;
    private double drawMilliseconds;
    private double maxDrawMilliseconds;
    private double previousSeconds;
    private TimeSpan previousCpu;
    private PixelSize target;
    private double scale;
    private string failure = "";
    private long previousAllocated = GC.GetTotalAllocatedBytes();
    private long updateCallbacks;
    private long frameRequests;
    private long previousUpdates;
    private long previousRequests;
    private string? lastCommand;
    private readonly string commandPath;
    private readonly string acknowledgementPath;
    private readonly string rendererInfoPath;
    private readonly Action dispatchFrameRequest;

    internal VideoBackgroundPrototypeControl(string libraryPath, string videoPath, string outputPath)
    {
        this.libraryPath = libraryPath;
        this.videoPath = videoPath;
        dispatchFrameRequest = DispatchFrameRequest;
        commandPath = Path.ChangeExtension(outputPath, ".command");
        acknowledgementPath = Path.ChangeExtension(outputPath, ".ack");
        rendererInfoPath = Path.ChangeExtension(outputPath, ".gl.txt");
        IsHitTestVisible = false;
        Focusable = false;
        samples = new StreamWriter(outputPath) { AutoFlush = true };
        samples.WriteLine("seconds,phase,frames_per_second,render_ms_mean,render_ms_max,width_px,height_px,scale,paused,hwdec,cpu_percent_machine,private_mb,position,dropped_frames,error,gc_heap_mb,gc_committed_mb,gc_total_memory_mb,allocated_mb_per_second,gen0,gen1,gen2,updates_per_second,requests_per_second,player_live,renderer_live,arena_blocks,process_handles");
        previousCpu = process.TotalProcessorTime;
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += Sample;
        timer.Start();
    }

    internal string Phase { get; set; } = "startup";

    /// <summary>原型测量脚本的白名单命令；只在当前原型的数据目录内收发。</summary>
    internal event Func<string, Task>? DiagnosticCommand;

    protected override void OnOpenGlInit(GlInterface gl)
    {
        if (!File.Exists(rendererInfoPath)) WriteRendererInfo(gl);
        if (probeMode is "Blank" or "Pattern") return;
        try
        {
            native = new MpvPrototypeNative(libraryPath);
            player = native.NewPlayer();
            if (player == 0) throw new InvalidOperationException("mpv_create failed");
            foreach (var option in new[]
            {
                ("config", "no"), ("load-scripts", "no"), ("vo", "libmpv"),
                // AUD-PERF-009 isolation: compare the hardware-copy path against software decode.
                ("hwdec", Environment.GetEnvironmentVariable("CAFE_VIDEO_PROTOTYPE_HWDEC") ?? "auto"),
                ("audio", "no"), ("loop-file", "inf"),
                ("panscan", "1"), ("pause", paused ? "yes" : "no"),
            })
            {
                MpvPrototypeNative.Check(native.SetOption(player, option.Item1, option.Item2), option.Item1);
            }
            foreach (var option in (Environment.GetEnvironmentVariable("CAFE_VIDEO_PROTOTYPE_MPV_OPTIONS") ?? "")
                     .Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = option.IndexOf('=', StringComparison.Ordinal);
                if (separator <= 0) continue;
                MpvPrototypeNative.Check(
                    native.SetOption(player, option[..separator], option[(separator + 1)..]), option[..separator]);
            }
            MpvPrototypeNative.Check(native.InitializePlayer(player), "initialize");
            getProc = (_, name) => gl.GetProcAddress(name);
            update = _ => QueueFrame();
            framebuffer = native.Allocate(new MpvPrototypeNative.Framebuffer(0, 1, 1));
            CreateRenderContext(gl);
            native.Load(player, videoPath);
            QueueFrame();
        }
        catch (Exception exception)
        {
            failure = exception.Message.Replace(',', ';');
            ReleaseNative();
        }
    }

    private void CreateRenderContext(GlInterface gl)
    {
        var init = native!.Allocate(new MpvPrototypeNative.GlInit(Marshal.GetFunctionPointerForDelegate(getProc!)));
        var parameters = native.Parameters(new(1, native.String("opengl")), new(2, init));
        MpvPrototypeNative.Check(native.NewRender(out renderContext, player, parameters), "render_context_create");
        renderParameters = native.Parameters(new(3, framebuffer), new(4, native.Allocate(1)), new(12, native.Allocate(0)));
        native.SetUpdateCallback(renderContext, Marshal.GetFunctionPointerForDelegate(update!), 0);
    }

    /// <summary>Renews the GL render context while the player keeps its position.</summary>
    internal void RecycleRenderer()
    {
        if (native is null || player == 0) return;
        recycleRequested = true;
        RequestNextFrameRendering();
    }

    /// <summary>Records the GL implementation once so driver-specific findings stay attributable.</summary>
    private void WriteRendererInfo(GlInterface gl)
    {
        var getString = Marshal.GetDelegateForFunctionPointer<GetString>(gl.GetProcAddress("glGetString"));
        string Read(uint name)
        {
            var pointer = getString(name);
            return pointer == 0 ? "unavailable" : Marshal.PtrToStringUTF8(pointer) ?? "unavailable";
        }
        File.WriteAllText(rendererInfoPath,
            $"vendor={Read(0x1F00)}\nrenderer={Read(0x1F01)}\nversion={Read(0x1F02)}\n");
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint GetString(uint name);

    private void QueueFrame()
    {
        Interlocked.Increment(ref updateCallbacks);
        if (Volatile.Read(ref stopped) || Interlocked.Exchange(ref redrawPending, 1) != 0) return;
        Dispatcher.UIThread.Post(dispatchFrameRequest, DispatcherPriority.Render);
    }

    private void DispatchFrameRequest()
    {
        Interlocked.Exchange(ref redrawPending, 0);
        if (!stopped && renderContext != 0)
        {
            frameRequests++;
            RequestNextFrameRendering();
        }
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (ReusesSurface && mediaReleased)
        {
            ReleaseNative();
            gl.ClearColor(0, 0, 0, 0);
            gl.Clear(GlConsts.GL_COLOR_BUFFER_BIT);
            return;
        }
        if (ReusesSurface && native is null) OnOpenGlInit(gl);
        if (probeMode == "Blank")
        {
            gl.ClearColor(0, 0, 0, 1);
            gl.Clear(GlConsts.GL_COLOR_BUFFER_BIT);
            RequestNextFrameRendering();
            return;
        }
        if (probeMode == "Pattern")
        {
            // AUD-PERF-009 isolation: changing pixels every frame with no mpv,
            // mirroring what the compositor sees while a video plays.
            var phase = frames++ / 255.0;
            gl.ClearColor((float)Math.Abs(phase % 2 - 1), 0.25f, 0.5f, 1);
            gl.Clear(GlConsts.GL_COLOR_BUFFER_BIT);
            RequestNextFrameRendering();
            return;
        }
        if (recycleRequested && native is not null && player != 0)
        {
            // mpv_render_context_free must run with the GL context current, so the
            // recycle happens here rather than in the command handler.
            recycleRequested = false;
            if (renderContext != 0)
            {
                native.SetUpdateCallback(renderContext, 0, 0);
                native.DestroyRender(renderContext);
                renderContext = 0;
            }
            try { CreateRenderContext(gl); }
            catch (Exception exception)
            {
                failure = exception.Message.Replace(',', ';');
                ReleaseNative();
                return;
            }
        }
        if (renderContext == 0 || native is null) return;
        scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        target = GetFramebufferSize(Bounds.Size, scale);
        if (target.Width <= 0 || target.Height <= 0) return;
        Marshal.StructureToPtr(new MpvPrototypeNative.Framebuffer(fb, target.Width, target.Height), framebuffer, false);
        if (skipDraw)
        {
            // AUD-PERF-009 isolation: decode and request frames, but keep mpv out of
            // the framebuffer. Growth that survives this run belongs to composition.
            gl.ClearColor(0, 0, 0, 1);
            gl.Clear(GlConsts.GL_COLOR_BUFFER_BIT);
            return;
        }
        var start = Stopwatch.GetTimestamp();
        var result = native.Draw(renderContext, renderParameters);
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        drawMilliseconds += elapsed;
        maxDrawMilliseconds = Math.Max(maxDrawMilliseconds, elapsed);
        frames++;
        if (result < 0) failure = $"render: {result}";
        else if (glFinishEveryFrames > 0 && frames % glFinishEveryFrames == 0)
        {
            // AUD-PERF-009 isolation: a per-frame glFinish rules out driver work
            // that only completes at an explicit sync point.
            finish ??= Marshal.GetDelegateForFunctionPointer<Finish>(gl.GetProcAddress("glFinish"));
            finish();
        }
        if (continuousRedraw) RequestNextFrameRendering();
    }

    /// <summary>必须与 Avalonia OpenGlControlBase 实际分配的 framebuffer 尺寸一致。</summary>
    internal static PixelSize GetFramebufferSize(Size size, double scaling) =>
        new(Math.Max(1, (int)(size.Width * scaling)), Math.Max(1, (int)(size.Height * scaling)));

    internal void SetPaused(bool value)
    {
        paused = value;
        if (native is not null && player != 0)
        {
            MpvPrototypeNative.Check(native.SetProperty(player, "pause", value ? "yes" : "no"), "pause");
        }
    }

    private async void Sample(object? sender, EventArgs e)
    {
        process.Refresh();
        var seconds = clock.Elapsed.TotalSeconds;
        var interval = seconds - previousSeconds;
        var cpu = process.TotalProcessorTime;
        var gc = GC.GetGCMemoryInfo();
        var allocated = GC.GetTotalAllocatedBytes();
        var updates = Interlocked.Read(ref updateCallbacks);
        var totalFrames = SampleFrameCount?.Invoke() ?? frames;
        var count = totalFrames - previousFrames;
        var resources = SampleNativeResources?.Invoke() ?? (player == 0 ? 0 : 1, renderContext == 0 ? 0 : 1, native?.AllocationCount ?? 0);
        var property = (string name) => SamplePlayerProperty?.Invoke(name)
            ?? (native is not null && player != 0 ? native.Read(player, name) : "unavailable");
        samples.WriteLine(FormattableString.Invariant(
            $"{seconds:F3},{Phase},{count / interval:F2},{(count == 0 ? 0 : drawMilliseconds / count):F3},{maxDrawMilliseconds:F3},{target.Width},{target.Height},{scale:F2},{paused},{property("hwdec-current")},{(cpu - previousCpu).TotalSeconds / interval / Environment.ProcessorCount * 100:F3},{process.PrivateMemorySize64 / 1048576.0:F1},{property("time-pos")},{property("decoder-frame-drop-count")},{failure},{gc.HeapSizeBytes / 1048576.0:F2},{gc.TotalCommittedBytes / 1048576.0:F2},{GC.GetTotalMemory(false) / 1048576.0:F2},{(allocated - previousAllocated) / 1048576.0 / interval:F2},{GC.CollectionCount(0)},{GC.CollectionCount(1)},{GC.CollectionCount(2)},{(updates - previousUpdates) / interval:F2},{(frameRequests - previousRequests) / interval:F2},{resources.Item1},{resources.Item2},{resources.Item3},{process.HandleCount}"));
        previousAllocated = allocated;
        previousUpdates = updates;
        previousRequests = frameRequests;
        previousSeconds = seconds;
        previousCpu = cpu;
        previousFrames = totalFrames;
        drawMilliseconds = maxDrawMilliseconds = 0;
        if (File.Exists(commandPath))
        {
            var command = File.ReadAllText(commandPath).Trim();
            if (command != lastCommand)
            {
                lastCommand = command;
                if (DiagnosticCommand is { } handle)
                    await handle(command.Contains('|', StringComparison.Ordinal) ? command[(command.IndexOf('|', StringComparison.Ordinal) + 1)..] : command);
                if (!stopped) File.WriteAllText(acknowledgementPath, command);
            }
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        ReleaseNative();
        if (probeMode == "Finish")
            Marshal.GetDelegateForFunctionPointer<Finish>(gl.GetProcAddress("glFinish"))();
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void Finish();

    private void ReleaseNative()
    {
        if (native is null) return;
        if (renderContext != 0)
        {
            native.SetUpdateCallback(renderContext, 0, 0);
            native.DestroyRender(renderContext);
            renderContext = 0;
        }
        if (player != 0)
        {
            native.DestroyPlayer(player);
            player = 0;
        }
        native.Dispose();
        native = null;
        // Delegates must outlive all native callbacks, including renderer teardown.
        GC.KeepAlive(getProc);
        GC.KeepAlive(update);
        GC.KeepAlive(finish);
        getProc = null;
        update = null;
        finish = null;
    }

    /// <summary>窗口关闭时停止采样；GL 资源由控件卸载后的 OnOpenGlDeinit 释放。</summary>
    internal void StopSampling()
    {
        if (stopped) return;
        stopped = true;
        timer.Stop();
        timer.Tick -= Sample;
        samples.Dispose();
        process.Dispose();
    }
}
#endif
