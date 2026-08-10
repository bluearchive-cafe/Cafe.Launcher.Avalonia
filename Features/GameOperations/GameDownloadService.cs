using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
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

    // Retry domain order: 0 = backup CDN, 1 = primary CDN (matching the original Electron launcher).
    // The first 4 attempts use the primary CDN, then 3 on backup, then 3 on primary.
    private const int MaxInstallVerificationRetry = 3;

    private readonly LauncherApiClient apiClient;
    private readonly LocalInstallationStateStore localInstallationStateStore;
    private readonly GameInstallationPath installationPath;
    private readonly LauncherSettingsService settingsService;
    private readonly DiskSpaceService diskSpaceService;
    private readonly LocalDiagnostics diagnostics;
    private readonly LocalizationService localizer;
    private readonly ManifestDiffCalculator diffCalculator;
    private readonly DownloadExecutor downloadExecutor;
    private DownloadCheckpointStore checkpointStore;
    private readonly object activeDownloadLock = new();
    private readonly object pauseLock = new();
    private ActiveDownloadOperation? activeDownload;
    private TaskCompletionSource? pauseTcs;
    private bool disposed;

    /// <summary>
    /// Initializes the download coordinator and its focused manifest-diff and download-execution collaborators.
    /// </summary>
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
        this.localInstallationStateStore = localInstallationStateStore;
        this.installationPath = installationPath;
        this.settingsService = settingsService;
        this.diskSpaceService = diskSpaceService;
        this.diagnostics = diagnostics;
        this.localizer = localizer;
        checkpointStore = DownloadCheckpointStore.CreateDefault();
        diffCalculator = new ManifestDiffCalculator(
            remoteManifestService,
            localInstallationStateStore,
            crc64Service);
        downloadExecutor = new DownloadExecutor(
            fileDownloadService,
            crc64Service,
            httpClientFactory,
            diagnostics,
            GetPauseTaskSnapshot,
            () => IsPaused);
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
            return Failed(localizer.T("operationUnavailableForCurrentState"), GameOperationErrorCode.InvalidState);
        }

        return await RunAsync(snapshot, repair: false, progress, cancellationToken).ConfigureAwait(false);
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
            return Failed(localizer.T("operationUnavailableForCurrentState"), GameOperationErrorCode.InvalidState);
        }

        return await RunAsync(snapshot, repair: true, progress, cancellationToken).ConfigureAwait(false);
    }

    public void Stop(bool clearPersistedState = true)
    {
        // Only notify the observer if there is an actual state change.
        ActiveDownloadOperation? operation;
        TaskCompletionSource? tcs;
        lock (activeDownloadLock)
        {
            operation = activeDownload;
            if (operation is not null)
            {
                operation.ClearPersistedStateOnCancel = clearPersistedState;
                operation.CancellationTokenSource.Cancel();
                activeDownload = null;
            }
        }

        // Release any paused awaits so they can observe the cancellation
        lock (pauseLock)
        {
            tcs = pauseTcs;
        }
        tcs?.TrySetResult();

        if (operation is not null)
        {
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

        return await RunAsync(snapshot, state.IsRepair, progress, cancellationToken).ConfigureAwait(false);
    }

    public void Pause()
    {
        lock (pauseLock)
        {
            pauseTcs ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        LocalDiagnostics.LogSync(LogEntrySeverity.Debug, "GameDownload", "Download paused");
    }

    public void Resume()
    {
        ResetPauseState();
        LocalDiagnostics.LogSync(LogEntrySeverity.Debug, "GameDownload", "Download resumed");
    }

    public bool IsPaused
    {
        get
        {
            lock (pauseLock)
            {
                return pauseTcs is not null;
            }
        }
    }

    /// <summary>
    /// Returns a snapshot of the current pause task under lock,
    /// so download threads always await a consistent reference.
    /// </summary>
    private Task GetPauseTaskSnapshot()
    {
        lock (pauseLock)
        {
            return pauseTcs?.Task ?? Task.CompletedTask;
        }
    }

    /// <summary>
    /// Whether a download/repair/uninstall operation is currently active.
    /// </summary>
    public bool IsRunning
    {
        get
        {
            lock (activeDownloadLock)
            {
                return activeDownload is not null
                    && !activeDownload.CancellationTokenSource.IsCancellationRequested;
            }
        }
    }

    private async Task<GameOperationResult> RunAsync(
        LauncherStatusSnapshot snapshot,
        bool repair,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var operation = new ActiveDownloadOperation(
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
        var operationCts = operation.CancellationTokenSource;
        var activeToken = operationCts.Token;
        var operationRegistered = false;
        var operationKind = repair ? GameOperationKind.Repair : GameOperationKind.Download;

        try
        {
            ReplaceActiveDownload(operation);
            operationRegistered = true;
            IsRunningChanged?.Invoke();
            ResetPauseState();

            var settings = await settingsService.ReadAsync(activeToken).ConfigureAwait(false);
            var gameConfig = snapshot.Remote.GameConfig
                ?? await apiClient.GetGameConfigAsync(settings.ProxyMode, activeToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(gameConfig.GameLatestVersion)
                || string.IsNullOrWhiteSpace(gameConfig.GameLatestFilePath)
                || string.IsNullOrWhiteSpace(gameConfig.GameStartExeName))
            {
                return Failed(localizer.T("downloadRemoteConfigIncomplete"), GameOperationErrorCode.RemoteConfiguration);
            }

            var speedLimitBytesPerSec = DownloadSpeedLimits.ToBytesPerSecond(settings.DownloadSpeedLimit);
            if (string.IsNullOrWhiteSpace(settings.GamePath))
                return Failed(localizer.T("gameInstallPathNotConfigured"), GameOperationErrorCode.PathMissing);
            var gamePath = installationPath.NormalizeGamePath(settings.GamePath);
            EnsureGamePath(gamePath);
            Directory.CreateDirectory(gamePath);

            var localGame = await localInstallationStateStore.ReadAsync(gamePath, activeToken).ConfigureAwait(false);
            if (localGame.GameConfig?.Name is { Length: > 0 }
                && await ProcessService.IsExeRunningAsync($"{localGame.GameConfig.Name}.exe", activeToken))
            {
                checkpointStore.Clear();
                return Failed(localizer.T("gameExecutableRunning"), GameOperationErrorCode.GameRunning);
            }

            // Persist download state for potential resume after restart
            await checkpointStore.SaveAsync(new DownloadTaskState
            {
                Version = gameConfig.GameLatestVersion,
                Basis = gameConfig.GameLatestFilePath,
                GamePath = gamePath,
                IsRepair = repair,
                PatchUrlGroup = settings.PatchUrlGroup,
                StartedAt = DateTimeOffset.Now.ToString("O")
            }, activeToken);

            progress(CreateProgress(
                operationKind,
                repair ? GameOperationStage.RepairCheck : GameOperationStage.UpdateCheck,
                0));

            var cdnConfig = snapshot.Remote.CdnConfig
                ?? await apiClient.GetCdnConfigAsync(
                    settings.PatchUrlGroup,
                    settings.ProxyMode,
                    activeToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(cdnConfig.PrimaryCdn) || string.IsNullOrWhiteSpace(cdnConfig.BackUpCdn))
            {
                checkpointStore.Clear();
                return Failed(localizer.T("cdnConfigIncomplete"), GameOperationErrorCode.CdnConfiguration);
            }

            var downloadPlan = repair
                ? await diffCalculator.BuildRepairPlanAsync(
                    gamePath,
                    gameConfig,
                    settings.PatchUrlGroup,
                    settings.ProxyMode,
                    progress,
                    activeToken).ConfigureAwait(false)
                : await diffCalculator.BuildInstallOrUpdatePlanAsync(
                    gamePath,
                    localGame,
                    gameConfig,
                    settings.PatchUrlGroup,
                    settings.ProxyMode,
                    progress,
                    activeToken).ConfigureAwait(false);

            if (downloadPlan.NeedDownload.Count == 0 && downloadPlan.NeedDelete.Count == 0)
            {
                await diagnostics.DebugAsync(
                    "GameDownload",
                    "Manifest diff: 0 files changed (already current)", CancellationToken.None).ConfigureAwait(false);
                await CommitInstallationStateAsync(
                    gamePath,
                    gameConfig,
                    downloadPlan.ManifestFiles,
                    activeToken).ConfigureAwait(false);
                checkpointStore.Clear();
                return new GameOperationResult
                {
                    Success = true,
                    Message = repair
                        ? localizer.T("repairNoChanges")
                        : localizer.T("gameAlreadyCurrent")
                };
            }

            await diagnostics.DebugAsync(
                "GameDownload",
                $"Manifest diff: {downloadPlan.NeedDownload.Count} to download, {downloadPlan.NeedDelete.Count} to delete", CancellationToken.None).ConfigureAwait(false);

            var currentDownloadList = downloadPlan.NeedDownload;
            var affectedCount = currentDownloadList.Count + downloadPlan.NeedDelete.Count;
            var plannedDownloadBytes = currentDownloadList.Sum(item => item.SizeBytes);
            var isFreshInstall = snapshot.RuntimeState == LauncherRuntimeState.NotInstalled;
            var requiredBytes = DiskSpaceService.ResolveRequiredBytes(
                isFreshInstall,
                plannedDownloadBytes,
                gameConfig.DecompressionSize);
            var diskCheck = diskSpaceService.Check(gamePath, requiredBytes);
            progress(new GameOperationProgress
            {
                OperationKind = operationKind,
                Stage = GameOperationStage.DiskCheck,
                RequiredDiskBytes = diskCheck.RequiredBytes,
                AvailableDiskBytes = diskCheck.AvailableBytes,
                IsRunning = true,
                CanStop = true
            });
            if (!diskCheck.HasEnoughSpace)
            {
                await diagnostics.MessageAsync(
                    "Game download blocked by disk space.",
                    $"path: {gamePath}{Environment.NewLine}required: {FileSizeFormatter.Format(diskCheck.RequiredBytes)}{Environment.NewLine}available: {(diskCheck.AvailableBytes.HasValue ? FileSizeFormatter.Format(diskCheck.AvailableBytes.Value) : "--")}",
                    activeToken);
                checkpointStore.Clear();
                return Failed(
                    localizer.F(
                        "diskSpaceInsufficientDetail",
                        FileSizeFormatter.Format(diskCheck.RequiredBytes),
                        diskCheck.AvailableBytes.HasValue ? FileSizeFormatter.Format(diskCheck.AvailableBytes.Value) : "--"),
                    GameOperationErrorCode.InsufficientDiskSpace,
                    affectedCount);
            }

            for (var retry = 0; retry <= MaxInstallVerificationRetry; retry++)
            {
                await diagnostics.DebugAsync(
                    "GameDownload",
                    $"Install verification retry {retry + 1}/{MaxInstallVerificationRetry}, {currentDownloadList.Count} files", CancellationToken.None).ConfigureAwait(false);
                activeToken.ThrowIfCancellationRequested();
                await downloadExecutor.DownloadFilesAsync(
                    gamePath,
                    cdnConfig,
                    downloadPlan.Source,
                    currentDownloadList,
                    settings.ProxyMode,
                    speedLimitBytesPerSec,
                    operationKind,
                    progress,
                    activeToken).ConfigureAwait(false);

                DownloadExecutor.RemoveFiles(gamePath, downloadPlan.NeedDelete, null);

                progress(CreateProgress(operationKind, GameOperationStage.FileCheck, 0));
                var failedFiles = await downloadExecutor.InstallDownloadedFilesAsync(
                    gamePath,
                    downloadPlan.ManifestFiles,
                    currentDownloadList,
                    value => progress(CreateProgress(operationKind, GameOperationStage.FileCheck, value)),
                    activeToken).ConfigureAwait(false);

                if (failedFiles.Count == 0)
                {
                    await CommitInstallationStateAsync(
                        gamePath,
                        gameConfig,
                        downloadPlan.ManifestFiles,
                        activeToken).ConfigureAwait(false);
                    checkpointStore.Clear();
                    progress(CreateProgress(
                        operationKind,
                        repair ? GameOperationStage.RepairCompleted : GameOperationStage.DownloadCompleted,
                        100));
                    await diagnostics.MessageAsync(
                        repair ? "Game repair completed." : "Game install or update completed.",
                        $"path: {gamePath}{Environment.NewLine}version: {gameConfig.GameLatestVersion}",
                        activeToken);
                    return new GameOperationResult
                    {
                        Success = true,
                        Message = repair
                            ? localizer.T("repairCompleted")
                            : localizer.T("installUpdateCompleted"),
                        AffectedFileCount = affectedCount
                    };
                }

                if (retry < MaxInstallVerificationRetry)
                {
                    progress(new GameOperationProgress
                    {
                        OperationKind = operationKind,
                        Stage = GameOperationStage.VerificationRetry,
                        FailedFileCount = failedFiles.Count,
                        RetryAttempt = retry + 1,
                        RetryLimit = MaxInstallVerificationRetry,
                        IsRunning = true,
                        CanStop = true
                    });
                }

                currentDownloadList = failedFiles.Select(file => new ManifestFile
                {
                    Path = file.Path,
                    Size = file.Size,
                    Hash = file.Hash
                }).ToList();
            }

            progress(new GameOperationProgress
            {
                OperationKind = operationKind,
                Stage = GameOperationStage.VerificationFailed,
                FailedFileCount = currentDownloadList.Count,
                IsRunning = true,
                CanStop = true
            });
            checkpointStore.Clear();
            return Failed(
                localizer.F("verificationFailed", currentDownloadList.Count),
                GameOperationErrorCode.Network,
                affectedCount,
                currentDownloadList.Count);
        }
        catch (OperationCanceledException) when (activeToken.IsCancellationRequested)
        {
            if (operation.ShouldClearPersistedStateOnCancel)
            {
                checkpointStore.Clear();
            }

            progress(CreateProgress(operationKind, GameOperationStage.Stopped, 0));
            return Failed(localizer.T("operationStopped"), GameOperationErrorCode.Stopped);
        }
        catch (IOException exception) when (exception.HResult == unchecked((int)0x80070070))
        {
            await diagnostics.ErrorAsync("Game download disk space error.", exception, CancellationToken.None).ConfigureAwait(false);
            checkpointStore.Clear();
            return Failed(localizer.T("diskSpaceInsufficient"), GameOperationErrorCode.InsufficientDiskSpace);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await diagnostics.ErrorAsync("Game file operation failed.", exception, CancellationToken.None).ConfigureAwait(false);
            checkpointStore.Clear();
            return Failed(localizer.F("fileOperationFailed", exception.Message), GameOperationErrorCode.System);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            await diagnostics.ErrorAsync("Game download network failed.", exception, CancellationToken.None).ConfigureAwait(false);
            checkpointStore.Clear();
            return Failed(localizer.F("networkErrorDetail", exception.Message), GameOperationErrorCode.Network);
        }
        catch (Exception exception)
        {
            // Catch-all for any unexpected exception — log and surface to user
            await diagnostics.ErrorAsync(
                $"Game download unexpected error (operation: {operationKind})",
                exception,
                CancellationToken.None);
            checkpointStore.Clear();
            return Failed(localizer.F("unexpectedError", exception.Message), GameOperationErrorCode.System);
        }
        finally
        {
            if (operationRegistered)
            {
                ClearActiveDownload(operation);
                IsRunningChanged?.Invoke();
            }
            else
            {
                operationCts.Dispose();
            }

            ResetPauseState();
        }
    }

    private void ReplaceActiveDownload(ActiveDownloadOperation operation)
    {
        ActiveDownloadOperation? previous;
        lock (activeDownloadLock)
        {
            ThrowIfDisposed();
            previous = activeDownload;
            activeDownload = operation;
            previous?.CancellationTokenSource.Cancel();
        }
    }

    private void ClearActiveDownload(ActiveDownloadOperation operation)
    {
        lock (activeDownloadLock)
        {
            if (ReferenceEquals(activeDownload, operation))
            {
                activeDownload = null;
            }
        }

        operation.CancellationTokenSource.Dispose();
    }

    private void ResetPauseState()
    {
        lock (pauseLock)
        {
            pauseTcs?.TrySetResult();
            pauseTcs = null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public void Dispose()
    {
        ActiveDownloadOperation? operation;
        lock (activeDownloadLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            operation = activeDownload;
            activeDownload = null;
            operation?.CancellationTokenSource.Cancel();
        }

        ResetPauseState();
        GC.SuppressFinalize(this);
    }

    private async Task CommitInstallationStateAsync(
        string gamePath,
        GameConfigResponse gameConfig,
        IReadOnlyList<ManifestFile> files,
        CancellationToken cancellationToken)
    {
        var commit = new LocalInstallationStateCommit(
            gameConfig.GameLatestVersion ?? "",
            gameConfig.GameLatestFilePath ?? "",
            gameConfig.GameStartExeName ?? "",
            gameConfig.GameStartParams ?? [],
            files.Select(file => new LocalInstallationFile(
                file.Path,
                file.SizeBytes,
                file.Hash)).ToArray());
        var state = await localInstallationStateStore.CommitAsync(
            gamePath,
            commit,
            cancellationToken).ConfigureAwait(false);
        if (state.Kind != LocalInstallationStateKind.Valid)
        {
            throw new IOException(state.Error ?? $"Local installation state commit failed: {state.Kind}.");
        }
    }

    /// <summary>Builds a progress snapshot for a phase boundary of an operation.</summary>
    internal static GameOperationProgress CreateProgress(
        GameOperationKind kind,
        GameOperationStage stage,
        int value)
    {
        return new GameOperationProgress
        {
            OperationKind = kind,
            Stage = stage,
            Progress = value,
            IsRunning = true,
            CanStop = kind is GameOperationKind.Download or GameOperationKind.Repair,
            CanPause = false
        };
    }

    /// <summary>Creates a failed <see cref="GameOperationResult"/> with the given details.</summary>
    internal static GameOperationResult Failed(
        string message,
        GameOperationErrorCode errorCode,
        int affectedFileCount = 0,
        int failedFileCount = 0)
    {
        return new GameOperationResult
        {
            Success = false,
            Message = message,
            ErrorCode = errorCode,
            AffectedFileCount = affectedFileCount,
            FailedFileCount = failedFileCount
        };
    }

    /// <summary>Validates that the game directory has the expected folder name.</summary>
    internal static void EnsureGamePath(string gamePath)
    {
        var fullPath = Path.GetFullPath(gamePath);
        if (!string.Equals(Path.GetFileName(fullPath), GamePaths.GameFolderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Game directory name must be {GamePaths.GameFolderName}.");
        }
    }

    private sealed class ActiveDownloadOperation
    {
        public ActiveDownloadOperation(CancellationTokenSource cancellationTokenSource)
        {
            CancellationTokenSource = cancellationTokenSource;
        }

        public CancellationTokenSource CancellationTokenSource { get; }

        private int clearPersistedStateOnCancel;

        public bool ClearPersistedStateOnCancel
        {
            set => Volatile.Write(ref clearPersistedStateOnCancel, value ? 1 : 0);
        }

        public bool ShouldClearPersistedStateOnCancel =>
            Volatile.Read(ref clearPersistedStateOnCancel) == 1;
    }
}
