using Avalonia;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Composition;
using Cafe.Launcher.Avalonia.Services;
using Microsoft.Extensions.DependencyInjection;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia;

sealed class Program
{
    private const string MutexName = @"Local\Cafe_Launcher_SI";
    private const string SignalName = @"Local\Cafe_Launcher_SI_Show";

    internal static bool PreviousSessionCrashed { get; private set; }

    /// <summary>
    /// True when the launcher settings file is missing at process startup.
    /// Used by <see cref="App"/> to show the first-launch setup wizard before normal refresh.
    /// </summary>
    internal static bool FirstLaunch { get; private set; }

    /// <summary>
    /// The pre-DI <see cref="UnifiedLogger"/> created before the DI container
    /// exists. <see cref="App"/> consumes this so a single logger instance
    /// serves both crash handling and application logging.
    /// </summary>
    internal static UnifiedLogger? PreDiLogger { get; private set; }

    /// <summary>
    /// Set by <see cref="App"/> once the DI container is built so that
    /// <see cref="RunSession"/> can dispose it after the session-end entry
    /// has been written.
    /// </summary>
    internal static ServiceProvider? ServiceProvider { get; set; }

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
        // is shared with the DI container so there is a single Serilog pipeline
        // for the entire process.
        var crashLogger = new UnifiedLogger();
        PreDiLogger = crashLogger;
        FirstLaunch = DetectFirstLaunch();
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

    private static bool DetectFirstLaunch()
    {
        var settingsPath = Path.Combine(
            LauncherUserDataDirectory.Root,
            GamePaths.LauncherSettingsFileName);
        return !File.Exists(settingsPath);
    }

    internal static void RunSession(CrashRecoveryService crashRecovery, Action runApplication)
    {
        PreviousSessionCrashed = crashRecovery.BeginSessionAsync().GetAwaiter().GetResult();
        try
        {
            runApplication();
            // Normal exit — finalise the session cleanly.
            crashRecovery.CompleteSessionAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // Log the crash before the logger is disposed, and intentionally
            // skip CompleteSessionAsync so the crash marker survives for the
            // next launch to detect. Main's catch block serves as a fallback
            // for pre-DI-container crashes (ServiceProvider is null).
            LogCrash(PreDiLogger!, "Main", ex);
            throw;
        }
        finally
        {
            // Dispose the DI container last, after all log writes complete.
            // MS.DI does not dispose externally-provided singleton instances,
            // so the shared UnifiedLogger must be disposed explicitly to flush
            // the async sink buffer.
            ServiceProvider?.Dispose();
            PreDiLogger?.Dispose();
        }
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
            .LogToTrace();
}
