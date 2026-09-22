using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>The GPU/Vulkan facts a diagnostics export records, when the tools are available.</summary>
internal sealed record GraphicsInfo(string? Vulkan, string? OpenGl);

/// <summary>
/// Best-effort GPU/Vulkan probe for the diagnostics export: runs <c>vulkaninfo --summary</c> and
/// <c>glxinfo -B</c> with a short timeout and keeps only the informative lines. DXVK/Proton issues
/// are driver- and session-dependent, so "which driver answered" is exactly what a failed-launch
/// report is missing.
/// </summary>
/// <remarks>
/// <para>Never fails the export: a missing tool, a non-zero exit, a timeout, or unreadable output all
/// produce <see langword="null"/> for that half. Both halves null means the whole probe is null.</para>
/// <para>The tool runner is injectable so the line filtering can be tested without the real tools;
/// tool absence is covered by <see cref="ExecutableLocator"/> returning null.</para>
/// </remarks>
public sealed class GraphicsInfoProbe
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    internal const int MaxCharactersPerTool = 4096;

    private readonly Func<string, string, TimeSpan, string?> runTool;

    public GraphicsInfoProbe()
        : this(RunToolCapture)
    {
    }

    internal GraphicsInfoProbe(Func<string, string, TimeSpan, string?> runTool)
    {
        ArgumentNullException.ThrowIfNull(runTool);
        this.runTool = runTool;
    }

    /// <summary>Probes both tools; returns null when neither produced a usable line.</summary>
    internal GraphicsInfo? Probe()
    {
        var vulkan = Capture("vulkaninfo", "--summary", IsVulkanSummaryLine);
        var opengl = Capture("glxinfo", "-B", IsOpenGlLine);
        return vulkan is null && opengl is null ? null : new GraphicsInfo(vulkan, opengl);
    }

    private string? Capture(string tool, string arguments, Func<string, bool> keepLine)
    {
        string? output;
        try
        {
            output = runTool(tool, arguments, DefaultTimeout);
        }
        catch (Exception)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        List<string> kept = output
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(keepLine)
            .ToList();
        if (kept.Count == 0)
        {
            return null;
        }

        var text = string.Join('\n', kept);
        return text.Length <= MaxCharactersPerTool ? text : text[..MaxCharactersPerTool];
    }

    private static bool IsVulkanSummaryLine(string line) =>
        line.Contains("deviceName", StringComparison.OrdinalIgnoreCase)
        || line.Contains("driverName", StringComparison.OrdinalIgnoreCase)
        || line.Contains("driverInfo", StringComparison.OrdinalIgnoreCase)
        || line.Contains("apiVersion", StringComparison.OrdinalIgnoreCase)
        || line.Contains("GPU", StringComparison.Ordinal);

    private static bool IsOpenGlLine(string line) =>
        line.StartsWith("OpenGL ", StringComparison.OrdinalIgnoreCase);

    private static string? RunToolCapture(string tool, string arguments, TimeSpan timeout)
    {
        var executable = ExecutableLocator.FindInPath(tool);
        if (executable is null)
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(arguments);

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
            // Drain both pipes concurrently with the exit wait so a chatty driver cannot deadlock.
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                TryKill(process);
                return null;
            }

            try
            {
                return output.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Best effort: the probe reports nothing rather than failing the export.
        }
    }
}
