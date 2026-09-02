using System;
using System.Linq;
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
    private readonly IGameRuntime gameRuntime;
    private readonly LocalizationService localizer;

    public GameLaunchService(
        ManifestValidationService manifestValidationService,
        ClickCodeService clickCodeService,
        IGameRuntime gameRuntime,
        LocalizationService localizer)
    {
        this.manifestValidationService = manifestValidationService;
        this.clickCodeService = clickCodeService;
        this.gameRuntime = gameRuntime;
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

        var targetResolution = GameLaunchTargetResolution.Resolve(snapshot);
        if (!targetResolution.Resolved)
        {
            return Failed(targetResolution.Status switch
            {
                GameLaunchTargetStatus.ExecutableNameInvalid => localizer.T("gameExecutableNameInvalid"),
                GameLaunchTargetStatus.ExecutableMissing => localizer.F(
                    "gameExecutableMissing", targetResolution.ExpectedExecutablePath),
                _ => localizer.T("gameExecutableNameEmpty")
            });
        }

        var target = targetResolution.Target!;

        var validation = await manifestValidationService.ValidateAsync(
            target.WorkingDirectory,
            snapshot.LocalGame,
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
        clickCodeService.WriteClickCodeToGamePath(target.WorkingDirectory);

        var runtimeConfiguration = GameRuntimeConfiguration.FromSettings(snapshot.Settings.GameRuntime);

        // A stable runtime id decouples compatibility state (prefix layout, UMU
        // GAMEID) from the game executable name, so renaming the EXE cannot orphan
        // an existing environment.
        var request = new GameLaunchRequest(
            GameId: GameRuntimeIds.BlueArchiveJapan,
            ExecutablePath: target.ExecutablePath,
            WorkingDirectory: target.WorkingDirectory,
            Arguments: target.Arguments);

        GameRuntimeLaunchResult launchResult;
        try
        {
            launchResult = await gameRuntime
                .LaunchAsync(request, runtimeConfiguration, cancellationToken)
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
                    $"{localizer.T("gameRuntimeRunner")}: {runtimeConfiguration.PreferredRunnerId ?? localizer.T("gameRuntimeRunnerAuto")}{Environment.NewLine}" +
                    $"{localizer.T("executable")}: {request.ExecutablePath}{Environment.NewLine}" +
                    $"{localizer.T("path")}: {request.WorkingDirectory}{Environment.NewLine}" +
                    $"{exception.Message}",
                DiagnosticException = exception,
                Validation = validation
            };
        }

        if (!launchResult.Success)
        {
            if (launchResult.FailureException is not null)
            {
                var exception = launchResult.FailureException;
                return new GameLaunchResult
                {
                    Success = false,
                    Message = localizer.F("gameLaunchFailed", exception.Message),
                    DiagnosticMessage =
                        $"{BuildLaunchContext(launchResult, request)}{Environment.NewLine}{exception.Message}",
                    DiagnosticException = exception,
                    Validation = validation
                };
            }

            return Failed(
                localizer.T("gameProcessStartFailed"),
                BuildRunnerSelectionFailure(launchResult, runtimeConfiguration.PreferredRunnerId));
        }

        return new GameLaunchResult
        {
            Success = true,
            Message = localizer.T("gameProcessStarted"),
            DiagnosticMessage = BuildLaunchContext(launchResult, request),
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

    private string BuildLaunchContext(GameRuntimeLaunchResult launchResult, GameLaunchRequest request)
    {
        var runnerLine = string.IsNullOrWhiteSpace(launchResult.RunnerId)
            ? $"{localizer.T("gameRuntimeRunner")}: {localizer.T("gameRuntimeRunnerAuto")}"
            : $"{localizer.T("gameRuntimeRunner")}: {launchResult.RunnerId}";
        var candidates = launchResult.Candidates.Count == 0
            ? ""
            : Environment.NewLine + string.Join(
                Environment.NewLine,
                launchResult.Candidates.Select(candidate =>
                    $"- {candidate.RunnerId}: {AvailabilityReason(candidate.Availability)}"));
        return runnerLine + candidates + Environment.NewLine +
            $"{localizer.T("executable")}: {request.ExecutablePath}{Environment.NewLine}" +
            $"{localizer.T("path")}: {request.WorkingDirectory}{Environment.NewLine}" +
            launchResult.Diagnostic.Describe();
    }

    private string BuildRunnerSelectionFailure(
        GameRuntimeLaunchResult launchResult,
        string? preferredRunnerId)
    {
        if (!string.IsNullOrWhiteSpace(preferredRunnerId))
        {
            if (launchResult.Candidates.Count == 0)
            {
                return $"{localizer.T("gameRuntimeRunner")}: {preferredRunnerId}{Environment.NewLine}" +
                    localizer.T("unknown");
            }

            var candidate = launchResult.Candidates[0];
            return $"{localizer.T("gameRuntimeRunner")}: {preferredRunnerId}{Environment.NewLine}" +
                $"{candidate.RunnerId}: {AvailabilityReason(candidate.Availability)}";
        }

        if (launchResult.Candidates.Count == 0)
        {
            return $"{localizer.T("gameRuntimeRunner")}: {localizer.T("gameRuntimeRunnerAuto")}{Environment.NewLine}" +
                localizer.T("unknown");
        }

        var details = string.Join(
            Environment.NewLine,
            launchResult.Candidates.Select(candidate =>
                $"- {candidate.RunnerId}: {AvailabilityReason(candidate.Availability)}"));
        return $"{localizer.T("gameRuntimeRunner")}: {localizer.T("gameRuntimeRunnerAuto")}{Environment.NewLine}{details}";
    }

    private string AvailabilityReason(GameRunnerAvailability availability) =>
        string.IsNullOrWhiteSpace(availability.Message)
            ? localizer.T("unknown")
            : string.IsNullOrWhiteSpace(availability.TechnicalDetail)
                ? availability.Message
                : $"{availability.Message}{Environment.NewLine}{availability.TechnicalDetail}";
}
