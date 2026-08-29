using System.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Seam between "build a ProcessStartInfo" and "actually spawn the process",
/// so runners can be unit-tested without launching real executables.
/// </summary>
public interface IProcessLauncher
{
    /// <summary>Starts the process described by <paramref name="startInfo"/>, or null on failure.</summary>
    Process? Start(ProcessStartInfo startInfo);
}

/// <summary>Default <see cref="IProcessLauncher"/> backed by <see cref="Process.Start(ProcessStartInfo)"/>.</summary>
public sealed class DefaultProcessLauncher : IProcessLauncher
{
    public Process? Start(ProcessStartInfo startInfo) => Process.Start(startInfo);
}
