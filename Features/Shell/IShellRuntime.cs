using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.Shell;

/// <summary>
/// The shell runtime interface used by the window presentation and its tests.
/// </summary>
public interface IShellRuntime : IDisposable
{
    event Action? PresentationChanged;
    event Action? StatusDetailModeChanged;

    bool IsBusy { get; }
    bool IsMotionReduced { get; }
    Task PendingStartupUpdateCheck { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task RefreshAsync(CancellationToken cancellationToken = default);
    void RefreshSystemMotionPreference();
    Task HandleOperationsRefreshRequestedAsync(GameOperationsRefreshMode mode);
    bool TryHandleEscape();
}
