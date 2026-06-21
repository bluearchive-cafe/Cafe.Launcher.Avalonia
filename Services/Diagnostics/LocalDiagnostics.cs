using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

public sealed class LocalDiagnostics
{
    private readonly string logPath;

    private static string GetLogPath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LauncherConstants.ProductName);
        return Path.Combine(folder, "diagnostics.log");
    }

    public LocalDiagnostics()
    {
        logPath = GetLogPath();
    }

    public async Task ErrorAsync(string title, Exception exception, CancellationToken cancellationToken = default)
    {
        try
        {
            var builder = new StringBuilder();
            builder.AppendLine(DateTimeOffset.Now.ToString("O"));
            builder.AppendLine(title);
            builder.AppendLine(exception.ToString());
            builder.AppendLine();

            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await File.AppendAllTextAsync(logPath, builder.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort — diagnostic logging must never crash the app
        }
    }

    public async Task MessageAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        try
        {
            var builder = new StringBuilder();
            builder.AppendLine(DateTimeOffset.Now.ToString("O"));
            builder.AppendLine(title);
            builder.AppendLine(message);
            builder.AppendLine();

            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await File.AppendAllTextAsync(logPath, builder.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort — diagnostic logging must never crash the app
        }
    }

    /// <summary>
    /// Synchronous log write for use in synchronous contexts (e.g. constructors, static methods).
    /// </summary>
    public static void LogSync(string title, string message)
    {
        try
        {
            var logPath = GetLogPath();
            var builder = new StringBuilder();
            builder.AppendLine(DateTimeOffset.Now.ToString("O"));
            builder.AppendLine(title);
            builder.AppendLine(message);
            builder.AppendLine();

            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, builder.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Best-effort — diagnostic logging must never crash the app
        }
    }
}
