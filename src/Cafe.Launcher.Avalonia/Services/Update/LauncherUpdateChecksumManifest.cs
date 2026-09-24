using System;
using System.Collections.Generic;
using System.IO;

namespace Cafe.Launcher.Avalonia.Services.Update;

/// <summary>
/// Parses the release's <c>SHA256SUMS</c> manifest (the <c>sha256sum</c> text format:
/// a 64-hex digest, whitespace, then the file name, optionally prefixed with <c>*</c>
/// for binary mode) into a name-to-digest map. Parsing is strict: any malformed line,
/// duplicate name, or empty content fails the whole manifest, so a truncated or
/// tampered manifest can never silently verify a package.
/// </summary>
internal static class LauncherUpdateChecksumManifest
{
    private static readonly char[] Separators = [' ', '\t'];

    /// <summary>
    /// Tries to parse <paramref name="content"/> into a file-name keyed digest map.
    /// Returns false for empty content, a malformed line, a repeated name, or a
    /// digest that is not 64 hexadecimal characters.
    /// </summary>
    public static bool TryParse(string? content, out IReadOnlyDictionary<string, string> hashes)
    {
        hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = new StringReader(content);
        string? rawLine;
        while ((rawLine = reader.ReadLine()) is not null)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separatorIndex = line.IndexOfAny(Separators);
            if (separatorIndex <= 0)
            {
                return false;
            }

            var hash = line[..separatorIndex];
            var fileName = line[(separatorIndex + 1)..].TrimStart(Separators);
            if (fileName.StartsWith('*'))
            {
                fileName = fileName[1..];
            }

            if (!IsHexSha256(hash) || fileName.Length == 0)
            {
                return false;
            }

            if (!parsed.TryAdd(fileName, hash.ToLowerInvariant()))
            {
                return false;
            }
        }

        if (parsed.Count == 0)
        {
            return false;
        }

        hashes = parsed;
        return true;
    }

    /// <summary>Gets the digest for <paramref name="fileName"/>, or false when the manifest omits it.</summary>
    public static bool TryGetHash(
        IReadOnlyDictionary<string, string> hashes,
        string fileName,
        out string hash)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        if (hashes.TryGetValue(fileName, out var found))
        {
            hash = found;
            return true;
        }

        hash = "";
        return false;
    }

    /// <summary>True when <paramref name="value"/> is exactly 64 hexadecimal characters.</summary>
    public static bool IsHexSha256(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
