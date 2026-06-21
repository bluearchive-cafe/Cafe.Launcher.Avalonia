using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>
/// Tracks whether the previous process session ended cleanly through a dedicated
/// active-session marker that is independent from log rotation.
/// </summary>
public sealed class CrashRecoveryService
{
    private readonly UnifiedLogger logger;
    private readonly string activeSessionPath;

    public CrashRecoveryService(UnifiedLogger logger)
    {
        this.logger = logger;
        activeSessionPath = Path.Combine(
            Path.GetDirectoryName(logger.LogFilePath)!,
            "session.active");
    }

    /// <summary>
    /// Returns <see langword="true"/> when the previous process left its
    /// active-session marker behind.
    /// </summary>
    public Task<bool> DetectCrashAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(activeSessionPath));
    }

    /// <summary>
    /// Detects whether the previous session crashed, then writes the current
    /// session start marker in that strict order.
    /// </summary>
    public async Task<bool> BeginSessionAsync(CancellationToken ct = default)
    {
        var crashed = false;
        try
        {
            crashed = await DetectCrashAsync(ct).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(activeSessionPath)!);
            await File.WriteAllTextAsync(
                activeSessionPath,
                DateTimeOffset.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Session tracking is diagnostic and must not prevent startup.
        }

        await logger.WriteSessionStartAsync(ct).ConfigureAwait(false);
        return crashed;
    }

    /// <summary>
    /// Marks the current session as cleanly completed and writes the diagnostic
    /// session-end entry.
    /// </summary>
    public async Task CompleteSessionAsync(CancellationToken ct = default)
    {
        await logger.WriteSessionEndAsync(ct).ConfigureAwait(false);
        try
        {
            File.Delete(activeSessionPath);
        }
        catch
        {
            // Session tracking is best-effort during process shutdown.
        }
    }
}
