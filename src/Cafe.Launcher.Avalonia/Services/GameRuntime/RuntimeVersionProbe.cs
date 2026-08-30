using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Proves a runtime executable actually works by running its version command
/// (e.g. "umu-run --version") and capturing the output. A file merely existing on
/// disk says nothing about whether the runtime can launch a game; this probe turns
/// "file exists" into "runtime responds". Returns the parsed version string, or
/// null when the process fails to start, exits non-zero, or times out.
/// </summary>
public static class RuntimeVersionProbe
{
    /// <summary>Version commands of healthy runtimes complete well within this budget.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private static readonly Regex VersionTokenPattern = new("""\d+(\.\d+)+""", RegexOptions.Compiled);

    public static async Task<string?> ProbeAsync(
        string executablePath,
        string versionArgument,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(versionArgument);

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or Win32Exception or PlatformNotSupportedException)
        {
            return null;
        }

        if (process is null)
        {
            return null;
        }

        using (process)
        {
            try
            {
                // Drain both pipes concurrently with the exit wait so a chatty
                // runtime cannot deadlock the probe on full pipe buffers.
                var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                var exitTask = process.WaitForExitAsync(cancellationToken);
                var completed = await Task
                    .WhenAny(exitTask, Task.Delay(timeout, cancellationToken))
                    .ConfigureAwait(false);
                if (completed != exitTask)
                {
                    KillProcessTree(process);
                    cancellationToken.ThrowIfCancellationRequested();
                    // Observe the abandoned pipe reads so a late cancellation cannot
                    // surface as an unobserved task exception; after the kill they
                    // complete (or fault immediately) instead of lingering.
                    await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
                    return null;
                }

                await exitTask.ConfigureAwait(false);
                if (TryReadExitCode(process) is not 0)
                {
                    return null;
                }

                var output = await outputTask.ConfigureAwait(false);
                var error = await errorTask.ConfigureAwait(false);
                return ParseVersion(output, error);
            }
            catch (OperationCanceledException)
            {
                KillProcessTree(process);
                throw;
            }
        }
    }

    /// <summary>Picks the bare version number out of a version command's first output line.</summary>
    internal static string? ParseVersion(string standardOutput, string standardError)
    {
        var firstLine = FirstNonEmptyLine(standardOutput) ?? FirstNonEmptyLine(standardError);
        if (firstLine is null)
        {
            return null;
        }

        // "umu-launcher 1.4.4" → "1.4.4"; "wine-9.0" → "9.0". When no dotted
        // version token exists, keep the whole line as the reported version.
        return VersionTokenPattern.Match(firstLine) is { Success: true } match
            ? match.Value
            : firstLine;
    }

    /// <summary>
    /// Standard technical explanation attached when a runtime's version probe
    /// fails — single source of truth so runners cannot drift from the probe's
    /// actual failure modes.
    /// </summary>
    internal static string DescribeProbeFailure(string executablePath) =>
        $"\"{executablePath} --version\" failed to start, exited non-zero, or timed out.";

    private static string? FirstNonEmptyLine(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return null;
    }

    private static int? TryReadExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or Win32Exception or ObjectDisposedException)
        {
            // e.g. terminated by a signal on Unix — treat as probe failure.
            return null;
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or Win32Exception or ObjectDisposedException)
        {
            // Already exited or no longer killable; nothing left to clean up.
        }
    }
}
