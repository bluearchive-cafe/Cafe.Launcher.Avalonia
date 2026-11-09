using System.Threading.Tasks;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>Read-only presentation context plus a typed output stream; UI never decides operation eligibility.</summary>
internal interface IGameOperationJourneyHost
{
    /// <summary>Gets whether presentation currently blocks starting another operation.</summary>
    bool IsBusy { get; }
    /// <summary>Gets the latest applied snapshot for refresh outcomes.</summary>
    LauncherStatusSnapshot? CurrentSnapshot { get; }
    /// <summary>Renders a typed fact; eligibility and workflow ordering belong to the journey.</summary>
    void Present(GameOperationPresentation update);
    /// <summary>Requests shell refresh and reports whether it was handled.</summary>
    Task<bool> RefreshAsync(GameOperationsRefreshMode mode);
    /// <summary>Opens the shared log surface.</summary>
    Task ShowLogViewerAsync();
}

/// <summary>Presentation facts emitted by an operation journey; each case has its own required payload.</summary>
internal abstract record GameOperationPresentation
{
    public sealed record Busy(bool Value) : GameOperationPresentation;
    public sealed record Preparing : GameOperationPresentation;
    public sealed record Progress(GameOperationProgress Value) : GameOperationPresentation;
    public sealed record Snapshot(LauncherStatusSnapshot Value) : GameOperationPresentation;
    public sealed record LaunchCheck(string Message) : GameOperationPresentation;
    public sealed record RepairConfirmation(string Message) : GameOperationPresentation;
    public sealed record UninstallConfirmation(long RequestId, LauncherStatusSnapshot Value) : GameOperationPresentation;
    public sealed record UninstallSize(long RequestId, UninstallFootprint Value) : GameOperationPresentation;
    public sealed record UninstallExecuting : GameOperationPresentation;
    public sealed record UninstallCompleted(GameOperationResult Value) : GameOperationPresentation;
    public sealed record Window(GameOperationWindowAction Action) : GameOperationPresentation;
}

internal enum GameOperationWindowAction { Minimize, Exit, Show }
