using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// Presentation sink driven by the game operation journey.
/// Implemented by the view model so the journey stays decoupled from the shell.
/// </summary>
internal interface IGameOperationJourneyHost
{
    bool IsBusy { get; }
    void PrepareOperation();
    void ApplyProgress(GameOperationProgress progress);
    void ApplySnapshot(LauncherStatusSnapshot snapshot);
    void SetOperationNote(string note);
    void SetBusy(bool busy);
}
