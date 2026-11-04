using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Helpers;

namespace Cafe.Launcher.UI.Features.Shell;

/// <summary>
/// Owns shell refresh serialization and the after-load handshake on top of the shared
/// <see cref="ActivityLifetime"/>: one refresh at a time, lifetime cancellation, and the
/// drained-completion handshake. Host-state loading and after-load work are injected
/// callbacks, so the concurrency rules live in one small module instead of inside the shell
/// coordinator.
/// </summary>
internal sealed class ShellRefreshCoordinator : IDisposable
{
    private readonly ActivityLifetime lifetime = new();
    private readonly object afterLoadLock = new();
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly Func<CancellationToken, Task<bool>> loadHostStateAsync;
    private readonly Func<bool, CancellationToken, Task<Task>> afterLoadAsync;

    private Task pendingAfterLoadWork = Task.CompletedTask;
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
            lock (afterLoadLock)
            {
                return pendingAfterLoadWork;
            }
        }
    }

    /// <summary>Gets the lifetime token shared by all refresh work of this lifecycle.</summary>
    public CancellationToken LifetimeToken => lifetime.LifetimeToken;

    /// <summary>
    /// Reloads host state through the gate and, when loaded, runs the after-load
    /// work (startup update check, persisted-download resume) while retaining
    /// the outstanding after-load task for shutdown coordination.
    /// </summary>
    public async Task RefreshAsync(
        bool resumePersistedDownload,
        CancellationToken cancellationToken = default)
    {
        using var lease = lifetime.TryBegin();
        if (lease is null)
        {
            return;
        }

        using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lifetime.LifetimeToken);
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
            lock (afterLoadLock)
            {
                pendingAfterLoadWork = pending;
            }
        }
        catch (OperationCanceledException) when (refreshToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Marks the lifecycle as shutting down and returns the task that completes
    /// when every active refresh has finished.
    /// </summary>
    public Task BeginShutdown() => lifetime.BeginShutdown();

    /// <summary>Cancels the lifetime token so in-flight refresh work unwinds.</summary>
    public void CancelLifetime() => lifetime.Cancel();

    /// <summary>Waits for refresh work and the outstanding after-load task to settle.</summary>
    public async Task WaitForShutdownWorkAsync(Task pendingRefreshes)
    {
        await pendingRefreshes;
        await PendingAfterLoadWork;
    }

    public void Dispose()
    {
        lock (afterLoadLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        lifetime.Dispose();
        refreshGate.Dispose();
    }
}
