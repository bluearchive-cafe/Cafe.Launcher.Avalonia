using System;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>
/// Half-open time window of a diagnostics export: <see cref="From"/> is inclusive and
/// <see cref="To"/> is exclusive, with a <see langword="null"/> bound meaning "unbounded on
/// that side". Both bounds <see langword="null"/> means the window keeps everything, which
/// lets callers copy whole files instead of filtering them line by line.
/// </summary>
public readonly record struct ExportWindow(DateTimeOffset? From, DateTimeOffset? To)
{
    /// <summary>A window that keeps every entry and every artifact.</summary>
    public static ExportWindow Unbounded => default;

    /// <summary>Gets whether the window keeps everything.</summary>
    public bool IsUnbounded => From is null && To is null;

    /// <summary>Gets whether <paramref name="timestamp"/> falls inside the window.</summary>
    public bool Contains(DateTimeOffset? timestamp)
    {
        // An entry without a parsable timestamp cannot be placed in time; the
        // export keeps it rather than silently dropping diagnostics.
        if (timestamp is null)
        {
            return true;
        }

        return (From is null || timestamp.Value >= From.Value)
            && (To is null || timestamp.Value < To.Value);
    }

    /// <summary>Gets whether a file written at <paramref name="lastWriteUtc"/> falls inside the window.</summary>
    public bool ContainsFileWrittenAt(DateTime lastWriteUtc) =>
        Contains(new DateTimeOffset(lastWriteUtc, TimeSpan.Zero));
}
