using System.Collections.Generic;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>
/// Answers where crash-report snapshots may live. The log exporter consumes this
/// seam so its crash-archive section mirrors the store's location policy by type
/// instead of by naming convention — a new crash location added on the store side
/// flows into exports through one implementation, not a hand-mirrored list.
/// Implemented by <see cref="CrashReportStore"/>, the single owner of the
/// primary + fallback location policy (ADR-019).
/// </summary>
public interface ICrashReportLocator
{
    /// <summary>Returns every directory that may contain crash reports for the supplied user-data root.</summary>
    IEnumerable<string> GetCrashReportDirectories(string userDataRoot);
}
