using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Core.Services.GameRuntime;

/// <summary>Default <see cref="IGameProcessTracker"/> over live Process handles; disposal releases observation only.</summary>
internal sealed class GameProcessTracker : IGameProcessTracker, IDisposable
{
    private readonly object gate = new();
    private readonly Func<RunningGameQuery, CancellationToken, Task<IReadOnlyList<string>>> exeRunningProbe;
    private readonly Func<Process, ITrackedProcess> processAdapter;

    private ITrackedProcess? trackedProcess;
    private Action? trackedProcessExitedHandler;
    private string trackedProcessName = "";
    private string trackedRunnerId = "";
    private DateTimeOffset startedAt;
    private GameLaunchExitInfo? lastExit;
    private bool disposed;

    public GameProcessTracker()
        : this(ProcessService.FindRunningGameProcessesAsync)
    {
    }

    internal GameProcessTracker(Func<RunningGameQuery, CancellationToken, Task<IReadOnlyList<string>>> exeRunningProbe)
        : this(exeRunningProbe, static process => new SystemTrackedProcess(process))
    {
    }

    /// <summary>
    /// Test seam: the exit-observation sequence runs against an injected
    /// <see cref="ITrackedProcess"/> so register→exit→duration timing can be
    /// verified without spawning a real process.
    /// </summary>
    internal GameProcessTracker(
        Func<RunningGameQuery, CancellationToken, Task<IReadOnlyList<string>>> exeRunningProbe,
        Func<Process, ITrackedProcess> processAdapter)
    {
        this.exeRunningProbe = exeRunningProbe;
        this.processAdapter = processAdapter;
    }

    public void Register(GameProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }

        var tracked = processAdapter(process.HostProcess);
        Action? exitedHandler = null;
        var exitNotificationPending = false;
        exitedHandler = () =>
        {
            // StartObserving can invoke its callback synchronously while Register owns gate.
            // In that case Register publishes the captured exit only after releasing gate.
            var deferNotification = Monitor.IsEntered(gate);
            if (!CaptureExit(tracked, exitedHandler!))
            {
                return;
            }

            if (deferNotification)
            {
                exitNotificationPending = true;
            }
            else
            {
                TrackedProcessExited?.Invoke();
            }
        };
        ITrackedProcess? previous = null;
        Action? previousExitedHandler = null;
        var adopted = false;
        try
        {
            lock (gate)
            {
                // Adapter creation is outside the lock. A concurrent Dispose may have won;
                // in that case this registration still owns and must release the new wrapper.
                ObjectDisposedException.ThrowIf(disposed, this);
                previous = trackedProcess;
                previousExitedHandler = trackedProcessExitedHandler;
                trackedProcess = tracked;
                trackedProcessExitedHandler = exitedHandler;
                adopted = true;
                trackedProcessName = ProcessService.TryReadProcessName(process.HostProcess);
                trackedRunnerId = process.RunnerId;
                startedAt = DateTimeOffset.Now;
                // LastExit 归属当前被跟踪的进程：重开即清零。订阅方（会话看护）据此把
                // 「Register 与订阅之间已退出」读成一次即时终态，而不是上一局的旧账。
                lastExit = null;
                try
                {
                    tracked.Exited += exitedHandler;
                    tracked.StartObserving();
                    // StartObserving may synchronously report exit and release the wrapper.
                    if (ReferenceEquals(trackedProcess, tracked) && tracked.HasExited)
                    {
                        exitedHandler();
                    }
                }
                catch
                {
                    if (ReferenceEquals(trackedProcess, tracked))
                    {
                        trackedProcess = null;
                        trackedProcessExitedHandler = null;
                        ReleaseTracking(tracked, exitedHandler);
                    }

                    throw;
                }
            }
        }
        finally
        {
            if (!adopted)
            {
                tracked.Dispose();
            }

            ReleaseTracking(previous, previousExitedHandler);
            if (exitNotificationPending)
            {
                TrackedProcessExited?.Invoke();
            }
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

    /// <inheritdoc />
    public event Action? TrackedProcessExited;

    public async Task<IReadOnlyList<string>> FindRunningGameProcessesAsync(
        RunningGameQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<string> scanned;
        try
        {
            scanned = await exeRunningProbe(query, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (ProcessService.IsEnumerationFailure(exception))
        {
            // 本接口服务于破坏性操作闸门：保留 ADR-032 的枚举失败放行策略，
            // 但已知仍存活的宿主继续优先。会话监控直接消费扫描器，不继承此策略。
            scanned = [];
        }
        if (scanned.Count > 0)
        {
            return scanned;
        }

        lock (gate)
        {
            if (trackedProcess is null || trackedProcess.HasExited)
            {
                return scanned;
            }

            // 句柄还活着但名字扫描没看见：宿主进程在本会话里由我们启动，句柄比扫描权威。
            var name = trackedProcessName.Length > 0
                ? trackedProcessName
                : query.KnownExeNames.Count > 0 ? query.KnownExeNames[0] : "";
            return name.Length > 0 ? [name] : [];
        }
    }

    private bool CaptureExit(ITrackedProcess process, Action exitedHandler)
    {
        lock (gate)
        {
            if (disposed || !ReferenceEquals(trackedProcess, process))
            {
                return false;
            }

            // Ownership must be checked before touching the wrapper: an already queued callback
            // may arrive after replacement or Dispose has released its process handle.
            var exitCode = process.ExitCode;
            var exitedAt = DateTimeOffset.Now;
            trackedProcess = null;
            trackedProcessExitedHandler = null;
            lastExit = new GameLaunchExitInfo(
                exitCode,
                exitedAt - startedAt,
                exitedAt,
                trackedRunnerId);
        }

        ReleaseTracking(process, exitedHandler);
        // A real exit already captured before Dispose may be in flight. The caller publishes
        // outside the ownership lock so subscribers can coordinate other threads.
        return true;
    }

    /// <summary>
    /// Stops observing and releases the owned process wrapper without terminating the game or
    /// reporting an exit. Idempotent; afterwards local tracking is empty, prior LastExit is retained,
    /// and process-name queries remain available for games still running outside this observation.
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            var process = trackedProcess;
            var exitedHandler = trackedProcessExitedHandler;
            trackedProcess = null;
            trackedProcessExitedHandler = null;
            trackedProcessName = "";
            trackedRunnerId = "";
            ReleaseTracking(process, exitedHandler);
        }
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
