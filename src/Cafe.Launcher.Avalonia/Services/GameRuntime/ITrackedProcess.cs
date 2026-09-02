using System;
using System.ComponentModel;
using System.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Observed handle to a launched host process. <see cref="GameProcessTracker"/>
/// tracks through this seam so the authoritative register→exit→duration sequence
/// can be driven by a fake in tests instead of a real spawned process.
/// </summary>
internal interface ITrackedProcess : IDisposable
{
    /// <summary>Whether the host process has exited.</summary>
    bool HasExited { get; }

    /// <summary>The exit code, or -1 when it cannot be read.</summary>
    int ExitCode { get; }

    /// <summary>Raised when the observed process exits.</summary>
    event Action? Exited;

    /// <summary>Begins exit observation; must be called once after registration.</summary>
    void StartObserving();
}

/// <summary>Default adapter over a live <see cref="System.Diagnostics.Process"/>.</summary>
internal sealed class SystemTrackedProcess : ITrackedProcess
{
    private readonly Process process;
    private bool observing;

    public SystemTrackedProcess(Process process)
    {
        this.process = process;
    }

    public bool HasExited
    {
        get
        {
            try
            {
                return process.HasExited;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ObjectDisposedException)
            {
                return true;
            }
        }
    }

    public int ExitCode
    {
        get
        {
            try
            {
                return process.ExitCode;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ObjectDisposedException)
            {
                return -1;
            }
        }
    }

    public event Action? Exited;

    public void StartObserving()
    {
        if (observing)
        {
            return;
        }

        observing = true;
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += OnExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ObjectDisposedException)
        {
            // Exit observation is best-effort; the name-scan fallback stays authoritative.
        }
    }

    public void Dispose()
    {
        try
        {
            process.Exited -= OnExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ObjectDisposedException)
        {
        }

        try
        {
            process.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ObjectDisposedException)
        {
        }
    }

    private void OnExited(object? sender, EventArgs e) => Exited?.Invoke();
}
