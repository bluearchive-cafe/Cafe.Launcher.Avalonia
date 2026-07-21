using System;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>Validates and executes managed game uninstallation.</summary>
internal interface IGameUninstallWorkflow
{
    /// <summary>Validates whether the supplied path can be uninstalled.</summary>
    Task<GameOperationResult> ValidateUninstallAsync(string gamePath);

    /// <summary>Uninstalls files managed by the launcher.</summary>
    Task<GameOperationResult> UninstallAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress);
}
