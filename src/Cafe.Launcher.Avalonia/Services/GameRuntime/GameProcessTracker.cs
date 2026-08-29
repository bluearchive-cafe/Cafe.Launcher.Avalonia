using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>Default <see cref="IGameProcessTracker"/> over live Process handles.</summary>
public sealed class GameProcessTracker : IGameProcessTracker
{
    private readonly object gate = new();
    private readonly Func<string, CancellationToken, Task<bool>> exeRunningProbe;

    private Process? trackedProcess;
    private string trackedRunnerId = "";
    private DateTimeOffset startedAt;
    private GameLaunchExitInfo? lastExit;

    public GameProcessTracker()
        : this(ProcessService.IsExeRunningAsync)
    {
    }

    internal GameProcessTracker(Func<string, CancellationToken, Task<bool>> exeRunningProbe)
    {
        this.exeRunningProbe = exeRunningProbe;
    }

    public void Register(GameProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        lock (gate)
        {
            trackedProcess = process.HostProcess;
            trackedRunnerId = process.RunnerId;
            startedAt = DateTimeOffset.Now;
        }

        TryObserveExit(process.HostProcess);
    }

    public bool HasLiveTrackedProcess
    {
        get
        {
            lock (gate)
            {
                return trackedProcess is not null && IsProcessAlive(trackedProcess);
            }
        }
    }

    public GameLaunchExitInfo? LastExit
    {
        get
        {
            lock (gate)
            {
                return lastExit;
            }
        }
    }

    public async Task<bool> IsGameRunningAsync(string exeName, CancellationToken cancellationToken = default)
    {
        if (HasLiveTrackedProcess)
        {
            return true;
        }

        return await exeRunningProbe(exeName, cancellationToken).ConfigureAwait(false);
    }

    private void TryObserveExit(Process process)
    {
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += OnTrackedProcessExited;

            // The process may have exited between registration and event hookup.
            if (process.HasExited)
            {
                CaptureExit(process);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ObjectDisposedException)
        {
            // Exit observation is best-effort; the name-scan fallback stays authoritative.
        }
    }

    private void OnTrackedProcessExited(object? sender, EventArgs e)
    {
        if (sender is Process process)
        {
            CaptureExit(process);
        }
    }

    private void CaptureExit(Process process)
    {
        var exitCode = TryReadExitCode(process);
        var exitedAt = DateTimeOffset.Now;

        lock (gate)
        {
            if (!ReferenceEquals(trackedProcess, process))
            {
                return;
            }

            trackedProcess = null;
            lastExit = new GameLaunchExitInfo(
                exitCode,
                exitedAt - startedAt,
                exitedAt,
                trackedRunnerId);
        }

        try
        {
            process.Exited -= OnTrackedProcessExited;
            process.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ObjectDisposedException)
        {
            // Already torn down by the runtime; nothing to clean up.
        }
    }

    private static int TryReadExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ObjectDisposedException)
        {
            // e.g. terminated by a signal on Unix — the exact code is unknowable.
            return -1;
        }
    }

    private static bool IsProcessAlive(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ObjectDisposedException)
        {
            return false;
        }
    }
}
