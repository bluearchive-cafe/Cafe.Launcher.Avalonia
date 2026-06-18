using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Reads game installation path from the original Yostar launcher's localStorage
/// (backed by Chromium LevelDB) for first-run migration.
/// </summary>
public static class OriginalLauncherMigrationService
{
    private static readonly string OriginalLauncherLevelDbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlueArchive_JP_Gamelauncher",
        "Local Storage",
        "leveldb");

    /// <summary>
    /// Attempts to read the game installation path from the original Yostar launcher.
    /// </summary>
    /// <returns>The game path if found and the directory exists on disk; otherwise null.</returns>
    public static string? TryGetGamePath()
    {
        if (!Directory.Exists(OriginalLauncherLevelDbPath))
        {
            return null;
        }

        try
        {
            // LevelDB stores localStorage key-value pairs across .log (write-ahead log)
            // and .ldb (sorted table) files. Scan both, preferring .log as it contains
            // the most recent writes.
            var files = Directory.EnumerateFiles(OriginalLauncherLevelDbPath, "*.log")
                .Concat(Directory.EnumerateFiles(OriginalLauncherLevelDbPath, "*.ldb"));

            foreach (var filePath in files)
            {
                var result = TryExtractFromFile(filePath);
                if (result is not null)
                {
                    return result;
                }
            }
        }
        catch
        {
            // Migration failure is non-fatal — user can set the path manually.
        }

        return null;
    }

    private static string? TryExtractFromFile(string filePath)
    {
        byte[] data;
        try
        {
            data = File.ReadAllBytes(filePath);
        }
        catch
        {
            return null;
        }

        // Search for the UTF-8 bytes of the localStorage key "downloadPath"
        var keyBytes = "downloadPath"u8;
        var offset = 0;
        while (offset < data.Length - keyBytes.Length)
        {
            var keyIndex = IndexOf(data, keyBytes, offset);
            if (keyIndex < 0)
            {
                break;
            }

            // The Chromium LevelDB localStorage value for downloadPath is a
            // JSON-encoded string: "X:\\path\\to\\game". Scan forward from
            // the key position for a drive-letter path pattern.
            var result = TryExtractJsonPath(data, keyIndex);
            if (result is not null)
            {
                return result;
            }

            // Advance past this match to look for a newer write (LevelDB logs
            // are append-only; later entries for the same key overwrite earlier ones).
            offset = keyIndex + keyBytes.Length;
        }

        return null;
    }

    private static string? TryExtractJsonPath(byte[] data, int searchStart)
    {
        var searchEnd = Math.Min(data.Length, searchStart + 2048);

        for (int i = searchStart; i < searchEnd - 4; i++)
        {
            if (data[i] != '"')
            {
                continue;
            }

            // A valid Windows path begins with a drive letter: "X:\\..."
            if (i + 4 < data.Length
                && IsAsciiLetter(data[i + 1])
                && data[i + 2] == ':'
                && data[i + 3] == '\\'
                && data[i + 4] == '\\')
            {
                // Find the closing double-quote
                var endQuote = Array.IndexOf(data, (byte)'"', i + 1);
                if (endQuote < 0 || endQuote - i - 1 <= 0)
                {
                    continue;
                }

                var jsonPath = Encoding.UTF8.GetString(data, i + 1, endQuote - i - 1);

                // Unescape JSON backslashes: "I:\\YostarGames\\BlueArchive_JP" → "I:\YostarGames\BlueArchive_JP"
                var decoded = jsonPath.Replace("\\\\", "\\");

                if (!string.IsNullOrWhiteSpace(decoded) && Directory.Exists(decoded))
                {
                    return decoded;
                }
            }
        }

        return null;
    }

    private static bool IsAsciiLetter(byte b)
    {
        return b is >= ((byte)'A') and <= ((byte)'Z') or >= ((byte)'a') and <= ((byte)'z');
    }

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle, int start)
    {
        var limit = haystack.Length - needle.Length;
        for (int i = start; i <= limit; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }
}
