using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class GameLaunchService
{
    private readonly ManifestValidationService manifestValidationService;
    private readonly ClickCodeService clickCodeService;

    public GameLaunchService(
        ManifestValidationService manifestValidationService,
        ClickCodeService clickCodeService)
    {
        this.manifestValidationService = manifestValidationService;
        this.clickCodeService = clickCodeService;
    }

    public async Task<GameLaunchResult> StartAsync(
        LauncherStatusSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (!snapshot.IsInstalled)
        {
            return Failed("Game is not installed.");
        }

        if (snapshot.BelowLowestVersion)
        {
            return Failed("Game version is below the required lowest version.");
        }

        var localGame = snapshot.LocalGame;
        var gameConfig = localGame.GameConfig;
        if (string.IsNullOrWhiteSpace(gameConfig?.Name))
        {
            return Failed("Game executable name is empty.");
        }

        var exePath = Path.Combine(localGame.GamePath, $"{gameConfig.Name}.exe");
        if (!File.Exists(exePath))
        {
            return Failed($"Game executable does not exist: {exePath}");
        }

        var validation = await manifestValidationService.ValidateAsync(
            localGame.GamePath,
            localGame,
            snapshot.Settings.LaunchCheckMode,
            snapshot.Settings.PatchUrlGroup,
            cancellationToken);

        if (!validation.Success)
        {
            return new GameLaunchResult
            {
                Success = false,
                Message = validation.Message,
                Validation = validation
            };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = localGame.GamePath,
            UseShellExecute = false
        };

        foreach (var parameter in gameConfig.Params)
        {
            startInfo.ArgumentList.Add(parameter);
        }

        // Write clickCode attribution to game directory before launch
        clickCodeService.WriteClickCodeToGamePath(localGame.GamePath);

        var process = Process.Start(startInfo);
        return process is null
            ? Failed("Game process did not start.")
            : new GameLaunchResult
            {
                Success = true,
                Message = "Game process started.",
                Validation = validation
            };
    }

    private static GameLaunchResult Failed(string message)
    {
        return new GameLaunchResult
        {
            Success = false,
            Message = message,
            Validation = new ManifestValidationResult
            {
                Success = false,
                Message = message
            }
        };
    }
}
