using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>
/// Stateless log file rotation. Renames <c>unified.log</c> → <c>unified.log.1</c>,
/// <c>.1</c> → <c>.2</c>, etc., and deletes the oldest file when the limit is exceeded.
/// </summary>
public sealed class LogRotationManager
{
    internal const long MaxFileSize = 5L * 1024 * 1024; // 5 MB
    internal const int MaxRotatedFiles = 3;

    /// <summary>
    /// Checks whether <paramref name="logFilePath"/> exceeds <see cref="MaxFileSize"/>
    /// and rotates all files with the same stem if it does.
    /// Returns <see langword="true"/> when rotation was performed.
    /// </summary>
    public async Task<bool> RotateIfNeededAsync(string logFilePath, CancellationToken ct = default)
    {
        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(logFilePath);
        }
        catch
        {
            return false;
        }

        if (!fileInfo.Exists || fileInfo.Length < MaxFileSize)
            return false;

        await Task.Run(() => Rotate(logFilePath), ct).ConfigureAwait(false);
        return true;
    }

    private static void Rotate(string logFilePath)
    {
        // Delete the oldest rotated file to make room.
        var oldest = $"{logFilePath}.{MaxRotatedFiles}";
        try { if (File.Exists(oldest)) File.Delete(oldest); } catch { }

        // Shift: unified.log.2 → unified.log.3, unified.log.1 → unified.log.2
        for (var i = MaxRotatedFiles - 1; i >= 1; i--)
        {
            var from = $"{logFilePath}.{i}";
            var to = $"{logFilePath}.{i + 1}";
            try { if (File.Exists(from)) File.Move(from, to, overwrite: true); } catch { }
        }

        // Rotate current: unified.log → unified.log.1
        try { File.Move(logFilePath, $"{logFilePath}.1", overwrite: true); } catch { }
    }
}
