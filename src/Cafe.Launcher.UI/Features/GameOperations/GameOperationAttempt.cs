using System;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Services;

namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>
/// Admission for one execution attempt. Only this module asks the state policy; the executor
/// carries the admitted attempt into pipelines without asking again. A confirmation or retry
/// starts a fresh attempt, while path and running-process checks remain at the I/O boundary.
/// </summary>
internal sealed class GameOperationAttempt
{
    private readonly GameOperationPolicy.Operation operation;
    private readonly LauncherStatusSnapshot snapshot;

    private GameOperationAttempt(GameOperationPolicy.Operation operation, LauncherStatusSnapshot snapshot)
    {
        this.operation = operation;
        this.snapshot = snapshot;
    }

    public static GameOperationAdmission Evaluate(GameOperationPolicy.Operation operation, LauncherStatusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var decision = GameOperationPolicy.Decide(operation, snapshot.RuntimeState);
        return new GameOperationAdmission(decision,
            decision == GameOperationDecision.Allowed ? new GameOperationAttempt(operation, snapshot) : null);
    }

    /// <summary>Prevents an admitted repair from being reused as an uninstall or launch.</summary>
    public LauncherStatusSnapshot SnapshotFor(GameOperationPolicy.Operation expected) => operation == expected
        ? snapshot : throw new InvalidOperationException("An operation attempt cannot authorize another operation.");

    public static GameLaunchResult RejectLaunch(LauncherStatusSnapshot snapshot, LocalizationService localizer)
    {
        var message = localizer.T(snapshot.RuntimeState switch
        {
            LauncherRuntimeState.BelowLowestVersion => LocalizationKeys.GameBelowLowestVersion,
            LauncherRuntimeState.Corrupted => LocalizationKeys.GameCorruptedInstallationState,
            LauncherRuntimeState.IoFailure => LocalizationKeys.GameInstallationStateReadFailed,
            LauncherRuntimeState.RemoteUnavailable => LocalizationKeys.GameRemoteStateUnavailable,
            LauncherRuntimeState.UpdateAvailable => LocalizationKeys.UpdateAvailable,
            _ => LocalizationKeys.GameNotInstalled
        });
        return new GameLaunchResult
        {
            Message = message, DiagnosticMessage = message,
            Validation = new ManifestValidationResult { Message = message }
        };
    }
}

/// <summary>A rejected request has no executable attempt; callers must present the decision.</summary>
internal sealed record GameOperationAdmission(GameOperationDecision Decision, GameOperationAttempt? Attempt);
