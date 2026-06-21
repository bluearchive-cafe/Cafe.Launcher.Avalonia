using System;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>Severity level for unified log entries.</summary>
public enum LogEntrySeverity
{
    Error,
    Warn,
    Info
}

/// <summary>Immutable structured log entry used internally by the logging pipeline.</summary>
internal readonly record struct LogEntry(
    DateTimeOffset Timestamp,
    LogEntrySeverity Severity,
    int SequenceNumber,
    string Title,
    string? Message,
    string? ExceptionString);
