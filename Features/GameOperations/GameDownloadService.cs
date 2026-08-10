using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// Coordinates game download, update, repair, pause, and persisted-resume operations.
/// </summary>
public sealed class GameDownloadService : IDisposable
{
    /// <summary>Raised when <see cref="IsRunning"/> changes value.</summary>
    internal event Action? IsRunningChanged;

    private readonly LauncherApiClient apiClient;
    private readonly RemoteManifestService remoteManifestService;
    private readonly IFileDownloadService fileDownloadService;
    private readonly LocalInstallationStateStore localInstallationStateStore;
    private readonly LauncherSettingsService settingsService;
    private readonly HttpClientFactory httpClientFactory;
    private readonly Crc64Service crc64Service;
    private readonly DiskSpaceService diskSpaceService;
    private readonly LocalDiagnostics diagnostics;
    private readonly LocalizationService localizer;
    private readonly GameInstallationPath installationPath;
    private readonly DownloadCheckpointStore checkpointStore;
    private readonly object activeDownloadLock = new();
    private DownloadSession? activeSession;
    private bool disposed;

    public GameDownloadService(
        LauncherApiClient apiClient,
        RemoteManifestService remoteManifestService,
        IFileDownloadService fileDownloadService,
        LocalInstallationStateStore localInstallationStateStore,
        LauncherSettingsService settingsService,
        HttpClientFactory httpClientFactory,
        Crc64Service crc64Service,
        DiskSpaceService diskSpaceService,
        LocalDiagnostics diagnostics,
        LocalizationService localizer,
        GameInstallationPath installationPath)
    {
        this.apiClient = apiClient;
        this.remoteManifestService = remoteManifestService;
        this.fileDownloadService = fileDownloadService;
        this.localInstallationStateStore = localInstallationStateStore;
        this.settingsService = settingsService;
        this.httpClientFactory = httpClientFactory;
        this.crc64Service = crc64Service;
        this.diskSpaceService = diskSpaceService;
        this.diagnostics = diagnostics;
        this.localizer = localizer;
        this.installationPath = installationPath;
        checkpointStore = DownloadCheckpointStore.CreateDefault();
    }

    internal GameDownloadService(
        LauncherApiClient apiClient,
        RemoteManifestService remoteManifestService,
        IFileDownloadService fileDownloadService,
        LocalInstallationStateStore localInstallationStateStore,
        LauncherSettingsService settingsService,
        HttpClientFactory httpClientFactory,
        Crc64Service crc64Service,
        DiskSpaceService diskSpaceService,
        LocalDiagnostics diagnostics,
        LocalizationService localizer,
        GameInstallationPath installationPath,
        string downloadStateFilePath)
        : this(
            apiClient,
            remoteManifestService,
            fileDownloadService,
            localInstallationStateStore,
            settingsService,
            httpClientFactory,
            crc64Service,
            diskSpaceService,
            diagnostics,
            localizer,
            installationPath)
    {
        checkpointStore = new DownloadCheckpointStore(downloadStateFilePath);
    }

    public async Task<GameOperationResult> InstallOrUpdateAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.RuntimeState is not (
            LauncherRuntimeState.NotInstalled or
            LauncherRuntimeState.BelowLowestVersion or
            LauncherRuntimeState.UpdateAvailable))
        {
            return DownloadSession.Failed(localizer.T("operationUnavailableForCurrentState"), GameOperationErrorCode.InvalidState);
        }

        return await RunSessionAsync(snapshot, repair: false, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GameOperationResult> RepairAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.RuntimeState is not (
            LauncherRuntimeState.Corrupted or
            LauncherRuntimeState.Ready))
        {
            return DownloadSession.Failed(localizer.T("operationUnavailableForCurrentState"), GameOperationErrorCode.InvalidState);
        }

        return await RunSessionAsync(snapshot, repair: true, progress, cancellationToken).ConfigureAwait(false);
    }

    public void Stop(bool clearPersistedState = true)
    {
        DownloadSession? session;
        lock (activeDownloadLock)
        {
            session = activeSession;
            if (session is not null)
            {
                session.ClearPersistedStateOnCancel = clearPersistedState;
                activeSession = null;
            }
        }

        if (session is not null)
        {
            session.Stop();
            IsRunningChanged?.Invoke();
        }

        LocalDiagnostics.LogSync(LogEntrySeverity.Debug, "GameDownload", "Download stopped by user");
    }

    public async Task<GameOperationResult?> ResumePersistedAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return null;
        }

        var session = await DownloadSessionFactory.TryCreateForResumeAsync(
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
            snapshot,
            progress,
            cancellationToken).ConfigureAwait(false);

        if (session is null)
        {
            return null;
        }

        return await RunRegisteredSessionAsync(session);
    }

    public void Pause()
    {
        DownloadSession? session;
        lock (activeDownloadLock)
        {
            session = activeSession;
        }

        session?.Pause();
    }

    public void Resume()
    {
        DownloadSession? session;
        lock (activeDownloadLock)
        {
            session = activeSession;
        }

        session?.Resume();
    }

    public bool IsPaused
    {
        get
        {
            lock (activeDownloadLock)
            {
                return activeSession?.IsPaused ?? false;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (activeDownloadLock)
            {
                return activeSession is not null
                    && !activeSession.CancellationTokenSource.IsCancellationRequested;
            }
        }
    }

    private async Task<GameOperationResult> RunSessionAsync(
        LauncherStatusSnapshot snapshot,
        bool repair,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var session = DownloadSessionFactory.Create(
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
            snapshot,
            repair,
            progress,
            cancellationToken);
        return await RunRegisteredSessionAsync(session);
    }

    private async Task<GameOperationResult> RunRegisteredSessionAsync(DownloadSession session)
    {
        var registered = false;
        try
        {
            ReplaceActiveSession(session);
            registered = true;
            IsRunningChanged?.Invoke();
            return await session.RunAsync().ConfigureAwait(false);
        }
        finally
        {
            if (registered)
            {
                ClearActiveSession(session);
                IsRunningChanged?.Invoke();
            }
            else
            {
                session.Dispose();
            }
        }
    }

    private void ReplaceActiveSession(DownloadSession session)
    {
        DownloadSession? previous;
        lock (activeDownloadLock)
        {
            ThrowIfDisposed();
            previous = activeSession;
            activeSession = session;
        }

        previous?.Stop();
    }

    private void ClearActiveSession(DownloadSession session)
    {
        lock (activeDownloadLock)
        {
            if (ReferenceEquals(activeSession, session))
            {
                activeSession = null;
            }
        }

        session.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public void Dispose()
    {
        DownloadSession? session;
        lock (activeDownloadLock)
        {
            if (disposed) return;
            disposed = true;
            session = activeSession;
            activeSession = null;
        }

        session?.Stop();
        session?.Dispose();
        GC.SuppressFinalize(this);
    }
}
