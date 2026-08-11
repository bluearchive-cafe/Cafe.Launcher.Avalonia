using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// Presentation sink driven by the game operation journey.
/// Implemented by the view model so the journey stays decoupled from the shell.
/// </summary>
internal interface IGameOperationJourneyHost
{
    /// <summary>Gets whether the host is currently presenting a busy operation.</summary>
    bool IsBusy { get; }

    /// <summary>Resets the host before an operation begins.</summary>
    void PrepareOperation();
    /// <summary>Applies a progress update to the presentation state.</summary>
    void ApplyProgress(GameOperationProgress progress);
    /// <summary>Applies the latest launcher status snapshot.</summary>
    void ApplySnapshot(LauncherStatusSnapshot snapshot);
    /// <summary>Sets the user-facing operation note.</summary>
    void SetOperationNote(string note);
    /// <summary>Sets whether the host should present a busy state.</summary>
    void SetBusy(bool busy);
}
