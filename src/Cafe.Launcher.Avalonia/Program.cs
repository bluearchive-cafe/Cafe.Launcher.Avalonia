using Avalonia;
using System;
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

    /// <summary>Set by <see cref="App"/> once the DI container is built.</summary>
    internal static ServiceProvider? ServiceProvider { get; set; }

    [STAThread]
    public static void Main(string[] args)
    {
        if (TryHandleCommandLine(args, Console.Out))
        {
            return;
        }

        // The single-instance handshake (signal endpoint before mutex probing,
        // forward-on-lose, bound endpoint handoff) is owned by the launch bridge.
        using var launchBridge = new CrossProcessLaunchBridge(LaunchGameSignalName, SignalName);
        if (!launchBridge.TryEnterSingleInstance(MutexName, args))
        {
            // A launcher is already running: forwarded --launch-game (when
            // requested) and exited instead of starting a duplicate process.
            return;
        }

        LaunchGameSignal = launchBridge.Signal;
        LaunchGameRequested = HasLaunchGameArgument(args);
        ShowHiddenSettings = HasShowHiddenSettingsArgument(args);

        // Create standalone diagnostics before DI is available. This instance
        // is shared with the DI container so there is a single Serilog pipeline
        // for the entire process.
        var crashLogger = new UnifiedLogger();
        PreDiLogger = crashLogger;
        FirstLaunch = DetectFirstLaunch();
        SetupCrashLogging(crashLogger);
        try
        {
            RunSession(
                crashLogger,
                () => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args));
        }
        catch (Exception exception)
        {
            LogCrash(crashLogger, "Main", exception);
            throw;
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

    private static bool DetectFirstLaunch()
    {
        var settingsPath = Path.Combine(
            LauncherUserDataDirectory.Root,
            GamePaths.LauncherSettingsFileName);
        return !File.Exists(settingsPath);
    }

    internal static void RunSession(UnifiedLogger logger, Action runApplication)
    {
        logger.WriteSessionStartAsync().GetAwaiter().GetResult();
        try
        {
            runApplication();
            logger.WriteSessionEndAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            LogCrash(logger, "Main", ex);
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

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
