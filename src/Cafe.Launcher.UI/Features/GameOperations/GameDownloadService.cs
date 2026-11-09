using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>
/// Coordinates game download, update, repair, pause, and persisted-resume operations.
/// </summary>
internal sealed class GameDownloadService : IDisposable
{
    /// <summary>Raised when <see cref="IsRunning"/> changes value.</summary>
    internal event Action? IsRunningChanged;

    private readonly ILauncherApiClient apiClient;
    private readonly RemoteManifestService remoteManifestService;
    private readonly IFileDownloadService fileDownloadService;
    private readonly ILocalInstallationStateStore localInstallationStateStore;
    private readonly ILauncherSettingsService settingsService;
    private readonly IDownloadTransportSource transportSource;
    private readonly ICrc64Service crc64Service;
    private readonly IDiskSpaceService diskSpaceService;
    private readonly YostarGameProfile gameProfile;
    private readonly ILauncherDiagnostics diagnostics;
    private readonly LocalizationService localizer;
    private readonly IGameInstallationPath installationPath;
    private readonly DownloadCheckpointStore checkpointStore;
    private readonly IGameProcessTracker gameProcessTracker;
    private readonly object activeDownloadLock = new();
    private DownloadSession? activeSession;
    private readonly DownloadSessionContext sessionContext;
    private bool disposed;

    public GameDownloadService(
        YostarGameProfile gameProfile,
        ILauncherApiClient apiClient,
        RemoteManifestService remoteManifestService,
        IFileDownloadService fileDownloadService,
        ILocalInstallationStateStore localInstallationStateStore,
        ILauncherSettingsService settingsService,
        IDownloadTransportSource transportSource,
        ICrc64Service crc64Service,
        IDiskSpaceService diskSpaceService,
        ILauncherDiagnostics diagnostics,
        LocalizationService localizer,
        IGameInstallationPath installationPath,
        IGameProcessTracker gameProcessTracker,
        DownloadCheckpointStore checkpointStore)
    {
        this.gameProfile = gameProfile;
        this.apiClient = apiClient;
        this.remoteManifestService = remoteManifestService;
        this.fileDownloadService = fileDownloadService;
        this.localInstallationStateStore = localInstallationStateStore;
        this.settingsService = settingsService;
        this.transportSource = transportSource;
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
        YostarGameProfile gameProfile,
        ILauncherApiClient apiClient,
        RemoteManifestService remoteManifestService,
        IFileDownloadService fileDownloadService,
        ILocalInstallationStateStore localInstallationStateStore,
        ILauncherSettingsService settingsService,
        IDownloadTransportSource transportSource,
        ICrc64Service crc64Service,
        IDiskSpaceService diskSpaceService,
        ILauncherDiagnostics diagnostics,
        LocalizationService localizer,
        IGameInstallationPath installationPath,
        IGameProcessTracker gameProcessTracker,
        LauncherDataRoot dataRoot)
        : this(
            gameProfile,
            apiClient,
            remoteManifestService,
            fileDownloadService,
            localInstallationStateStore,
            settingsService,
            transportSource,
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

    /// <summary>Reads the fresh-install download size without writing settings, files, or checkpoints.</summary>
    internal async Task<long?> GetInstallDownloadSizeAsync(LauncherStatusSnapshot snapshot, CancellationToken cancellationToken)
    {
        var config = snapshot.Remote.GameConfig;
        if (config is null || string.IsNullOrWhiteSpace(config.GameLatestVersion)
            || string.IsNullOrWhiteSpace(config.GameLatestFilePath)) return null;
        var manifest = await remoteManifestService.GetOptionalManifestAsync(config.GameLatestVersion,
            config.GameLatestFilePath, snapshot.Settings.PatchUrlGroup, cancellationToken).ConfigureAwait(false);
        return manifest?.File.Sum(file => Math.Max(0, file.SizeBytes));
    }

    public async Task<GameOperationResult> InstallOrUpdateAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var admission = GameOperationAttempt.Evaluate(GameOperationPolicy.Operation.InstallOrUpdate, snapshot);
        return admission.Attempt is { } attempt
            ? await InstallOrUpdateAsync(attempt, progress, cancellationToken).ConfigureAwait(false)
            : GameOperationRejections.UnavailableResult(localizer);
    }

    internal Task<GameOperationResult> InstallOrUpdateAsync(
        GameOperationAttempt attempt, Action<GameOperationProgress> progress, CancellationToken cancellationToken = default) =>
        RunSessionAsync(attempt.SnapshotFor(GameOperationPolicy.Operation.InstallOrUpdate),
            DownloadOperationProfile.ForDownload(), progress, cancellationToken);


    public async Task<GameOperationResult> RepairAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var admission = GameOperationAttempt.Evaluate(GameOperationPolicy.Operation.Repair, snapshot);
        return admission.Attempt is { } attempt
            ? await RepairAsync(attempt, progress, cancellationToken).ConfigureAwait(false)
            : GameOperationRejections.UnavailableResult(localizer);
    }

    internal Task<GameOperationResult> RepairAsync(
        GameOperationAttempt attempt, Action<GameOperationProgress> progress, CancellationToken cancellationToken = default) =>
        RunSessionAsync(attempt.SnapshotFor(GameOperationPolicy.Operation.Repair),
            DownloadOperationProfile.ForRepair(), progress, cancellationToken);


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
            gameProfile,
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
        DownloadOperationProfile profile,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var session = DownloadSessionFactory.Create(
            sessionContext,
            snapshot,
            profile,
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
