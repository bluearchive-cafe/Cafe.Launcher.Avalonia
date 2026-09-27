using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Cafe.Launcher.Avalonia.UI;

/// <summary>
/// The presentation layer's lifecycle façade. Its callbacks are installed by
/// <c>AddLauncherPresentation</c>; the WinExe deals only with this small
/// lifecycle vocabulary instead of resolving view models or tray services.
/// </summary>
public sealed class LauncherPresentationSession : IDisposable
{
    private readonly LauncherPresentationCallbacks callbacks;
    private bool disposed;

    public LauncherPresentationSession(LauncherPresentationCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(callbacks);
        this.callbacks = callbacks;
    }

    /// <summary>Creates the one desktop window owned by this presentation session.</summary>
    public Window CreateMainWindow()
    {
        ThrowIfDisposed();
        return callbacks.CreateMainWindow();
    }

    /// <summary>Runs normal or first-launch initialization after the window opens.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return callbacks.InitializeAsync(cancellationToken);
    }

    /// <summary>Brings the existing window to the foreground after a forwarded launch.</summary>
    public void ShowWindow()
    {
        ThrowIfDisposed();
        callbacks.ShowWindow();
    }

    /// <summary>Routes a forwarded game-launch request through the regular UI journey.</summary>
    public Task LaunchGameAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return callbacks.LaunchGameAsync(cancellationToken);
    }

    /// <summary>Completes UI-owned shutdown work before the host disposes its container.</summary>
    public Task PrepareForShutdownAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return callbacks.PrepareForShutdownAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        callbacks.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}

/// <summary>
/// UI-owned implementation callbacks for <see cref="LauncherPresentationSession"/>.
/// It is deliberately the only bridge from the host into concrete Avalonia
/// presentation types while the application is migrated in source batches.
/// </summary>
public sealed record LauncherPresentationCallbacks(
    Func<Window> CreateMainWindow,
    Func<CancellationToken, Task> InitializeAsync,
    Action ShowWindow,
    Func<CancellationToken, Task> LaunchGameAsync,
    Func<CancellationToken, Task> PrepareForShutdownAsync,
    Action Dispose);
