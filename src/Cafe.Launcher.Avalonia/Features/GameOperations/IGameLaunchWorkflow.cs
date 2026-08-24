using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>Starts the game after applying launcher validation rules.</summary>
internal interface IGameLaunchWorkflow
{
    /// <summary>Runs launch validation and starts the game when allowed.</summary>
    Task<GameLaunchResult> StartGameAsync(LauncherStatusSnapshot snapshot);
}
