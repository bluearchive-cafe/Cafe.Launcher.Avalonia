using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

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
        bool repair,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken) =>
        new(context, snapshot, repair, progress, cancellationToken);

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
        var state = await checkpointStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            return null;
        }

        var gameConfig = snapshot.Remote.GameConfig;
        var settingsPath = context.InstallationPath.NormalizeGamePath(snapshot.Settings.GamePath);
        var statePath = context.InstallationPath.NormalizeGamePath(state.GamePath);
        if (gameConfig is null
            || !string.Equals(state.Version, gameConfig.GameLatestVersion, StringComparison.Ordinal)
            || !string.Equals(state.Basis, gameConfig.GameLatestFilePath, StringComparison.Ordinal)
            || !string.Equals(statePath, settingsPath, StringComparison.Ordinal)
            || !string.Equals(state.PatchUrlGroup, snapshot.Settings.PatchUrlGroup, StringComparison.Ordinal))
        {
            checkpointStore.Clear();
            return null;
        }

        return Create(context, snapshot, state.IsRepair, progress, cancellationToken);
    }
}
