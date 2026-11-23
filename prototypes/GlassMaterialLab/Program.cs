using System;
using System.Linq;
using Avalonia;
using Fluid.Avalonia.Acrylic;

namespace GlassMaterialLab;

internal static class Program
{
    /// <summary>Software rendering requested via --software; surfaced by the window header hint.</summary>
    public static bool SoftwareRendering { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        SoftwareRendering = args.Contains("--software", StringComparer.OrdinalIgnoreCase);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
        if (SoftwareRendering)
            builder.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Software],
            });
        // Fluid.Avalonia.Acrylic 推荐的组合器/脏矩形调优（对三种候选同屏生效）。
        // GLASSLAB_NO_PERF_DEFAULTS=1 用于隔离该变量（稳定性定位用）。
        if (Environment.GetEnvironmentVariable("GLASSLAB_NO_PERF_DEFAULTS") != "1")
            builder = builder.UseAcrylicPerformanceDefaults();
        return builder;
    }
}
