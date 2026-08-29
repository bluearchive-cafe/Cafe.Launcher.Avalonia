using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// Assembles DownloadSession instances from the module's collaborators.
/// The single place where manifest diff, download execution, and checkpoint store wiring is expressed.
/// </summary>
internal static class DownloadSessionFactory
{
    /// <summary>
    /// Creates a ready-to-run download or repair session.
    /// </summary>
    internal static DownloadSession Create(
        LauncherApiClient apiClient,
        RemoteManifestService remoteManifestService,
        IFileDownloadService fileDownloadService,
        HttpClientFactory httpClientFactory,
        Crc64Service crc64Service,
        LocalInstallationStateStore localInstallationStateStore,
        LauncherSettingsService settingsService,
        DiskSpaceService diskSpaceService,
        LocalDiagnostics diagnostics,
        LocalizationService localizer,
        GameInstallationPath installationPath,
        DownloadCheckpointStore checkpointStore,
        IGameProcessTracker gameProcessTracker,
        LauncherStatusSnapshot snapshot,
        bool repair,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        return new DownloadSession(
            apiClient,
            remoteManifestService,
            fileDownloadService,
            httpClientFactory,
            crc64Service,
            localInstallationStateStore,
            settingsService,
            diskSpaceService,
            diagnostics,
            localizer,
            installationPath,
            checkpointStore,
            gameProcessTracker,
            snapshot,
            repair,
            progress,
            cancellationToken);
    }

    /// <summary>
    /// Attempts to create a session from a persisted checkpoint.
    /// Returns null when no checkpoint exists or it's stale (wrong version/basis/path/group).
    /// </summary>
    internal static async Task<DownloadSession?> TryCreateForResumeAsync(
        LauncherApiClient apiClient,
        RemoteManifestService remoteManifestService,
        IFileDownloadService fileDownloadService,
        HttpClientFactory httpClientFactory,
        Crc64Service crc64Service,
        LocalInstallationStateStore localInstallationStateStore,
        LauncherSettingsService settingsService,
        DiskSpaceService diskSpaceService,
        LocalDiagnostics diagnostics,
        LocalizationService localizer,
        GameInstallationPath installationPath,
        DownloadCheckpointStore checkpointStore,
        IGameProcessTracker gameProcessTracker,
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        var state = await checkpointStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            return null;
        }

        var gameConfig = snapshot.Remote.GameConfig;
        var settingsPath = installationPath.NormalizeGamePath(snapshot.Settings.GamePath);
        var statePath = installationPath.NormalizeGamePath(state.GamePath);
        if (gameConfig is null
            || !string.Equals(state.Version, gameConfig.GameLatestVersion, StringComparison.Ordinal)
            || !string.Equals(state.Basis, gameConfig.GameLatestFilePath, StringComparison.Ordinal)
            || !string.Equals(statePath, settingsPath, StringComparison.Ordinal)
            || !string.Equals(state.PatchUrlGroup, snapshot.Settings.PatchUrlGroup, StringComparison.Ordinal))
        {
            checkpointStore.Clear();
            return null;
        }

        return Create(
            apiClient,
            remoteManifestService,
            fileDownloadService,
            httpClientFactory,
            crc64Service,
            localInstallationStateStore,
            settingsService,
            diskSpaceService,
            diagnostics,
            localizer,
            installationPath,
            checkpointStore,
            gameProcessTracker,
            snapshot,
            state.IsRepair,
            progress,
            cancellationToken);
    }
}
