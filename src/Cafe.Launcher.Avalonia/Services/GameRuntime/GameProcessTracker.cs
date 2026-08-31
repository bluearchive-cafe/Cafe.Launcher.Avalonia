using System;
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
    private readonly Func<Process, ITrackedProcess> processAdapter;

    private ITrackedProcess? trackedProcess;
    private string trackedRunnerId = "";
    private DateTimeOffset startedAt;
    private GameLaunchExitInfo? lastExit;

    public GameProcessTracker()
        : this(ProcessService.IsExeRunningAsync)
    {
    }

    internal GameProcessTracker(Func<string, CancellationToken, Task<bool>> exeRunningProbe)
        : this(exeRunningProbe, static process => new SystemTrackedProcess(process))
    {
    }

    /// <summary>
    /// Test seam: the exit-observation sequence runs against an injected
    /// <see cref="ITrackedProcess"/> so register→exit→duration timing can be
    /// verified without spawning a real process.
    /// </summary>
    internal GameProcessTracker(
        Func<string, CancellationToken, Task<bool>> exeRunningProbe,
        Func<Process, ITrackedProcess> processAdapter)
    {
        this.exeRunningProbe = exeRunningProbe;
        this.processAdapter = processAdapter;
    }

    public void Register(GameProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        ITrackedProcess tracked;
        lock (gate)
        {
            tracked = processAdapter(process.HostProcess);
            trackedProcess = tracked;
            trackedRunnerId = process.RunnerId;
            startedAt = DateTimeOffset.Now;
        }

        tracked.Exited += OnTrackedProcessExited;
        tracked.StartObserving();

        // The process may have exited between registration and observation hookup.
        if (tracked.HasExited)
        {
            CaptureExit(tracked);
        }
    }

    public bool HasLiveTrackedProcess
    {
        get
        {
            lock (gate)
            {
                return trackedProcess is not null && !trackedProcess.HasExited;
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

    private void OnTrackedProcessExited()
    {
        ITrackedProcess? tracked;
        lock (gate)
        {
            tracked = trackedProcess;
        }

        CaptureExit(tracked);
    }

    private void CaptureExit(ITrackedProcess? process)
    {
        if (process is null)
        {
            return;
        }

        var exitCode = process.ExitCode;
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

        process.Exited -= OnTrackedProcessExited;
        process.Dispose();
    }
}
