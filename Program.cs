using Avalonia;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia;

sealed class Program
{
    private const string MutexName = @"Global\Cafe_Launcher_SI";
    private const string SignalName = @"Global\Cafe_Launcher_SI_Show";

    [STAThread]
    public static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            SignalFirstInstance();
            return;
        }

        SetupCrashLogging();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void SetupCrashLogging()
    {
        var crashLogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cafe Launcher");
        var crashLogPath = Path.Combine(crashLogDir, "crash.log");

        void WriteCrash(string source, Exception? ex)
        {
            try
            {
                Directory.CreateDirectory(crashLogDir);
                var sb = new StringBuilder();
                sb.AppendLine($"{DateTimeOffset.Now:O} [{source}]");
                if (ex != null)
                {
                    sb.AppendLine($"Exception: {ex.GetType().FullName}: {ex.Message}");
                    sb.AppendLine(ex.StackTrace ?? "(no stack trace)");
                    if (ex is AggregateException agg)
                    {
                        foreach (var inner in agg.Flatten().InnerExceptions)
                            sb.AppendLine($"  Inner: {inner.GetType().Name}: {inner.Message}");
                    }
                }
                sb.AppendLine();
                File.AppendAllText(crashLogPath, sb.ToString());
            }
            catch { /* Last resort — can't log the crash */ }
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrash("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    private static void SignalFirstInstance()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            using var signal = EventWaitHandle.OpenExisting(SignalName);
            signal.Set();
        }
        catch
        {
            // First instance may not have created the signal yet — ignore
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
