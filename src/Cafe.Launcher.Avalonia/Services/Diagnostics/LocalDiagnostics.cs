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
    /// Thread-safe static reference used by the static entry points to reach the
    /// logger registered once by the composition root (see
    /// <see cref="RegisterSharedLogger"/>). Uses Volatile.Read/Write to avoid
    /// stale reads without locking. Falls back to Debug.WriteLine when no logger
    /// has been registered (e.g. before DI init, or after disposal during shutdown).
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
    }

    /// <summary>
    /// Registers the process-wide logger backing the static <see cref="LogAsync"/>
    /// and <see cref="LogSync"/> entry points. The composition root calls this
    /// exactly once for the real pipeline; later constructions — including test
    /// doubles writing to temporary directories — cannot hijack the shared path.
    /// </summary>
    internal static void RegisterSharedLogger(UnifiedLogger logger) =>
        Volatile.Write(ref syncLogger, logger);

    internal string LogFilePath => logger.LogFilePath;

    public Task ErrorAsync(string title, Exception exception, CancellationToken cancellationToken = default)
        => ErrorAsync(title, message: null, exception, cancellationToken);

    /// <summary>
    /// Error carrying a caller-supplied context message alongside the exception, so callers that
    /// need to name the failed step keep <paramref name="title"/> free for the module tag the
    /// log conventions require.
    /// </summary>
    public Task ErrorAsync(
        string title,
        string? message,
        Exception exception,
        CancellationToken cancellationToken = default)
        => TryLogAsync(LogEntrySeverity.Error, title, message, exception, cancellationToken);

    public Task MessageAsync(string title, string message, CancellationToken cancellationToken = default)
        => TryLogAsync(LogEntrySeverity.Info, title, message, exception: null, cancellationToken);

    public Task VerboseAsync(
        string title,
        string? message = null,
        CancellationToken cancellationToken = default)
        => TryLogAsync(LogEntrySeverity.Verbose, title, message, exception: null, cancellationToken);

    public Task DebugAsync(
        string title,
        string? message = null,
        CancellationToken cancellationToken = default)
        => TryLogAsync(LogEntrySeverity.Debug, title, message, exception: null, cancellationToken);

    public Task WarningAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default)
        => TryLogAsync(LogEntrySeverity.Warn, title, message, exception: null, cancellationToken);

    public Task FatalAsync(
        string title,
        Exception exception,
        CancellationToken cancellationToken = default)
        => TryLogAsync(LogEntrySeverity.Fatal, title, message: null, exception, cancellationToken);

    /// <summary>
    /// The six facades' single implementation — the only 「永不抛」 wrapper on the instance side.
    /// <see cref="UnifiedLogger.LogAsync"/> already swallows its own failures; wrapping once here
    /// is what lets each call site stay unaware of that guarantee, and it is also where the
    /// <c>ConfigureAwait(false)</c> for these fire-and-forget writes lives.
    /// </summary>
    private async Task TryLogAsync(
        LogEntrySeverity severity,
        string title,
        string? message,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        try
        {
            await logger.LogAsync(severity, title, message: message, exception: exception,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort — diagnostic logging must never crash the app.
        }
    }

    /// <summary>
    /// Asynchronous log write for use inside async methods, including catch/finally blocks.
    /// Preferred over the blocking <see cref="LogSync(LogEntrySeverity,string,string?)"/>:
    /// sync-over-async stalls the UI thread whenever the Serilog async sink buffer is full
    /// or the log file is contended (e.g. held open by the log viewer).
    /// Falls back to Debug.WriteLine when no logger has been registered yet or after disposal.
    /// </summary>
    public static Task LogAsync(LogEntrySeverity severity, string title, string? message = null)
        => LogViaSharedAsync(severity, title, message);

    /// <summary>
    /// Synchronous log write for use in synchronous contexts (e.g. static methods).
    /// Writes through the DI-resolved UnifiedLogger when available, falling back to
    /// Debug.WriteLine if no DI logger has been registered yet or after disposal.
    /// Inside async methods prefer <see cref="LogAsync(LogEntrySeverity,string,string?)"/>
    /// to avoid blocking the calling thread on sink backpressure.
    /// </summary>
    public static void LogSync(string title, string message)
        => LogSync(LogEntrySeverity.Info, title, message);

    /// <summary>
    /// Synchronous log write with explicit severity for synchronous contexts.
    /// </summary>
    public static void LogSync(LogEntrySeverity severity, string title, string? message = null)
        => LogViaSharedSync(severity, title, message);

    /// <summary>
    /// 静态入口的共同实现。回退行按要求用严重级本身而不是硬编码文本：
    /// 收敛前 <see cref="LogSync(string,string)"/> 那条写的是 <c>[INFO]</c>，现在与另一条一致写成
    /// <c>[Info]</c>——两处格式不同本就是手工展开留下的痕迹。
    /// </summary>
    private static async Task LogViaSharedAsync(LogEntrySeverity severity, string title, string? message)
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

        DebugWriteFallback(severity, title, message);
    }

    /// <summary>
    /// 静态入口的同步实现。写入失败同样落到回退行（与收敛前一致：catch 之后并不 return）。
    /// </summary>
    private static void LogViaSharedSync(LogEntrySeverity severity, string title, string? message)
    {
        try
        {
            var logger = Volatile.Read(ref syncLogger);
            if (logger is not null)
            {
                logger.LogAsync(severity, title, message: message).GetAwaiter().GetResult();
                return;
            }
        }
        catch
        {
            // Best-effort — diagnostic logging must never crash the app.
        }

        DebugWriteFallback(severity, title, message);
    }

    private static void DebugWriteFallback(LogEntrySeverity severity, string title, string? message) =>
        System.Diagnostics.Debug.WriteLine($"{DateTimeOffset.Now:O} [{severity}] [{title}] {message}");
}
