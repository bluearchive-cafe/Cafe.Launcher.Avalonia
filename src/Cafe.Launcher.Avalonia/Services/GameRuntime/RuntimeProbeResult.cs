using System;
using System.Collections.Generic;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>Reason a runtime version probe did not produce a usable version.</summary>
public enum RuntimeProbeFailureKind
{
    None,
    ProcessStartFailed,
    NonZeroExit,
    TimedOut,
    EmptyOutput
}

/// <summary>
/// Structured evidence from a runtime health probe. Diagnostics retain the exit
/// code and captured output instead of collapsing every failure into <c>null</c>.
/// </summary>
public sealed record RuntimeProbeResult(
    RuntimeProbeFailureKind FailureKind,
    string? Version = null,
    int? ExitCode = null,
    string StandardOutput = "",
    string StandardError = "",
    string? ErrorMessage = null)
{
    public bool Succeeded => FailureKind == RuntimeProbeFailureKind.None;

    public static RuntimeProbeResult Success(
        string version,
        int exitCode,
        string standardOutput,
        string standardError) =>
        new(
            RuntimeProbeFailureKind.None,
            version,
            exitCode,
            standardOutput,
            standardError);

    public string Describe(string executablePath, string versionArgument)
    {
        var lines = new List<string>
        {
            $"Command: \"{executablePath}\" {versionArgument}",
            $"Failure: {FailureKind}"
        };
        if (ExitCode is not null)
        {
            lines.Add($"ExitCode: {ExitCode.Value}");
        }

        DiagnosticLines.Optional(lines, "StandardOutput", StandardOutput?.Trim());
        DiagnosticLines.Optional(lines, "StandardError", StandardError?.Trim());
        DiagnosticLines.Optional(lines, "Error", ErrorMessage);

        return string.Join(Environment.NewLine, lines);
    }
}
