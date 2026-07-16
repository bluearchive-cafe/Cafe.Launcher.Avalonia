using System;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>Adapts the uninstall service to the presentation workflow.</summary>
internal sealed class GameUninstallWorkflow(GameUninstallService service) : IGameUninstallWorkflow
{
    /// <inheritdoc />
    public Task<GameOperationResult> ValidateUninstallAsync(string gamePath) =>
        service.ValidateAsync(gamePath);
    /// <inheritdoc />
    public Task<GameOperationResult> UninstallAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress) => service.UninstallAsync(snapshot, progress);
}
