using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Proves a runtime executable actually works by running its version command
/// (e.g. "umu-run --version") and capturing the output. A file merely existing on
/// disk says nothing about whether the runtime can launch a game; this probe turns
/// "file exists" into "runtime responds" and returns structured evidence containing
/// either the parsed version or precise failure details.
/// </summary>
public static class RuntimeVersionProbe
{
    /// <summary>Version commands of healthy runtimes complete well within this budget.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private static readonly Regex VersionTokenPattern = new("""\d+(\.\d+)+""", RegexOptions.Compiled);

    public static async Task<RuntimeProbeResult> ProbeAsync(
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
            return new RuntimeProbeResult(
                RuntimeProbeFailureKind.ProcessStartFailed,
                ErrorMessage: exception.Message);
        }

        if (process is null)
        {
            return new RuntimeProbeResult(
                RuntimeProbeFailureKind.ProcessStartFailed,
                ErrorMessage: "Process.Start returned null.");
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
                    var timedOutOutput = await ReadCapturedOutputAsync(outputTask).ConfigureAwait(false);
                    var timedOutError = await ReadCapturedOutputAsync(errorTask).ConfigureAwait(false);
                    return new RuntimeProbeResult(
                        RuntimeProbeFailureKind.TimedOut,
                        StandardOutput: timedOutOutput,
                        StandardError: timedOutError,
                        ErrorMessage: $"The version probe exceeded {timeout}.");
                }

                await exitTask.ConfigureAwait(false);
                var output = await ReadCapturedOutputAsync(outputTask).ConfigureAwait(false);
                var error = await ReadCapturedOutputAsync(errorTask).ConfigureAwait(false);
                var exitCode = TryReadExitCode(process);
                if (exitCode is not 0)
                {
                    return new RuntimeProbeResult(
                        RuntimeProbeFailureKind.NonZeroExit,
                        ExitCode: exitCode,
                        StandardOutput: output,
                        StandardError: error);
                }

                var version = ParseVersion(output, error);
                return version is null
                    ? new RuntimeProbeResult(
                        RuntimeProbeFailureKind.EmptyOutput,
                        ExitCode: exitCode,
                        StandardOutput: output,
                        StandardError: error)
                    : RuntimeProbeResult.Success(version, exitCode.Value, output, error);
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

    private static async Task<string> ReadCapturedOutputAsync(Task<string> outputTask)
    {
        try
        {
            return await outputTask.ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
            return $"<capture failed: {exception.Message}>";
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
