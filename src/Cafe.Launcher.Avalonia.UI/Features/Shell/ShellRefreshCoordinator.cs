using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Features.Shell;

/// <summary>
/// Owns shell refresh serialization and lifetime drainage: one refresh at a
/// time, refresh counting for shutdown, the lifetime cancellation token, and
/// the drained-completion handshake. Host-state loading and after-load work
/// are injected callbacks, so the concurrency rules live in one small module
/// instead of inside the shell coordinator.
/// </summary>
internal sealed class ShellRefreshCoordinator : IDisposable
{
    private readonly object lifetimeLock = new();
    private readonly CancellationTokenSource lifetimeCts = new();
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly Func<CancellationToken, Task<bool>> loadHostStateAsync;
    private readonly Func<bool, CancellationToken, Task<Task>> afterLoadAsync;

    private TaskCompletionSource? refreshesDrained;
    private Task pendingAfterLoadWork = Task.CompletedTask;
    private int activeRefreshCount;
    private bool shutdownRequested;
    private bool disposed;

    /// <summary>Initializes the coordinator with the host-state and after-load callbacks.</summary>
    public ShellRefreshCoordinator(
        Func<CancellationToken, Task<bool>> loadHostStateAsync,
        Func<bool, CancellationToken, Task<Task>> afterLoadAsync)
    {
        this.loadHostStateAsync = loadHostStateAsync;
        this.afterLoadAsync = afterLoadAsync;
    }

    /// <summary>Gets the after-load work (startup update check) awaited during shutdown.</summary>
    internal Task PendingAfterLoadWork
    {
        get
        {
            lock (lifetimeLock)
            {
                return pendingAfterLoadWork;
            }
        }
    }

    /// <summary>Gets the lifetime token shared by all refresh work of this lifecycle.</summary>
    public CancellationToken LifetimeToken
    {
        get
        {
            lock (lifetimeLock)
            {
                return lifetimeCts.Token;
            }
        }
    }

    /// <summary>
    /// Reloads host state through the gate and, when loaded, runs the after-load
    /// work (startup update check, persisted-download resume) while retaining
    /// the outstanding after-load task for shutdown coordination.
    /// </summary>
    public async Task RefreshAsync(
        bool resumePersistedDownload,
        CancellationToken cancellationToken = default)
    {
        CancellationToken lifetimeToken;
        lock (lifetimeLock)
        {
            if (shutdownRequested || disposed)
            {
                return;
            }

            activeRefreshCount++;
            lifetimeToken = lifetimeCts.Token;
        }

        using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lifetimeToken);
        var refreshToken = refreshCts.Token;
        var loaded = false;
        var gateEntered = false;
        try
        {
            try
            {
                await refreshGate.WaitAsync(refreshToken);
                gateEntered = true;
                loaded = await loadHostStateAsync(refreshToken);
            }
            finally
            {
                if (gateEntered)
                {
                    refreshGate.Release();
                }
            }

            if (!loaded || refreshToken.IsCancellationRequested)
            {
                return;
            }

            var pending = await afterLoadAsync(resumePersistedDownload, refreshToken);
            lock (lifetimeLock)
            {
                pendingAfterLoadWork = pending;
            }
        }
        catch (OperationCanceledException) when (refreshToken.IsCancellationRequested)
        {
        }
        finally
        {
            CompleteRefresh();
        }
    }

    /// <summary>
    /// Marks the lifecycle as shutting down and returns the task that completes
    /// when every active refresh has finished.
    /// </summary>
    public Task BeginShutdown()
    {
        lock (lifetimeLock)
        {
            shutdownRequested = true;
            if (activeRefreshCount == 0)
            {
                return Task.CompletedTask;
            }

            refreshesDrained ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return refreshesDrained.Task;
        }
    }

    /// <summary>Cancels the lifetime token so in-flight refresh work unwinds.</summary>
    public void CancelLifetime() => lifetimeCts.Cancel();

    /// <summary>Waits for refresh work and the outstanding after-load task to settle.</summary>
    public async Task WaitForShutdownWorkAsync(Task pendingRefreshes)
    {
        await pendingRefreshes;
        await PendingAfterLoadWork;
    }

    public void Dispose()
    {
        DisposeLifetimeResources();
    }

    private void CompleteRefresh()
    {
        TaskCompletionSource? drained;
        lock (lifetimeLock)
        {
            activeRefreshCount--;
            drained = activeRefreshCount == 0 ? refreshesDrained : null;
        }

        drained?.TrySetResult();
    }

    private void DisposeLifetimeResources()
    {
        lock (lifetimeLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        lifetimeCts.Dispose();
        refreshGate.Dispose();
    }
}
