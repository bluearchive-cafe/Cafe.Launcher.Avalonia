using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

public sealed class GameLaunchService
{
    private readonly ManifestValidationService manifestValidationService;
    private readonly ClickCodeService clickCodeService;
    private readonly GameRunnerResolver gameRunnerResolver;
    private readonly IGameProcessTracker gameProcessTracker;
    private readonly LocalizationService localizer;

    public GameLaunchService(
        ManifestValidationService manifestValidationService,
        ClickCodeService clickCodeService,
        GameRunnerResolver gameRunnerResolver,
        IGameProcessTracker gameProcessTracker,
        LocalizationService localizer)
    {
        this.manifestValidationService = manifestValidationService;
        this.clickCodeService = clickCodeService;
        this.gameRunnerResolver = gameRunnerResolver;
        this.gameProcessTracker = gameProcessTracker;
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
                LauncherRuntimeState.Corrupted => localizer.T("gameCorruptedInstallationState"),
                LauncherRuntimeState.IoFailure => localizer.T("gameInstallationStateReadFailed"),
                LauncherRuntimeState.RemoteUnavailable => localizer.T("gameRemoteStateUnavailable"),
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

        // Write clickCode attribution to game directory before launch
        clickCodeService.WriteClickCodeToGamePath(localGame.GamePath);

        var runner = await gameRunnerResolver
            .ResolveAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (runner is null)
        {
            return Failed(localizer.T("gameProcessStartFailed"));
        }

        var request = new GameLaunchRequest(
            GameId: gameConfig.Name,
            ExecutablePath: exePath,
            WorkingDirectory: localGame.GamePath,
            Arguments: gameConfig.Params);

        var gameProcess = await runner
            .StartAsync(request, new GameRuntimeOptions(), cancellationToken)
            .ConfigureAwait(false);

        // The tracker owns the host process from here: it keeps the handle alive
        // as the authoritative running-state source and records exit details.
        gameProcessTracker.Register(gameProcess);

        return new GameLaunchResult
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
