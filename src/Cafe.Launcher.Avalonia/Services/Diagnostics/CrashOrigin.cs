namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>
/// Closed set of boundaries that may raise an unrecoverable failure. Snapshots and logs
/// store the mapped label instead of caller-supplied text, so a future call site cannot
/// widen what leaves the machine.
/// </summary>
public enum CrashOrigin
{
    /// <summary>A failure escaped the launcher session entry point.</summary>
    Main,

    /// <summary>The process domain reported an unhandled exception.</summary>
    AppDomainUnhandledException,

    /// <summary>The Avalonia dispatcher reported an unhandled exception.</summary>
    DispatcherUnhandledException,

    /// <summary>Diagnostics initialization failed before the UI existed.</summary>
    DiagnosticsInitialization,

    /// <summary>The debug panel simulated an unrecoverable failure.</summary>
    DebugSimulation
}

internal static class CrashOriginExtensions
{
    /// <summary>Stable snapshot and log label for one crash origin.</summary>
    public static string ToSourceLabel(this CrashOrigin origin) => origin switch
    {
        CrashOrigin.Main => "Main",
        CrashOrigin.AppDomainUnhandledException => "AppDomain.UnhandledException",
        CrashOrigin.DispatcherUnhandledException => "Dispatcher.UnhandledException",
        CrashOrigin.DiagnosticsInitialization => "DiagnosticsInitialization",
        CrashOrigin.DebugSimulation => "DebugPanel",
        _ => "Unknown"
    };
}
