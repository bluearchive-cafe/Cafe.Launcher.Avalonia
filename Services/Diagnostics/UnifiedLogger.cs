using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>
/// Centralised logging engine. All error, warning, and informational messages
/// flow through this singleton and are persisted to a single rotating log file
/// (backed by Serilog with an async sink wrapper).
/// </summary>
public sealed class UnifiedLogger : IDisposable
{
    private readonly Logger serilogLogger;
    private readonly LoggingLevelSwitch levelSwitch;
    private readonly string logFilePath;
    private readonly AsyncLogBufferMonitor asyncLogBufferMonitor;
    private bool disposed;

    public UnifiedLogger() : this(null) { }

    internal UnifiedLogger(string? logDirectory)
    {
        var dir = logDirectory ?? Path.Combine(
            LauncherUserDataDirectory.Root);
        logFilePath = Path.Combine(dir, "unified.log");

        // Verbose in Debug builds so developers see everything; Information in
        // Release so production logs stay lean. The switch can be adjusted at
        // runtime via SetMinimumLevel().
        levelSwitch = new LoggingLevelSwitch(
#if DEBUG
            LogEventLevel.Verbose
#else
            LogEventLevel.Information
#endif
        );

        asyncLogBufferMonitor = new AsyncLogBufferMonitor();

        serilogLogger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("AppVersion", BuildInfo.LauncherVersion)
            .Enrich.WithProperty("CommitSha", BuildInfo.CommitSha)
            .WriteTo.Async(a => a.File(
                logFilePath,
                formatProvider: CultureInfo.InvariantCulture,
                fileSizeLimitBytes: 5L * 1024 * 1024,     // 5 MB
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 4,                  // current + 3 rotated
                rollingInterval: RollingInterval.Infinite,
                shared: true,                               // allow log viewer to read while writing
                outputTemplate: "{Timestamp:O} [{Level:u3}] [{LogTitle}] {Message}{NewLine}{Exception}"),
                bufferSize: 10000,
                monitor: asyncLogBufferMonitor)
            .CreateLogger();

        // Route Serilog's own diagnostics to Debug output so sink failures
        // (e.g. disk full) are visible during development and debugging.
        Serilog.Debugging.SelfLog.Enable(msg => Debug.WriteLine($"[Serilog.SelfLog] {msg}"));
    }

    // ── diagnostics / testing ──────────────────────────────────────────

    internal string LogFilePath => logFilePath;

    // ── public API ──────────────────────────────────────────────────────

    /// <summary>
    /// Adjusts the minimum log level at runtime without restarting the process.
    /// </summary>
    public void SetMinimumLevel(LogEventLevel level)
    {
        levelSwitch.MinimumLevel = level;
    }

    /// <summary>
    /// Returns the current minimum log level so callers (e.g. settings UI)
    /// can display it or persist it.
    /// </summary>
    public LogEventLevel MinimumLevel => levelSwitch.MinimumLevel;

    public async Task LogAsync(
        LogEntrySeverity severity,
        string title,
        string? message = null,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        // Note: cancellationToken is checked here to support the caller's
        // cancellation needs, but logging is inherently fire-and-forget.
        // If the caller has already been cancelled, their diagnostic about
        // why they were cancelled is the most valuable log entry to keep.
        try
        {
            var level = severity switch
            {
                LogEntrySeverity.Verbose => LogEventLevel.Verbose,
                LogEntrySeverity.Debug => LogEventLevel.Debug,
                LogEntrySeverity.Info => LogEventLevel.Information,
                LogEntrySeverity.Warn => LogEventLevel.Warning,
                LogEntrySeverity.Error => LogEventLevel.Error,
                LogEntrySeverity.Fatal => LogEventLevel.Fatal,
                _ => LogEventLevel.Information
            };

            var msg = title;
            if (!string.IsNullOrEmpty(message))
                msg += "\n" + message;

            // Attach structured properties for searchability without changing
            // the human-readable log line.
            serilogLogger
                .ForContext("LogTitle", title)
                .ForContext("LogMessage", message)
                .Write(level, exception, msg);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelled by caller — expected during shutdown.
            // Suppress the OperationCanceledException here since logging
            // must never crash the app.
        }
        catch
        {
            // Best-effort — logging must never crash the app.
        }
    }

    public async Task WriteSessionStartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var version = BuildInfo.LauncherVersion;
            var commitSha = BuildInfo.CommitSha;
            var os = Environment.OSVersion.ToString();
            var framework = RuntimeInformation.FrameworkDescription;
            var buildConfig = BuildInfo.BuildConfiguration;

            var message = new StringBuilder();
            message.AppendLine("Session started");
            message.Append("Version: ").Append(version)
                   .Append("  CommitSha: ").Append(commitSha).AppendLine();
            message.Append("OS: ").Append(os)
                   .Append("  Framework: ").Append(framework).AppendLine();
            message.Append("BuildConfig: ").Append(buildConfig).AppendLine();

            serilogLogger
                .ForContext("LogTitle", "Session")
                .ForContext("SessionVersion", version)
                .ForContext("SessionCommitSha", commitSha)
                .ForContext("SessionOS", os)
                .ForContext("SessionFramework", framework)
                .ForContext("SessionBuildConfig", buildConfig)
                .Information(message.ToString());
        }
        catch
        {
            // Best-effort.
        }
    }

    public async Task WriteSessionEndAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            serilogLogger.ForContext("LogTitle", "Session").Information("Session ended");
        }
        catch
        {
            // Best-effort.
        }
    }

    // ── IDisposable ────────────────────────────────────────────────────

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        serilogLogger.Dispose();
        asyncLogBufferMonitor.Dispose();
        GC.SuppressFinalize(this);
    }
}
