using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

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
    private readonly IDownloadTransportSource transportSource;
    private readonly Crc64Service crc64Service;
    private readonly DiskSpaceService diskSpaceService;
    private readonly LocalDiagnostics diagnostics;
    private readonly LocalizationService localizer;
    private readonly GameInstallationPath installationPath;
    private readonly DownloadCheckpointStore checkpointStore;
    private readonly IGameProcessTracker gameProcessTracker;
    private readonly object activeDownloadLock = new();
    private DownloadSession? activeSession;
    private readonly DownloadSessionContext sessionContext;
    private bool disposed;

    /// <summary>One proxy-aware lease serves a whole download batch; its timeout bounds slow resumptions, not single files.</summary>
    private static readonly TimeSpan DownloadLeaseTimeout = TimeSpan.FromMinutes(10);

    public GameDownloadService(
        LauncherApiClient apiClient,
        RemoteManifestService remoteManifestService,
        IFileDownloadService fileDownloadService,
        LocalInstallationStateStore localInstallationStateStore,
        LauncherSettingsService settingsService,
        HttpClientFactory httpClientFactory,
        RemoteHttpUrlValidator urlValidator,
        Crc64Service crc64Service,
        DiskSpaceService diskSpaceService,
        LocalDiagnostics diagnostics,
        LocalizationService localizer,
        GameInstallationPath installationPath,
        IGameProcessTracker gameProcessTracker)
    {
        this.apiClient = apiClient;
        this.remoteManifestService = remoteManifestService;
        this.fileDownloadService = fileDownloadService;
        this.localInstallationStateStore = localInstallationStateStore;
        this.settingsService = settingsService;
        this.transportSource = new LeaseBackedDownloadTransportSource(
            httpClientFactory,
            urlValidator,
            DownloadLeaseTimeout);
        this.crc64Service = crc64Service;
        this.diskSpaceService = diskSpaceService;
        this.diagnostics = diagnostics;
        this.localizer = localizer;
        this.installationPath = installationPath;
        this.gameProcessTracker = gameProcessTracker;
        checkpointStore = DownloadCheckpointStore.CreateDefault();
        sessionContext = BuildSessionContext(checkpointStore);
    }

    internal GameDownloadService(
        LauncherApiClient apiClient,
        RemoteManifestService remoteManifestService,
        IFileDownloadService fileDownloadService,
        LocalInstallationStateStore localInstallationStateStore,
        LauncherSettingsService settingsService,
        HttpClientFactory httpClientFactory,
        RemoteHttpUrlValidator urlValidator,
        Crc64Service crc64Service,
        DiskSpaceService diskSpaceService,
        LocalDiagnostics diagnostics,
        LocalizationService localizer,
        GameInstallationPath installationPath,
        IGameProcessTracker gameProcessTracker,
        string downloadStateFilePath)
        : this(
            apiClient,
            remoteManifestService,
            fileDownloadService,
            localInstallationStateStore,
            settingsService,
            httpClientFactory,
            urlValidator,
            crc64Service,
            diskSpaceService,
            diagnostics,
            localizer,
            installationPath,
            gameProcessTracker)
    {
        checkpointStore = new DownloadCheckpointStore(downloadStateFilePath);
        sessionContext = BuildSessionContext(checkpointStore);
    }

    public async Task<GameOperationResult> InstallOrUpdateAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        if (!GameOperationPolicy.Allows(GameOperationPolicy.Operation.InstallOrUpdate, snapshot.RuntimeState))
        {
            return DownloadSession.Failed(localizer.T(LocalizationKeys.OperationUnavailableForCurrentState), GameOperationErrorCode.InvalidState);
        }

        return await RunSessionAsync(snapshot, repair: false, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GameOperationResult> RepairAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        if (!GameOperationPolicy.Allows(GameOperationPolicy.Operation.Repair, snapshot.RuntimeState))
        {
            return DownloadSession.Failed(localizer.T(LocalizationKeys.OperationUnavailableForCurrentState), GameOperationErrorCode.InvalidState);
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
            // Only a live session is a user stop: shutdown calls Stop() with no active
            // session, and logging there produced phantom "stopped by user" entries on
            // every clean exit. The injected logger keeps the line off the process-wide
            // static sink.
            diagnostics.DebugAsync("GameDownload", "Download stopped by user").GetAwaiter().GetResult();
        }
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
            sessionContext,
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

    private DownloadSessionContext BuildSessionContext(DownloadCheckpointStore checkpointStore) =>
        new(
            apiClient,
            remoteManifestService,
            fileDownloadService,
            transportSource,
            crc64Service,
            localInstallationStateStore,
            settingsService,
            diskSpaceService,
            diagnostics,
            localizer,
            installationPath,
            checkpointStore,
            gameProcessTracker);

    private async Task<GameOperationResult> RunSessionAsync(
        LauncherStatusSnapshot snapshot,
        bool repair,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var session = DownloadSessionFactory.Create(
            sessionContext,
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
                if (ClearActiveSession(session))
                {
                    IsRunningChanged?.Invoke();
                }
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

    private bool ClearActiveSession(DownloadSession session)
    {
        var cleared = false;
        lock (activeDownloadLock)
        {
            if (ReferenceEquals(activeSession, session))
            {
                activeSession = null;
                cleared = true;
            }
        }

        session.Dispose();
        return cleared;
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
