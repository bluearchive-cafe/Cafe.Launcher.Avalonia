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
        IGameProcessTracker gameProcessTracker,
        DownloadCheckpointStore checkpointStore)
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
        this.checkpointStore = checkpointStore;
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
        LauncherDataRoot dataRoot)
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
            gameProcessTracker,
            new DownloadCheckpointStore(dataRoot))
    {
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

    /// <summary>
    /// 停止当前下载会话。停止原因随取消一次性传入会话：用户停止丢弃持久化
    /// 检查点，生命周期退出保留它供下次启动续传——调用方无需预置任何标志。
    /// </summary>
    public void Stop(DownloadStopReason reason)
    {
        DownloadSession? session;
        lock (activeDownloadLock)
        {
            session = activeSession;
            if (session is not null)
            {
                activeSession = null;
            }
        }

        if (session is not null)
        {
            session.Stop(reason);
            IsRunningChanged?.Invoke();
            // Only a live session counts as a stop: lifecycle shutdown calls Stop() with no
            // active session, and logging there produced phantom "stopped" entries on every
            // clean exit. The injected logger keeps the line off the process-wide static sink.
            // 显式弃等而非阻塞等待：Stop() 在 UI 点击路径上，sync-over-async 会在
            // Serilog async sink 背压时卡住 UI 线程；DebugAsync 内部吞掉全部异常，
            // 弃等的 Task 不会产生未观察异常。
            _ = diagnostics.DebugAsync(
                "GameDownload",
                reason == DownloadStopReason.UserRequested
                    ? "Download stopped by user"
                    : "Download stopped for application exit");
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

        // 被新操作取代的旧会话按用户意图处置：丢弃旧检查点，新会话写入自己的。
        previous?.Stop(DownloadStopReason.UserRequested);
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

        session?.Stop(DownloadStopReason.ApplicationExit);
        session?.Dispose();
        GC.SuppressFinalize(this);
    }
}
