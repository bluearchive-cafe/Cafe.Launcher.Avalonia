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

        var runtime = snapshot.Settings.GameRuntime;
        var preferredRunnerId = runtime.Runner is GameRuntimeRunners.Auto or ""
            ? null
            : runtime.Runner;
        // Resolution and launch must share one options instance: availability checks
        // honor the configured runner/prefix/proton paths exactly like StartAsync does.
        var runtimeOptions = new GameRuntimeOptions(runtime.RunnerPath, runtime.PrefixPath, runtime.ProtonPath);
        var runnerResolution = await gameRunnerResolver
            .ResolveWithDiagnosticsAsync(preferredRunnerId, runtimeOptions, cancellationToken)
            .ConfigureAwait(false);
        var runner = runnerResolution.Runner;
        if (runner is null)
        {
            return Failed(localizer.T("gameProcessStartFailed"), runnerResolution.DiagnosticMessage);
        }

        // A stable runtime id decouples compatibility state (prefix layout, UMU
        // GAMEID) from the game executable name, so renaming the EXE cannot orphan
        // an existing environment.
        var request = new GameLaunchRequest(
            GameId: GameRuntimeIds.BlueArchiveJapan,
            ExecutablePath: exePath,
            WorkingDirectory: localGame.GamePath,
            Arguments: gameConfig.Params);

        GameProcess gameProcess;
        try
        {
            gameProcess = await runner
                .StartAsync(request, runtimeOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new GameLaunchResult
            {
                Success = false,
                Message = localizer.F("gameLaunchFailed", exception.Message),
                DiagnosticMessage =
                    $"{BuildLaunchContext(runner, runnerResolution.Availability, runnerResolution.DiagnosticMessage, request, runtimeOptions)}{Environment.NewLine}{exception.Message}",
                DiagnosticException = exception,
                Validation = validation
            };
        }

        // The tracker owns the host process from here: it keeps the handle alive
        // as the authoritative running-state source and records exit details.
        gameProcessTracker.Register(gameProcess);

        return new GameLaunchResult
        {
            Success = true,
            Message = localizer.T("gameProcessStarted"),
            DiagnosticMessage = BuildLaunchContext(
                runner,
                runnerResolution.Availability,
                runnerResolution.DiagnosticMessage,
                request,
                runtimeOptions),
            Validation = validation
        };
    }

    private static GameLaunchResult Failed(string message, string? diagnosticMessage = null)
    {
        return new GameLaunchResult
        {
            Success = false,
            Message = message,
            DiagnosticMessage = diagnosticMessage ?? message,
            Validation = new ManifestValidationResult
            {
                Success = false,
                Message = message
            }
        };
    }

    private string BuildLaunchContext(
        IGameRunner runner,
        GameRunnerAvailability? availability,
        string runnerSelectionDiagnostic,
        GameLaunchRequest request,
        GameRuntimeOptions runtimeOptions) =>
        $"{runnerSelectionDiagnostic}{Environment.NewLine}" +
        $"{localizer.T("executable")}: {request.ExecutablePath}{Environment.NewLine}" +
        $"{localizer.T("path")}: {request.WorkingDirectory}{Environment.NewLine}" +
        BuildDiagnosticSnapshot(runner, availability, request, runtimeOptions).Describe();

    private static GameRuntimeDiagnosticSnapshot BuildDiagnosticSnapshot(
        IGameRunner runner,
        GameRunnerAvailability? availability,
        GameLaunchRequest request,
        GameRuntimeOptions runtimeOptions) =>
        new(
            RunnerId: runner.Id,
            RunnerVersion: availability?.Version,
            RunnerExecutable: availability?.ExecutablePath,
            PrefixPath: runner.GetEffectivePrefixPath(request, runtimeOptions),
            ProtonPath: runner.GetEffectiveProtonPath(runtimeOptions),
            GameId: request.GameId,
            GameExecutable: request.ExecutablePath,
            WorkingDirectory: request.WorkingDirectory);
}
