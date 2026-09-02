using System.IO;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>Structured reason a game launch target could not be resolved.</summary>
public enum GameLaunchTargetStatus
{
    Resolved,
    ExecutableNameEmpty,
    ExecutableNameInvalid,
    ExecutableMissing
}

/// <summary>
/// The start target shared by game launch and shortcut creation: a locally named
/// executable that exists on disk, its working directory, and its arguments.
/// </summary>
public sealed record GameLaunchTarget(
    string ExecutableName,
    string ExecutablePath,
    string WorkingDirectory,
    System.Collections.Generic.IReadOnlyList<string> Arguments);

/// <summary>Outcome of resolving the game launch target from a status snapshot.</summary>
public sealed record GameLaunchTargetResolution(
    GameLaunchTargetStatus Status,
    GameLaunchTarget? Target = null,
    string ExpectedExecutablePath = "")
{
    public bool Resolved => Status == GameLaunchTargetStatus.Resolved;

    /// <summary>
    /// Resolves the start target from the local installation state alone. The
    /// remote game config is deliberately not consulted: a target is startable
    /// only when the local installation names an executable that exists on disk,
    /// so a shortcut can never point at an installation the launch flow would
    /// reject.
    /// </summary>
    public static GameLaunchTargetResolution Resolve(LauncherStatusSnapshot snapshot)
    {
        var localGame = snapshot.LocalGame;
        var gamePath = localGame.GamePath;
        var executableName = localGame.GameConfig?.Name;
        if (string.IsNullOrWhiteSpace(gamePath) || string.IsNullOrWhiteSpace(executableName))
        {
            return new GameLaunchTargetResolution(GameLaunchTargetStatus.ExecutableNameEmpty);
        }

        // Defense-in-depth: reject executable names containing path separators.
        if (executableName.Contains('/') || executableName.Contains('\\'))
        {
            return new GameLaunchTargetResolution(GameLaunchTargetStatus.ExecutableNameInvalid);
        }

        var executablePath = Path.Combine(gamePath, $"{executableName}.exe");
        if (!File.Exists(executablePath))
        {
            return new GameLaunchTargetResolution(
                GameLaunchTargetStatus.ExecutableMissing,
                ExpectedExecutablePath: executablePath);
        }

        return new GameLaunchTargetResolution(
            GameLaunchTargetStatus.Resolved,
            new GameLaunchTarget(
                executableName,
                executablePath,
                gamePath,
                localGame.GameConfig?.Params ?? []));
    }
}
