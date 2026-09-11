using System;
using System.Text.Json.Serialization;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>Serializable snapshot describing one unrecoverable launcher failure.</summary>
public sealed record CrashReport
{
    public required string Id { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string Source { get; init; }

    public required string AppVersion { get; init; }

    /// <summary>Commit the crashing build was produced from; identifies the exact build.</summary>
    public required string BuildSha { get; init; }

    public required string OperatingSystem { get; init; }

    public required string UiCulture { get; init; }

    public required string ExceptionType { get; init; }

    public required string TechnicalDetails { get; init; }

    /// <summary>
    /// Absolute path of the persisted snapshot. Runtime-only: every reader receives the path from
    /// its caller (the reporter gets it as a command-line argument, <see cref="CrashReportStore.TryRead"/>
    /// re-derives it from its argument), so serializing it would only add the user's directory —
    /// and with it the OS account name — to a document that is meant to be share-safe.
    /// </summary>
    [JsonIgnore]
    public string SnapshotPath { get; init; } = "";
}
