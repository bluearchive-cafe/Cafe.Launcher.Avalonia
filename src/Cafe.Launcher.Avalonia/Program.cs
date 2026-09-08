using Avalonia;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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

    /// <summary>
    /// Signal the first instance raises its launch-game listener on, so a second
    /// <c>--launch-game</c> invocation forwards the request instead of starting
    /// a duplicate process.
    /// </summary>
    internal const string LaunchGameSignalName = @"Local\Cafe_Launcher_SI_LaunchGame";

    /// <summary>CLI argument that launches the game through the full launcher pipeline.</summary>
    internal const string LaunchGameArgument = "--launch-game";

    /// <summary>
    /// CLI argument that exposes conditional settings controls for diagnostics.
    /// This only affects the current process and is never persisted.
    /// </summary>
    internal const string ShowHiddenSettingsArgument = "--show-hidden-settings";

    /// <summary>Internal CLI argument used by the isolated crash-report application.</summary>
    internal const string CrashReportArgument = "--crash-report";

    /// <summary>
    /// True when this process itself was started with <see cref="LaunchGameArgument"/>
    /// and won the single-instance mutex: the app auto-launches the game after its
    /// initial state refresh. Managed by <see cref="Main"/>.
    /// </summary>
    internal static bool LaunchGameRequested { get; private set; }

    /// <summary>
    /// True when the launcher was started with <see cref="ShowHiddenSettingsArgument"/>.
    /// </summary>
    internal static bool ShowHiddenSettings { get; private set; }

    /// <summary>
    /// The cross-process launch-game signal endpoint owned by the first instance.
    /// Set by <see cref="Main"/> after the single-instance mutex is won and bound,
    /// then polled by the <see cref="App"/> launch-game listener. Disposed with <see cref="Main"/>.
    /// </summary>
    internal static CrossProcessLaunchSignal? LaunchGameSignal { get; private set; }

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

    /// <summary>The process-wide fatal crash coordinator shared with application DI.</summary>
    internal static FatalCrashService? PreDiFatalCrashService { get; private set; }

    /// <summary>
    /// Set by <see cref="App"/> when the in-process crash window is shown. The desktop
    /// lifetime reports its own (zero) exit code for that shutdown, so the terminal
    /// result is applied by <see cref="ResolveSessionExitCode"/> after it returns.
    /// </summary>
    internal static bool FatalCrashExitRequested { get; set; }

    /// <summary>Snapshot opened by <see cref="CrashReportApp"/> in isolated reporter mode.</summary>
    internal static string? CrashReportPath { get; private set; }

    /// <summary>Set by <see cref="App"/> once the DI container is built.</summary>
    internal static ServiceProvider? ServiceProvider { get; set; }

    [STAThread]
    public static void Main(string[] args)
    {
        if (TryGetCrashReportPath(args, out var crashReportPath))
        {
            CrashReportPath = crashReportPath;
            Environment.ExitCode = RunCrashReporter(
                () => BuildCrashReportApp().StartWithClassicDesktopLifetime(args));
            return;
        }

        if (TryHandleCommandLine(args, Console.Out))
        {
            return;
        }

        var reportStore = new CrashReportStore();
        UnifiedLogger crashLogger;
        try
        {
            crashLogger = new UnifiedLogger();
        }
        catch (Exception exception)
        {
            // Diagnostics initialization itself failed. The snapshot store still has a
            // temp-directory fallback and can start the isolated reporter without DI.
            var emergencyCrashService = new FatalCrashService(
                logger: null,
                reportStore,
                new CrashReporterLauncher());
            emergencyCrashService.HandleUnhandledCrash("DiagnosticsInitialization", exception);
            Environment.ExitCode = 1;
            return;
        }

        PreDiLogger = crashLogger;
        var fatalCrashService = new FatalCrashService(
            crashLogger,
            reportStore,
            new CrashReporterLauncher());
        PreDiFatalCrashService = fatalCrashService;
        SetupCrashHandling(crashLogger, fatalCrashService);

        var sessionFailureCaptured = false;
        try
        {
            reportStore.CleanupOldReports();

            // The isolated reporter bypasses this handshake above. Normal launches still
            // forward to the first instance instead of starting a duplicate process.
            using var launchBridge = new CrossProcessLaunchBridge(LaunchGameSignalName, SignalName);
            if (!launchBridge.TryEnterSingleInstance(MutexName, args))
            {
                return;
            }

            LaunchGameSignal = launchBridge.Signal;
            LaunchGameRequested = HasLaunchGameArgument(args);
            ShowHiddenSettings = HasShowHiddenSettingsArgument(args);
            FirstLaunch = DetectFirstLaunch();

            RunSession(
                crashLogger,
                () => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args),
                exception =>
                {
                    sessionFailureCaptured = true;
                    fatalCrashService.HandleUnhandledCrash("Main", exception);
                });

            Environment.ExitCode = ResolveSessionExitCode();
        }
        catch (Exception exception)
        {
            if (!sessionFailureCaptured)
            {
                fatalCrashService.HandleUnhandledCrash("Main", exception);
            }

            Environment.ExitCode = 1;
        }
    }

    internal static bool TryHandleCommandLine(string[] args, TextWriter output)
    {
        if (args.Length != 1 ||
            !string.Equals(args[0], "--version", StringComparison.Ordinal))
        {
            return false;
        }

        var informationalVersion = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        output.WriteLine(informationalVersion ?? typeof(Program).Assembly.GetName().Version?.ToString());
        return true;
    }

    /// <summary>Matches <see cref="LaunchGameArgument"/> exactly; no prefixes, no casing tricks.</summary>
    internal static bool HasLaunchGameArgument(string[] args) =>
        args.Any(argument => string.Equals(argument, LaunchGameArgument, StringComparison.Ordinal));

    /// <summary>Matches <see cref="ShowHiddenSettingsArgument"/> exactly.</summary>
    internal static bool HasShowHiddenSettingsArgument(string[] args) =>
        args.Any(argument => string.Equals(argument, ShowHiddenSettingsArgument, StringComparison.Ordinal));

    internal static bool TryGetCrashReportPath(string[] args, out string? path)
    {
        path = null;
        if (args.Length != 2
            || !string.Equals(args[0], CrashReportArgument, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(args[1]))
        {
            return false;
        }

        try
        {
            path = Path.GetFullPath(args[1]);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Runs the isolated reporter and returns the process exit code. The reporter is a
    /// terminal diagnostic surface, so it must never report success: the desktop lifetime
    /// resets <see cref="Environment.ExitCode"/> to its own (zero) value when the window
    /// closes cleanly, which would otherwise turn a fatal launch into a success code.
    /// </summary>
    internal static int RunCrashReporter(Func<int> startApplication)
    {
        ArgumentNullException.ThrowIfNull(startApplication);
        try
        {
            startApplication();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Crash reporter failed: {exception}");
        }

        return 1;
    }

    /// <summary>
    /// Resolves the process exit code once the desktop lifetime has returned. A fatal
    /// crash is a terminal diagnostic outcome, so it must never surface as success —
    /// the lifetime's own exit code is zero for that shutdown.
    /// </summary>
    internal static int ResolveSessionExitCode() => FatalCrashExitRequested ? 1 : 0;

    private static bool DetectFirstLaunch()
    {
        var settingsPath = Path.Combine(
            LauncherUserDataDirectory.Root,
            GamePaths.LauncherSettingsFileName);
        return !File.Exists(settingsPath);
    }

    internal static void RunSession(
        UnifiedLogger logger,
        Action runApplication,
        Action<Exception>? handleCrash = null)
    {
        logger.WriteSessionStartAsync().GetAwaiter().GetResult();
        try
        {
            runApplication();
            logger.WriteSessionEndAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            if (handleCrash is null)
            {
                LogCrash(logger, "Main", ex);
            }
            else
            {
                handleCrash(ex);
            }

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

    private static void SetupCrashHandling(UnifiedLogger logger, FatalCrashService fatalCrashService)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var exception = e.ExceptionObject as Exception
                            ?? new InvalidOperationException($"Unhandled object: {e.ExceptionObject}");
            fatalCrashService.HandleUnhandledCrash("AppDomain.UnhandledException", exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try
            {
                logger.LogAsync(
                        LogEntrySeverity.Warn,
                        "TaskScheduler.UnobservedTaskException",
                        exception: e.Exception,
                        cancellationToken: CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // Unobserved task diagnostics are best-effort and are not fatal.
            }

            e.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            fatalCrashService.HandleUnhandledCrash("Dispatcher.UnhandledException", e.Exception);
            e.Handled = false;
        };
    }

    private static void LogCrash(UnifiedLogger logger, string source, Exception? exception)
    {
        try
        {
            logger.LogAsync(LogEntrySeverity.Fatal, source,
                exception: exception,
                cancellationToken: CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch
        {
            // Last resort — the process is already failing and no additional sink exists.
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    internal static AppBuilder BuildCrashReportApp()
        => AppBuilder.Configure<CrashReportApp>()
            .UsePlatformDetect()
            .LogToTrace();
}
