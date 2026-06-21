using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>
/// Centralised logging engine. All error, warning, and informational messages
/// flow through this singleton and are persisted to a single rotating log file.
/// </summary>
public sealed class UnifiedLogger : IDisposable
{
    private readonly string logDirectory;
    private readonly string logFilePath;
    private readonly LogRotationManager? rotationManager;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private int sequenceNumber;
    private bool disposed;

    public UnifiedLogger() : this(null, null) { }

    public UnifiedLogger(LogRotationManager rotationManager) : this(null, rotationManager) { }

    internal UnifiedLogger(string? logDirectory, LogRotationManager? rotationManager = null)
    {
        this.rotationManager = rotationManager;
        this.logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LauncherConstants.ProductName);
        logFilePath = Path.Combine(this.logDirectory, "unified.log");
    }

    // ── diagnostics / testing ──────────────────────────────────────────

    internal string LogFilePath => logFilePath;
    internal int CurrentSequenceNumber => Volatile.Read(ref sequenceNumber);

    // ── public API ──────────────────────────────────────────────────────

    public async Task LogAsync(
        LogEntrySeverity severity,
        string title,
        string? message = null,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var seq = Interlocked.Increment(ref sequenceNumber);
            var entry = new LogEntry(
                DateTimeOffset.Now, severity, seq, title, message,
                exception?.ToString());

            await WriteEntryAsync(entry, cancellationToken).ConfigureAwait(false);
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
            builder.AppendLine(Separator);

            await WriteTextAsync(builder.ToString(), cancellationToken).ConfigureAwait(false);
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
            var builder = new StringBuilder();
            builder.Append(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
            builder.Append(" [SESSION_END]").AppendLine();
            builder.AppendLine(Separator);

            await WriteTextAsync(builder.ToString(), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort.
        }
    }

    // ── internals ───────────────────────────────────────────────────────

    private const string Separator = "---";

    private async Task WriteEntryAsync(LogEntry entry, CancellationToken ct)
    {
        var builder = new StringBuilder();
        var severityLabel = entry.Severity switch
        {
            LogEntrySeverity.Error => "ERROR",
            LogEntrySeverity.Warn => "WARN",
            LogEntrySeverity.Info => "INFO",
            _ => "???"
        };

        builder.Append(entry.Timestamp.ToString("O", CultureInfo.InvariantCulture))
               .Append(' ').Append('[').Append(severityLabel).Append(']').Append(" #")
               .Append(entry.SequenceNumber.ToString("D3", CultureInfo.InvariantCulture))
               .Append(' ').Append(entry.Title).AppendLine();
        if (entry.Message is not null)
            builder.AppendLine(entry.Message);
        if (entry.ExceptionString is not null)
            builder.AppendLine(entry.ExceptionString);
        builder.AppendLine(Separator);

        await WriteTextAsync(builder.ToString(), ct).ConfigureAwait(false);
    }

    private async Task WriteTextAsync(string text, CancellationToken ct)
    {
        await writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(logDirectory);
            await File.AppendAllTextAsync(logFilePath, text, Encoding.UTF8, ct).ConfigureAwait(false);

            if (rotationManager is not null)
                await rotationManager.RotateIfNeededAsync(logFilePath, ct).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        writeLock.Dispose();
    }
}
