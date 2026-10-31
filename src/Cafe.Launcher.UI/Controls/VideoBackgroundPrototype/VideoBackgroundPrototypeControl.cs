#if DEBUG
using MpvPrototypeNative = Cafe.Launcher.UI.Controls.VideoBackground.MpvNative;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
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
    private readonly Action dispatchFrameRequest;

    internal VideoBackgroundPrototypeControl(string libraryPath, string videoPath, string outputPath)
    {
        this.libraryPath = libraryPath;
        this.videoPath = videoPath;
        dispatchFrameRequest = DispatchFrameRequest;
        commandPath = Path.ChangeExtension(outputPath, ".command");
        acknowledgementPath = Path.ChangeExtension(outputPath, ".ack");
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
    internal event Action<string>? DiagnosticCommand;

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            native = new MpvPrototypeNative(libraryPath);
            player = native.NewPlayer();
            if (player == 0) throw new InvalidOperationException("mpv_create failed");
            foreach (var option in new[]
            {
                ("config", "no"), ("load-scripts", "no"), ("vo", "libmpv"),
                ("hwdec", "auto"), ("audio", "no"), ("loop-file", "inf"),
                ("panscan", "1"), ("pause", paused ? "yes" : "no"),
            })
            {
                MpvPrototypeNative.Check(native.SetOption(player, option.Item1, option.Item2), option.Item1);
            }
            MpvPrototypeNative.Check(native.InitializePlayer(player), "initialize");
            getProc = (_, name) => gl.GetProcAddress(name);
            var init = native.Allocate(new MpvPrototypeNative.GlInit(Marshal.GetFunctionPointerForDelegate(getProc)));
            var parameters = native.Parameters(new(1, native.String("opengl")), new(2, init));
            MpvPrototypeNative.Check(native.NewRender(out renderContext, player, parameters), "render_context_create");
            framebuffer = native.Allocate(new MpvPrototypeNative.Framebuffer(0, 1, 1));
            renderParameters = native.Parameters(new(3, framebuffer), new(4, native.Allocate(1)), new(12, native.Allocate(0)));
            update = _ => QueueFrame();
            native.SetUpdateCallback(renderContext, Marshal.GetFunctionPointerForDelegate(update), 0);
            native.Load(player, videoPath);
            QueueFrame();
        }
        catch (Exception exception)
        {
            failure = exception.Message.Replace(',', ';');
            ReleaseNative();
        }
    }

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
        if (renderContext == 0 || native is null) return;
        scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        target = GetFramebufferSize(Bounds.Size, scale);
        if (target.Width <= 0 || target.Height <= 0) return;
        Marshal.StructureToPtr(new MpvPrototypeNative.Framebuffer(fb, target.Width, target.Height), framebuffer, false);
        var start = Stopwatch.GetTimestamp();
        var result = native.Draw(renderContext, renderParameters);
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        drawMilliseconds += elapsed;
        maxDrawMilliseconds = Math.Max(maxDrawMilliseconds, elapsed);
        frames++;
        if (result < 0) failure = $"render: {result}";
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

    private void Sample(object? sender, EventArgs e)
    {
        process.Refresh();
        var seconds = clock.Elapsed.TotalSeconds;
        var interval = seconds - previousSeconds;
        var cpu = process.TotalProcessorTime;
        var gc = GC.GetGCMemoryInfo();
        var allocated = GC.GetTotalAllocatedBytes();
        var updates = Interlocked.Read(ref updateCallbacks);
        var count = frames - previousFrames;
        var property = (string name) => native is not null && player != 0 ? native.Read(player, name) : "unavailable";
        samples.WriteLine(FormattableString.Invariant(
            $"{seconds:F3},{Phase},{count / interval:F2},{(count == 0 ? 0 : drawMilliseconds / count):F3},{maxDrawMilliseconds:F3},{target.Width},{target.Height},{scale:F2},{paused},{property("hwdec-current")},{(cpu - previousCpu).TotalSeconds / interval / Environment.ProcessorCount * 100:F3},{process.PrivateMemorySize64 / 1048576.0:F1},{property("time-pos")},{property("decoder-frame-drop-count")},{failure},{gc.HeapSizeBytes / 1048576.0:F2},{gc.TotalCommittedBytes / 1048576.0:F2},{GC.GetTotalMemory(false) / 1048576.0:F2},{(allocated - previousAllocated) / 1048576.0 / interval:F2},{GC.CollectionCount(0)},{GC.CollectionCount(1)},{GC.CollectionCount(2)},{(updates - previousUpdates) / interval:F2},{(frameRequests - previousRequests) / interval:F2},{(player == 0 ? 0 : 1)},{(renderContext == 0 ? 0 : 1)},{native?.AllocationCount ?? 0},{process.HandleCount}"));
        previousAllocated = allocated;
        previousUpdates = updates;
        previousRequests = frameRequests;
        previousSeconds = seconds;
        previousCpu = cpu;
        previousFrames = frames;
        drawMilliseconds = maxDrawMilliseconds = 0;
        if (File.Exists(commandPath))
        {
            var command = File.ReadAllText(commandPath).Trim();
            if (command != lastCommand)
            {
                lastCommand = command;
                DiagnosticCommand?.Invoke(command.Contains('|', StringComparison.Ordinal) ? command[(command.IndexOf('|', StringComparison.Ordinal) + 1)..] : command);
                File.WriteAllText(acknowledgementPath, command);
            }
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl) => ReleaseNative();

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
        getProc = null;
        update = null;
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
