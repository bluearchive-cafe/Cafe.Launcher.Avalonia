using System;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>Serializable snapshot describing one unrecoverable launcher failure.</summary>
public sealed record CrashReport
{
    public required string Id { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string Source { get; init; }

    public required string AppVersion { get; init; }

    public required string OperatingSystem { get; init; }

    public required string UiCulture { get; init; }

    public required string ExceptionType { get; init; }

    public required string TechnicalDetails { get; init; }

    /// <summary>Absolute path of the persisted snapshot; populated after serialization.</summary>
    public string SnapshotPath { get; init; } = "";
}
