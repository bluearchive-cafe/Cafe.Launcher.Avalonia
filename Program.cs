using Avalonia;
using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia;

sealed class Program
{
    private const string MutexName = @"Local\Cafe_Launcher_SI";
    private const string SignalName = @"Local\Cafe_Launcher_SI_Show";

    internal static bool PreviousSessionCrashed { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            SignalFirstInstance();
            return;
        }

        // Create standalone diagnostics before DI is available. This instance
        // owns process-session tracking and the unhandled-exception handlers.
        var crashLogger = new UnifiedLogger();
        var crashRecovery = new CrashRecoveryService(crashLogger);
        SetupCrashLogging(crashLogger);
        try
        {
            RunSession(
                crashRecovery,
                () => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args));
        }
        catch (Exception exception)
        {
            LogCrash(crashLogger, "Main", exception);
            throw;
        }
    }

    internal static void RunSession(CrashRecoveryService crashRecovery, Action runApplication)
    {
        PreviousSessionCrashed = crashRecovery.BeginSessionAsync().GetAwaiter().GetResult();
        runApplication();
        crashRecovery.CompleteSessionAsync().GetAwaiter().GetResult();
    }

    private static void SetupCrashLogging(UnifiedLogger logger)
    {
        void WriteCrash(string source, Exception? ex) => LogCrash(logger, source, ex);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            WriteCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrash("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    private static void LogCrash(UnifiedLogger logger, string source, Exception? exception)
    {
        try
        {
            logger.LogAsync(LogEntrySeverity.Error, source,
                exception: exception,
                cancellationToken: CancellationToken.None)
                .GetAwaiter().GetResult();
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
