using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>
/// Detects whether the previous session ended cleanly by scanning
/// the unified log for a <c>[SESSION_END]</c> marker after the last <c>[SESSION_START]</c>.
/// </summary>
public sealed class CrashRecoveryService
{
    private readonly UnifiedLogger logger;

    public CrashRecoveryService(UnifiedLogger logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the unified log exists, contains a
    /// <c>[SESSION_START]</c> marker, but no matching <c>[SESSION_END]</c> — meaning
    /// the previous session crashed or was killed.
    /// </summary>
    public async Task<bool> DetectCrashAsync(CancellationToken ct = default)
    {
        var logPath = logger.LogFilePath;
        try
        {
            if (!File.Exists(logPath))
                return false;

            bool? lastMarkerWasStart = null;
            await using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (line.EndsWith("[SESSION_START]", StringComparison.Ordinal))
                    lastMarkerWasStart = true;
                else if (line.EndsWith("[SESSION_END]", StringComparison.Ordinal))
                    lastMarkerWasStart = false;
            }

            return lastMarkerWasStart == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Detects whether the previous session crashed, then writes the current
    /// session start marker in that strict order.
    /// </summary>
    public async Task<bool> BeginSessionAsync(CancellationToken ct = default)
    {
        var crashed = await DetectCrashAsync(ct).ConfigureAwait(false);
        await logger.WriteSessionStartAsync(ct).ConfigureAwait(false);
        return crashed;
    }
}
