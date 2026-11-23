using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Models;

namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>
/// Creates download/repair sessions from the module's collaborator context.
/// The context is the single wiring object; these methods take a handful of
/// per-run inputs instead of forwarding a positional dependency list.
/// </summary>
internal static class DownloadSessionFactory
{
    /// <summary>
    /// Creates a ready-to-run download or repair session.
    /// </summary>
    internal static DownloadSession Create(
        DownloadSessionContext context,
        LauncherStatusSnapshot snapshot,
        DownloadOperationProfile profile,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken) =>
        new(context, snapshot, profile, progress, cancellationToken);

    /// <summary>
    /// Attempts to create a session from a persisted checkpoint.
    /// Returns null when no checkpoint exists or it's stale (wrong version/basis/path/group).
    /// </summary>
    internal static async Task<DownloadSession?> TryCreateForResumeAsync(
        DownloadSessionContext context,
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        var checkpointStore = context.CheckpointStore;
        if (!string.Equals(snapshot.GameId, context.GameProfile.RuntimeId, StringComparison.Ordinal)
            || !string.Equals(checkpointStore.GameId, context.GameProfile.RuntimeId, StringComparison.Ordinal))
        {
            return null;
        }

        var state = await checkpointStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            return null;
        }

        // A manually edited settings document must not turn a valid checkpoint into permission
        // to write another game's tree. A refused checkpoint remains available for correction.
        var settings = await context.SettingsService.ReadAsync(cancellationToken).ConfigureAwait(false);
        var identified = state.FormatVersion == DownloadCheckpointStore.CurrentFormatVersion
            && string.Equals(state.GameId, snapshot.GameId, StringComparison.Ordinal);
        try
        {
            if (!string.IsNullOrWhiteSpace(state.GamePath)
                && (GameInstallationPathSafety.FindConflict(snapshot.GameId, state.GamePath, settings) is not null
                    || GameInstallationPathSafety.FindConflict(snapshot.GameId, state.GamePath, snapshot.Settings) is not null))
            {
                return null;
            }

            if (!identified
                && !await DownloadCheckpointStore.HasVerifiedLegacyIdentityAsync(snapshot,
                    context.ILocalInstallationStateStore, context.InstallationPath, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            if (!DownloadCheckpointStore.MatchesSnapshot(state, snapshot, context.InstallationPath))
            {
                if (identified)
                {
                    checkpointStore.Clear();
                }

                return null;
            }
        }
        catch (Exception exception) when (StorageFailure.IsRecoverable(exception)
            || exception is ArgumentException or NotSupportedException)
        {
            // An unreadable or unverifiable legacy document is evidence, not
            // permission to delete it or guess which game owns it.
            return null;
        }

        return Create(context, snapshot, DownloadOperationProfile.FromCheckpoint(state.IsRepair), progress, cancellationToken);
    }
}
