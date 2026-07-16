using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.ViewModels;

/// <summary>Presentation model for one parsed diagnostic log entry.</summary>
public sealed class LogEntryDisplay
{
    /// <summary>Gets or sets the serialized timestamp shown to the user.</summary>
    public string TimestampText { get; set; } = "";
    /// <summary>Gets or sets the localized severity label.</summary>
    public string SeverityLabel { get; set; } = "";
    /// <summary>Gets or sets the first-line entry title.</summary>
    public string Title { get; set; } = "";
    /// <summary>Gets or sets continuation lines and exception details.</summary>
    public string Details { get; set; } = "";
    /// <summary>Gets or sets the structured severity.</summary>
    public LogEntrySeverity Severity { get; set; }
}
