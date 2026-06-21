using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Serilog;
using Serilog.Events;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>
/// Centralised logging engine. All error, warning, and informational messages
/// flow through this singleton and are persisted to a single rotating log file
/// (backed by Serilog).
/// </summary>
public sealed class UnifiedLogger : IDisposable
{
    private readonly Serilog.Core.Logger serilogLogger;
    private readonly string logFilePath;
    private bool disposed;

    public UnifiedLogger() : this(null) { }

    internal UnifiedLogger(string? logDirectory)
    {
        var dir = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LauncherConstants.ProductName);
        logFilePath = Path.Combine(dir, "unified.log");

        serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.File(
                logFilePath,
                formatProvider: CultureInfo.InvariantCulture,
                fileSizeLimitBytes: 5L * 1024 * 1024,     // 5 MB
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 4,                  // current + 3 rotated
                rollingInterval: RollingInterval.Infinite,
                outputTemplate: "{Timestamp:O} [{Level:u3}] {Message:l}{NewLine}{Exception}")
            .CreateLogger();
    }

    // ── diagnostics / testing ──────────────────────────────────────────

    internal string LogFilePath => logFilePath;

    // ── public API ──────────────────────────────────────────────────────

    public async Task LogAsync(
        LogEntrySeverity severity,
        string title,
        string? message = null,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var level = severity switch
            {
                LogEntrySeverity.Error => LogEventLevel.Error,
                LogEntrySeverity.Warn => LogEventLevel.Warning,
                _ => LogEventLevel.Information
            };

            var msg = title;
            if (!string.IsNullOrEmpty(message))
                msg += "\n" + message;

            serilogLogger.Write(level, exception, msg);
            await Task.CompletedTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
            var builder = new StringBuilder();
            builder.Append(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
            builder.Append(" [SESSION_START]").AppendLine();
            builder.Append("Version: ").Append(BuildInfo.LauncherVersion)
                   .Append("  CommitSha: ").Append(BuildInfo.CommitSha).AppendLine();
            builder.Append("OS: ").Append(Environment.OSVersion.ToString())
                   .Append("  Framework: ").Append(RuntimeInformation.FrameworkDescription).AppendLine();
            builder.Append("BuildConfig: ").Append(BuildInfo.BuildConfiguration).AppendLine();

            serilogLogger.Information("Session started\n" + builder);
            await Task.CompletedTask;
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
            serilogLogger.Information("Session ended");
            await Task.CompletedTask;
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
    }
}
