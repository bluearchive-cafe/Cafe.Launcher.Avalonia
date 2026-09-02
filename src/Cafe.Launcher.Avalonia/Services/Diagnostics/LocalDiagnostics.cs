using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>
/// Thin compatibility wrapper around <see cref="UnifiedLogger"/>.
/// All public signatures are preserved so existing call sites compile unchanged.
/// </summary>
public sealed class LocalDiagnostics
{
    private readonly UnifiedLogger logger;

    /// <summary>
    /// Thread-safe static reference used by <see cref="LogSync"/> to reach the DI-resolved logger.
    /// Uses Volatile.Read/Write to avoid stale reads without locking.
    /// Falls back to Debug.WriteLine when no logger has been registered (e.g. before DI init,
    /// or after the logger has been disposed during shutdown).
    /// </summary>
    private static UnifiedLogger? syncLogger;

    /// <summary>
    /// Creates a <see cref="LocalDiagnostics"/> writing to a test-only temporary directory.
    /// Only for use by test projects (see <c>InternalsVisibleTo</c>) and legacy call sites.
    /// Production code should always go through the DI container which provides the real
    /// <see cref="UnifiedLogger"/> path via <see cref="LocalDiagnostics(UnifiedLogger)"/>.
    /// </summary>
    internal LocalDiagnostics() : this(new UnifiedLogger(Path.Combine(
        Path.GetTempPath(),
        "Cafe.Launcher.Avalonia.Tests",
        Environment.ProcessId.ToString(CultureInfo.InvariantCulture))))
    {
    }

    public LocalDiagnostics(UnifiedLogger logger)
    {
        this.logger = logger;
        Volatile.Write(ref syncLogger, logger);
    }

    internal string LogFilePath => logger.LogFilePath;

    public async Task ErrorAsync(string title, Exception exception, CancellationToken cancellationToken = default)
    {
        try
        {
            await logger.LogAsync(LogEntrySeverity.Error, title, exception: exception,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort — diagnostic logging must never crash the app.
        }
    }

    public async Task MessageAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        try
        {
            await logger.LogAsync(LogEntrySeverity.Info, title, message: message,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort — diagnostic logging must never crash the app.
        }
    }

    public async Task VerboseAsync(
        string title,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await logger.LogAsync(LogEntrySeverity.Verbose, title, message: message,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort.
        }
    }

    public async Task DebugAsync(
        string title,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await logger.LogAsync(LogEntrySeverity.Debug, title, message: message,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort.
        }
    }

    public async Task WarningAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await logger.LogAsync(LogEntrySeverity.Warn, title, message: message,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort.
        }
    }

    public async Task FatalAsync(
        string title,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await logger.LogAsync(LogEntrySeverity.Fatal, title, exception: exception,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort.
        }
    }

    /// <summary>
    /// Asynchronous log write for use inside async methods, including catch/finally blocks.
    /// Preferred over the blocking <see cref="LogSync(LogEntrySeverity,string,string?)"/>:
    /// sync-over-async stalls the UI thread whenever the Serilog async sink buffer is full
    /// or the log file is contended (e.g. held open by the log viewer).
    /// Falls back to Debug.WriteLine when no logger has been registered yet or after disposal.
    /// </summary>
    public static async Task LogAsync(LogEntrySeverity severity, string title, string? message = null)
    {
        try
        {
            var logger = Volatile.Read(ref syncLogger);
            if (logger is not null)
            {
                await logger.LogAsync(severity, title, message: message).ConfigureAwait(false);
                return;
            }
        }
        catch
        {
            // Best-effort — diagnostic logging must never crash the app.
        }

        System.Diagnostics.Debug.WriteLine(
            $"{DateTimeOffset.Now:O} [{severity}] [{title}] {message}");
    }

    /// <summary>
    /// Synchronous log write for use in synchronous contexts (e.g. static methods).
    /// Writes through the DI-resolved UnifiedLogger when available, falling back to
    /// Debug.WriteLine if no DI logger has been registered yet or after disposal.
    /// Inside async methods prefer <see cref="LogAsync(LogEntrySeverity,string,string?)"/>
    /// to avoid blocking the calling thread on sink backpressure.
    /// </summary>
    public static void LogSync(string title, string message)
    {
        try
        {
            var logger = Volatile.Read(ref syncLogger);
            if (logger is not null)
            {
                logger.LogAsync(LogEntrySeverity.Info, title, message: message)
                    .GetAwaiter().GetResult();
                return;
            }
        }
        catch
        {
            // Best-effort — diagnostic logging must never crash the app.
        }

        System.Diagnostics.Debug.WriteLine(
            $"{DateTimeOffset.Now:O} [INFO] [{title}] {message}");
    }

    /// <summary>
    /// Synchronous log write with explicit severity for synchronous contexts.
    /// </summary>
    public static void LogSync(LogEntrySeverity severity, string title, string? message = null)
    {
        try
        {
            var logger = Volatile.Read(ref syncLogger);
            if (logger is not null)
            {
                logger.LogAsync(severity, title, message: message)
                    .GetAwaiter().GetResult();
                return;
            }
        }
        catch
        {
            // Best-effort.
        }

        System.Diagnostics.Debug.WriteLine(
            $"{DateTimeOffset.Now:O} [{severity}] [{title}] {message}");
    }
}
