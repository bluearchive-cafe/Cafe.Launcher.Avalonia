using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
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
            return Failed(localizer.T(LocalizationKeys.GameNotInstalled));
        }

        if (snapshot.RuntimeState == LauncherRuntimeState.BelowLowestVersion)
        {
            return Failed(localizer.T(LocalizationKeys.GameBelowLowestVersion));
        }

        if (snapshot.RuntimeState != LauncherRuntimeState.Ready)
        {
            return Failed(snapshot.RuntimeState switch
            {
                LauncherRuntimeState.Corrupted => localizer.T(LocalizationKeys.GameCorruptedInstallationState),
                LauncherRuntimeState.IoFailure => localizer.T(LocalizationKeys.GameInstallationStateReadFailed),
                LauncherRuntimeState.RemoteUnavailable => localizer.T(LocalizationKeys.GameRemoteStateUnavailable),
                LauncherRuntimeState.UpdateAvailable => localizer.T(LocalizationKeys.UpdateAvailable),
                _ => localizer.T(LocalizationKeys.GameNotInstalled)
            });
        }

        var targetResolution = GameLaunchTargetResolution.Resolve(snapshot);
        if (!targetResolution.Resolved)
        {
            return Failed(targetResolution.Status switch
            {
                GameLaunchTargetStatus.ExecutableNameInvalid => localizer.T(LocalizationKeys.GameExecutableNameInvalid),
                GameLaunchTargetStatus.ExecutableMissing => localizer.F(
                    LocalizationKeys.GameExecutableMissing, targetResolution.ExpectedExecutablePath),
                _ => localizer.T(LocalizationKeys.GameExecutableNameEmpty)
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
                Message = localizer.F(LocalizationKeys.GameLaunchFailed, exception.Message),
                DiagnosticMessage =
                    $"{localizer.T(LocalizationKeys.GameRuntimeRunner)}: {runtimeConfiguration.PreferredRunnerId ?? localizer.T(LocalizationKeys.GameRuntimeRunnerAuto)}{Environment.NewLine}" +
                    $"{localizer.T(LocalizationKeys.Executable)}: {request.ExecutablePath}{Environment.NewLine}" +
                    $"{localizer.T(LocalizationKeys.Path)}: {request.WorkingDirectory}{Environment.NewLine}" +
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
                    Message = localizer.F(LocalizationKeys.GameLaunchFailed, exception.Message),
                    DiagnosticMessage =
                        $"{BuildLaunchContext(launchResult, request)}{Environment.NewLine}{exception.Message}",
                    DiagnosticException = exception,
                    Validation = validation
                };
            }

            return Failed(
                localizer.T(LocalizationKeys.GameProcessStartFailed),
                BuildRunnerSelectionFailure(launchResult, runtimeConfiguration.PreferredRunnerId));
        }

        return new GameLaunchResult
        {
            Success = true,
            Message = localizer.T(LocalizationKeys.GameProcessStarted),
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
            ? $"{localizer.T(LocalizationKeys.GameRuntimeRunner)}: {localizer.T(LocalizationKeys.GameRuntimeRunnerAuto)}"
            : $"{localizer.T(LocalizationKeys.GameRuntimeRunner)}: {launchResult.RunnerId}";
        var candidates = launchResult.Candidates.Count == 0
            ? ""
            : Environment.NewLine + string.Join(
                Environment.NewLine,
                launchResult.Candidates.Select(candidate =>
                    $"- {candidate.RunnerId}: {AvailabilityReason(candidate.Availability)}"));
        return runnerLine + candidates + Environment.NewLine +
            $"{localizer.T(LocalizationKeys.Executable)}: {request.ExecutablePath}{Environment.NewLine}" +
            $"{localizer.T(LocalizationKeys.Path)}: {request.WorkingDirectory}{Environment.NewLine}" +
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
                return $"{localizer.T(LocalizationKeys.GameRuntimeRunner)}: {preferredRunnerId}{Environment.NewLine}" +
                    localizer.T(LocalizationKeys.Unknown);
            }

            var candidate = launchResult.Candidates[0];
            return $"{localizer.T(LocalizationKeys.GameRuntimeRunner)}: {preferredRunnerId}{Environment.NewLine}" +
                $"{candidate.RunnerId}: {AvailabilityReason(candidate.Availability)}";
        }

        if (launchResult.Candidates.Count == 0)
        {
            return $"{localizer.T(LocalizationKeys.GameRuntimeRunner)}: {localizer.T(LocalizationKeys.GameRuntimeRunnerAuto)}{Environment.NewLine}" +
                localizer.T(LocalizationKeys.Unknown);
        }

        var details = string.Join(
            Environment.NewLine,
            launchResult.Candidates.Select(candidate =>
                $"- {candidate.RunnerId}: {AvailabilityReason(candidate.Availability)}"));
        return $"{localizer.T(LocalizationKeys.GameRuntimeRunner)}: {localizer.T(LocalizationKeys.GameRuntimeRunnerAuto)}{Environment.NewLine}{details}";
    }

    private string AvailabilityReason(GameRunnerAvailability availability) =>
        string.IsNullOrWhiteSpace(availability.Message)
            ? localizer.T(LocalizationKeys.Unknown)
            : string.IsNullOrWhiteSpace(availability.TechnicalDetail)
                ? availability.Message
                : $"{availability.Message}{Environment.NewLine}{availability.TechnicalDetail}";
}
