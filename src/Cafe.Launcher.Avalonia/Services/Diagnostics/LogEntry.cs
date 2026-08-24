using System;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>Severity level for unified log entries.</summary>
public enum LogEntrySeverity
{
    Verbose,
    Debug,
    Info,
    Warn,
    Error,
    Fatal
}
