using System;
using System.Collections.Generic;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Complete picture of the runtime environment chosen for one launch, answering:
/// which runner was selected, which runtime executable and version it uses, which
/// prefix and Proton build apply, and which game executable it targets. Attached
/// to launch diagnostics so failures can be attributed to the right layer.
/// Entries that do not apply to the chosen runner (e.g. a prefix for native
/// execution) are null and omitted from <see cref="Describe"/>.
/// </summary>
public sealed record GameRuntimeDiagnosticSnapshot(
    string RunnerId,
    string? RunnerVersion,
    string? RunnerExecutable,
    string? PrefixPath,
    string? ProtonPath,
    string GameId,
    string GameExecutable,
    string WorkingDirectory)
{
    public string Describe()
    {
        var lines = new List<string>
        {
            "[GameRuntime]",
            $"Runner: {RunnerId}"
        };
        DiagnosticLines.Optional(lines, "RunnerVersion", RunnerVersion);
        DiagnosticLines.Optional(lines, "Executable", RunnerExecutable);
        lines.Add($"GameId: {GameId}");
        lines.Add($"GameExecutable: {GameExecutable}");
        lines.Add($"WorkingDirectory: {WorkingDirectory}");
        DiagnosticLines.Optional(lines, "Prefix", PrefixPath);
        DiagnosticLines.Optional(lines, "Proton", ProtonPath);

        return string.Join(Environment.NewLine, lines);
    }
}
