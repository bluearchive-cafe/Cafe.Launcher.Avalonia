using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Best-effort reader for Chrome localStorage LevelDB data.
/// Scans .ldb and .log files for known key prefixes via byte-level search.
/// Never throws — returns an empty dictionary on any failure.
/// </summary>
public static class LevelDbReader
{
    private static readonly Dictionary<string, byte[]> KnownKeyPrefixes = new()
    {
        // Chrome localStorage stores keys as: _<keyname>\x01
        ["downloadPath"] = Encoding.UTF8.GetBytes("_downloadPath\x01"),
        ["proxy-config"] = Encoding.UTF8.GetBytes("_proxy-config\x01"),
        ["close-choice"] = Encoding.UTF8.GetBytes("_close-choice\x01"),
    };

    /// <summary>
    /// Scans a LevelDB directory for known localStorage key-value pairs.
    /// </summary>
    /// <param name="levelDbPath">Path to the leveldb directory.</param>
    /// <returns>Dictionary mapping setting keys to their extracted string values.</returns>
    public static Dictionary<string, string> TryReadValues(string levelDbPath)
    {
        var results = new Dictionary<string, string>();

        if (!Directory.Exists(levelDbPath))
            return results;

        try
        {
            // Scan both .ldb (SSTable) and .log (WAL) files
            var files = new List<string>();
            try
            {
                files.AddRange(Directory.EnumerateFiles(levelDbPath, "*.ldb"));
            }
            catch { }
            try
            {
                files.AddRange(Directory.EnumerateFiles(levelDbPath, "*.log"));
            }
            catch { }

            foreach (var file in files)
            {
                if (results.Count >= KnownKeyPrefixes.Count)
                    break;

                ScanFile(file, results);
            }
        }
        catch
        {
            // Best effort — return whatever we found
        }

        return results;
    }

    private static void ScanFile(string filePath, Dictionary<string, string> results)
    {
        try
        {
            var content = File.ReadAllBytes(filePath);
            var span = content.AsSpan();

            foreach (var (key, prefix) in KnownKeyPrefixes)
            {
                if (results.ContainsKey(key))
                    continue;

                var index = span.IndexOf(prefix);
                if (index < 0)
                    continue;

                var valueStart = index + prefix.Length;
                if (valueStart >= span.Length)
                    continue;

                // Find the end of the value — scan for a null byte or non-ASCII control char
                // that marks the end of the value in LevelDB's encoding
                var valueEnd = valueStart;
                var maxEnd = Math.Min(valueStart + 512, span.Length); // Reasonable max value length
                while (valueEnd < maxEnd)
                {
                    var b = span[valueEnd];
                    // Null byte or common LevelDB record separators
                    if (b == 0x00 || b == 0x01 || b == 0x02 || b == 0x08 || b == 0x10)
                        break;
                    // Non-printable control chars (except common path separators)
                    if (b < 0x20 && b != 0x09 && b != 0x0A && b != 0x0D)
                        break;
                    valueEnd++;
                }

                if (valueEnd <= valueStart)
                    continue;

                try
                {
                    var value = Encoding.UTF8.GetString(span[valueStart..valueEnd]);
                    if (IsPlausibleValue(key, value))
                        results[key] = value;
                }
                catch
                {
                    // Skip this value
                }
            }
        }
        catch
        {
            // Best effort per file
        }
    }

    private static bool IsPlausibleValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return key switch
        {
            "downloadPath" => value.Length > 2 && (value.Contains('\\') || value.Contains('/')),
            "proxy-config" => value is "direct" or "system",
            "close-choice" => value is "minimize" or "exit",
            _ => false
        };
    }
}
