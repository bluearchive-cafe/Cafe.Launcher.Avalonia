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
    private Action? trackedProcessExitedHandler;
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

        var tracked = processAdapter(process.HostProcess);
        Action? exitedHandler = null;
        exitedHandler = () => CaptureExit(tracked, exitedHandler!);
        ITrackedProcess? previous;
        Action? previousExitedHandler;
        lock (gate)
        {
            previous = trackedProcess;
            previousExitedHandler = trackedProcessExitedHandler;
            trackedProcess = tracked;
            trackedProcessExitedHandler = exitedHandler;
            trackedRunnerId = process.RunnerId;
            startedAt = DateTimeOffset.Now;
            tracked.Exited += exitedHandler;
            tracked.StartObserving();
        }

        ReleaseTracking(previous, previousExitedHandler);

        // The process may have exited between registration and observation hookup.
        if (tracked.HasExited)
        {
            CaptureExit(tracked, exitedHandler);
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

    private void CaptureExit(ITrackedProcess process, Action exitedHandler)
    {
        var exitCode = process.ExitCode;
        var exitedAt = DateTimeOffset.Now;

        lock (gate)
        {
            if (!ReferenceEquals(trackedProcess, process))
            {
                return;
            }

            trackedProcess = null;
            trackedProcessExitedHandler = null;
            lastExit = new GameLaunchExitInfo(
                exitCode,
                exitedAt - startedAt,
                exitedAt,
                trackedRunnerId);
        }

        ReleaseTracking(process, exitedHandler);
    }

    private static void ReleaseTracking(ITrackedProcess? process, Action? exitedHandler)
    {
        if (process is null)
        {
            return;
        }

        if (exitedHandler is not null)
        {
            process.Exited -= exitedHandler;
        }

        process.Dispose();
    }
}
