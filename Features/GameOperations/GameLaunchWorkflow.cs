using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>Adapts the game launch service to the presentation workflow.</summary>
internal sealed class GameLaunchWorkflow(GameLaunchService service) : IGameLaunchWorkflow
{
    /// <inheritdoc />
    public Task<GameLaunchResult> StartGameAsync(LauncherStatusSnapshot snapshot) =>
        service.StartAsync(snapshot);
}
