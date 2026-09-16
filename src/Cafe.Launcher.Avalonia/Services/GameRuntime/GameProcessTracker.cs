using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>Default <see cref="IGameProcessTracker"/> over live Process handles.</summary>
public sealed class GameProcessTracker : IGameProcessTracker
{
    private readonly object gate = new();
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<string>>> exeRunningProbe;
    private readonly Func<Process, ITrackedProcess> processAdapter;

    private ITrackedProcess? trackedProcess;
    private Action? trackedProcessExitedHandler;
    private string trackedProcessName = "";
    private string trackedRunnerId = "";
    private DateTimeOffset startedAt;
    private GameLaunchExitInfo? lastExit;

    public GameProcessTracker()
        : this(ProcessService.FindRunningExeNamesAsync)
    {
    }

    internal GameProcessTracker(Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<string>>> exeRunningProbe)
        : this(exeRunningProbe, static process => new SystemTrackedProcess(process))
    {
    }

    /// <summary>
    /// Test seam: the exit-observation sequence runs against an injected
    /// <see cref="ITrackedProcess"/> so register→exit→duration timing can be
    /// verified without spawning a real process.
    /// </summary>
    internal GameProcessTracker(
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<string>>> exeRunningProbe,
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
            trackedProcessName = ProcessService.TryReadProcessName(process.HostProcess);
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

    public async Task<IReadOnlyList<string>> FindRunningGameProcessesAsync(
        IReadOnlyList<string> knownExeNames,
        CancellationToken cancellationToken = default)
    {
        var scanned = await exeRunningProbe(knownExeNames, cancellationToken).ConfigureAwait(false);
        if (scanned.Count > 0 || !HasLiveTrackedProcess)
        {
            return scanned;
        }

        // 句柄还活着但名字扫描没看见：宿主进程在本会话里由我们启动，句柄比扫描权威。
        var name = trackedProcessName.Length > 0
            ? trackedProcessName
            : knownExeNames.Count > 0 ? knownExeNames[0] : "";
        return name.Length > 0 ? [name] : [];
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
