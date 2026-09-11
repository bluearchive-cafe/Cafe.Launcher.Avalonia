using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>
/// One unified-log entry: its header line plus every continuation line that belongs to it
/// (multi-line message body and exception text).
/// </summary>
internal sealed record LogRecord(
    string TimestampText,
    DateTimeOffset? Timestamp,
    string SeverityCode,
    string Title,
    IReadOnlyList<string> Lines);

/// <summary>
/// Groups the unified log's raw lines into entries. Serilog writes one header line per entry
/// (<c>{Timestamp:O} [LEVEL] [Title] message</c>) followed by the message body and exception
/// text, so any line that is not a header belongs to the entry above it. Shared by the log
/// viewer and the export filter so both agree on entry boundaries.
/// </summary>
internal static class LogEntryReader
{
    internal static readonly Regex HeaderLineRegex = new(
        @"^(\d{4}-\d{2}-\d{2}T[\d:.+-]+) \[(ERR|WRN|INF|VRB|DBG|FTL)\] (.+)",
        RegexOptions.CultureInvariant);

    /// <summary>Reads entries in file order; lines before the first header are ignored.</summary>
    public static IEnumerable<LogRecord> Read(IEnumerable<string> lines)
    {
        string? timestampText = null;
        DateTimeOffset? timestamp = null;
        string severityCode = "";
        string title = "";
        List<string>? entryLines = null;

        foreach (var line in lines)
        {
            var match = HeaderLineRegex.Match(line);
            if (match.Success)
            {
                if (entryLines is not null)
                    yield return new LogRecord(timestampText!, timestamp, severityCode, title, entryLines);

                timestampText = match.Groups[1].Value;
                timestamp = TryParseTimestamp(timestampText, out var parsed) ? parsed : null;
                severityCode = match.Groups[2].Value;
                title = match.Groups[3].Value;
                entryLines = [line];
            }
            else if (entryLines is not null)
            {
                entryLines.Add(line);
            }
        }

        if (entryLines is not null)
            yield return new LogRecord(timestampText!, timestamp, severityCode, title, entryLines);
    }

    /// <summary>Parses a Serilog round-trip ("O") timestamp written with the invariant culture.</summary>
    public static bool TryParseTimestamp(string timestampText, out DateTimeOffset timestamp) =>
        DateTimeOffset.TryParse(
            timestampText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out timestamp);
}
