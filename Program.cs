using Avalonia;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia;

sealed class Program
{
    private const string MutexName = @"Local\Cafe_Launcher_SI";
    private const string SignalName = @"Local\Cafe_Launcher_SI_Show";

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
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            WriteCrashLog("Main", exception);
            throw;
        }
    }

    private static void SetupCrashLogging()
    {
        void WriteCrash(string source, Exception? ex)
        {
            WriteCrashLog(source, ex);
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrash("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    private static void WriteCrashLog(string source, Exception? exception)
    {
        try
        {
            var crashLogDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Cafe Launcher");
            var crashLogPath = Path.Combine(crashLogDir, "crash.log");
            Directory.CreateDirectory(crashLogDir);
            var builder = new StringBuilder();
            builder.AppendLine(FormattableString.Invariant($"{DateTimeOffset.Now:O} [{source}]"));
            if (exception is not null)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"Exception: {exception.GetType().FullName}: {exception.Message}"));
                builder.AppendLine(exception.StackTrace ?? "(no stack trace)");
                if (exception is AggregateException aggregateException)
                {
                    foreach (var inner in aggregateException.Flatten().InnerExceptions)
                    {
                        builder.AppendLine(FormattableString.Invariant(
                            $"  Inner: {inner.GetType().Name}: {inner.Message}"));
                    }
                }
            }

            builder.AppendLine();
            File.AppendAllText(crashLogPath, builder.ToString());
        }
        catch
        {
            // Last resort — the process is already failing and no additional sink exists.
        }
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
