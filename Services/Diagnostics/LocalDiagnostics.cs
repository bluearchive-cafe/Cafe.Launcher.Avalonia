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

    public LocalDiagnostics() : this(new UnifiedLogger(Path.Combine(
        Path.GetTempPath(),
        "Cafe.Launcher.Avalonia.Tests",
        Environment.ProcessId.ToString(CultureInfo.InvariantCulture))))
    {
    }

    public LocalDiagnostics(UnifiedLogger logger)
    {
        this.logger = logger;
        StaticLoggerHolder.Instance = logger;
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

    /// <summary>
    /// Synchronous log write for use in synchronous contexts (e.g. static methods).
    /// </summary>
    public static void LogSync(string title, string message)
    {
        try
        {
            StaticLoggerHolder.Instance.LogAsync(LogEntrySeverity.Info, title, message: message)
                .GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort — diagnostic logging must never crash the app.
        }
    }
}

/// <summary>Bridge for static LogSync to reach the DI-resolved <see cref="UnifiedLogger"/> singleton.</summary>
internal static class StaticLoggerHolder
{
    public static UnifiedLogger Instance { get; set; } = null!;
}
