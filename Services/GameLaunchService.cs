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
    private readonly LocalizationService localizer;

    public GameLaunchService(
        ManifestValidationService manifestValidationService,
        ClickCodeService clickCodeService,
        LocalizationService localizer)
    {
        this.manifestValidationService = manifestValidationService;
        this.clickCodeService = clickCodeService;
        this.localizer = localizer;
    }

    public async Task<GameLaunchResult> StartAsync(
        LauncherStatusSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.RuntimeState == LauncherRuntimeState.NotInstalled)
        {
            return Failed(localizer.T("gameNotInstalled"));
        }

        if (snapshot.RuntimeState == LauncherRuntimeState.BelowLowestVersion)
        {
            return Failed(localizer.T("gameBelowLowestVersion"));
        }

        if (snapshot.RuntimeState != LauncherRuntimeState.Ready)
        {
            return Failed(snapshot.RuntimeState switch
            {
                LauncherRuntimeState.Corrupted => localizer.T("corruptedInstallationState"),
                LauncherRuntimeState.IoFailure => localizer.T("installationStateReadFailed"),
                LauncherRuntimeState.RemoteUnavailable => localizer.T("remoteStateUnavailable"),
                LauncherRuntimeState.UpdateAvailable => localizer.T("updateAvailable"),
                _ => localizer.T("gameNotInstalled")
            });
        }

        var localGame = snapshot.LocalGame;
        var gameConfig = localGame.GameConfig;
        if (string.IsNullOrWhiteSpace(gameConfig?.Name))
        {
            return Failed(localizer.T("gameExecutableNameEmpty"));
        }

        // Defense-in-depth: reject executable names containing path separators
        if (gameConfig.Name.Contains('/') || gameConfig.Name.Contains('\\'))
        {
            return Failed(localizer.T("gameExecutableNameInvalid"));
        }

        var exePath = Path.Combine(localGame.GamePath, $"{gameConfig.Name}.exe");
        if (!File.Exists(exePath))
        {
            return Failed(localizer.F("gameExecutableMissing", exePath));
        }

        var validation = await manifestValidationService.ValidateAsync(
            localGame.GamePath,
            localGame,
            snapshot.Settings.LaunchCheckMode,
            snapshot.Settings.PatchUrlGroup,
            snapshot.Settings.ProxyMode,
            cancellationToken).ConfigureAwait(false);

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

        using var process = Process.Start(startInfo);
        return process is null
            ? Failed(localizer.T("gameProcessStartFailed"))
            : new GameLaunchResult
            {
                Success = true,
                Message = localizer.T("gameProcessStarted"),
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
