using System;
using System.Collections.Generic;

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
        if (!string.IsNullOrWhiteSpace(RunnerVersion))
        {
            lines.Add($"RunnerVersion: {RunnerVersion}");
        }

        if (!string.IsNullOrWhiteSpace(RunnerExecutable))
        {
            lines.Add($"Executable: {RunnerExecutable}");
        }

        lines.Add($"GameId: {GameId}");
        lines.Add($"GameExecutable: {GameExecutable}");
        lines.Add($"WorkingDirectory: {WorkingDirectory}");
        if (!string.IsNullOrWhiteSpace(PrefixPath))
        {
            lines.Add($"Prefix: {PrefixPath}");
        }

        if (!string.IsNullOrWhiteSpace(ProtonPath))
        {
            lines.Add($"Proton: {ProtonPath}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
