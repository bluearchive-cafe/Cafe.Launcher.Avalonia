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
/// Encapsulates one download/repair operation from checkpoint save through plan,
/// disk check, download execution, verification retry, and local-state commit.
/// Owns its own cancellation, pause, and persisted-state-clearing semantics.
/// </summary>
internal sealed class DownloadSession : IDisposable
{
    private const int MaxInstallVerificationRetry = 3;

    private readonly LauncherApiClient apiClient;
    private readonly LauncherSettingsService settingsService;
    private readonly LocalInstallationStateStore localInstallationStateStore;
    private readonly GameInstallationPath installationPath;
    private readonly DiskSpaceService diskSpaceService;
    private readonly LocalDiagnostics diagnostics;
    private readonly LocalizationService localizer;
    private readonly ManifestDiffCalculator diffCalculator;
    private readonly DownloadExecutor downloadExecutor;
    private readonly DownloadCheckpointStore checkpointStore;
    private readonly LauncherStatusSnapshot snapshot;
    private readonly bool repair;
    private readonly Action<GameOperationProgress> progress;
    private readonly object pauseLock = new();
    private TaskCompletionSource? pauseTcs;
    private bool disposed;

    /// <summary>Gets the cancellation source owned by this single download session.</summary>
    public CancellationTokenSource CancellationTokenSource { get; }
    private int clearPersistedStateOnCancel;

    /// <summary>Sets whether cancellation clears the persisted download checkpoint.</summary>
    public bool ClearPersistedStateOnCancel
    {
        set => Volatile.Write(ref clearPersistedStateOnCancel, value ? 1 : 0);
    }

    private bool ShouldClearPersistedStateOnCancel =>
        Volatile.Read(ref clearPersistedStateOnCancel) == 1;

    /// <summary>Gets whether execution is currently paused at a download boundary.</summary>
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

    /// <summary>Initializes the session and its isolated pause and cancellation state.</summary>
    public DownloadSession(
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
        LauncherStatusSnapshot snapshot,
        bool repair,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        this.apiClient = apiClient;
        this.localInstallationStateStore = localInstallationStateStore;
        this.installationPath = installationPath;
        this.settingsService = settingsService;
        this.diskSpaceService = diskSpaceService;
        this.diagnostics = diagnostics;
        this.localizer = localizer;
        this.checkpointStore = checkpointStore;
        this.snapshot = snapshot;
        this.repair = repair;
        this.progress = progress;
        CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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

    /// <summary>Runs the configured install, update, or repair workflow to a terminal result.</summary>
    public async Task<GameOperationResult> RunAsync()
    {
        var activeToken = CancellationTokenSource.Token;
        var operationKind = repair ? GameOperationKind.Repair : GameOperationKind.Download;
        string? gamePath = null;

        try
        {
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
            gamePath = installationPath.NormalizeGamePath(settings.GamePath);
            EnsureGamePath(gamePath);
            Directory.CreateDirectory(gamePath);

            var localGame = await localInstallationStateStore.ReadAsync(gamePath, activeToken).ConfigureAwait(false);
            if (localGame.GameConfig?.Name is { Length: > 0 }
                && await ProcessService.IsExeRunningAsync($"{localGame.GameConfig.Name}.exe", activeToken))
            {
                checkpointStore.Clear();
                return Failed(localizer.T("gameExecutableRunning"), GameOperationErrorCode.GameRunning);
            }

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
                    "GameDownload",
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

            // 下载前探测目录可写性：权限不足时立即失败并给出明确指引，
            // 避免大流量下载完成后才在落盘阶段报 UnauthorizedAccessException。
            if (!TryProbeWriteAccess(gamePath))
            {
                await diagnostics.MessageAsync(
                    "GameDownload",
                    $"Write probe failed: {gamePath}",
                    activeToken);
                checkpointStore.Clear();
                return Failed(
                    localizer.F("fileAccessDenied", gamePath),
                    GameOperationErrorCode.System,
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
                        repair ? "GameRepair" : "GameDownload",
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
            if (ShouldClearPersistedStateOnCancel)
            {
                checkpointStore.Clear();
            }

            progress(CreateProgress(operationKind, GameOperationStage.Stopped, 0));
            return Failed(localizer.T("operationStopped"), GameOperationErrorCode.Stopped);
        }
        catch (IOException exception) when (exception.HResult == unchecked((int)0x80070070))
        {
            await diagnostics.ErrorAsync("GameDownload", exception, CancellationToken.None).ConfigureAwait(false);
            checkpointStore.Clear();
            return Failed(localizer.T("diskSpaceInsufficient"), GameOperationErrorCode.InsufficientDiskSpace);
        }
        catch (UnauthorizedAccessException exception)
        {
            await diagnostics.ErrorAsync("GameDownload", exception, CancellationToken.None).ConfigureAwait(false);
            checkpointStore.Clear();
            return Failed(localizer.F("fileAccessDenied", gamePath), GameOperationErrorCode.System);
        }
        catch (IOException exception)
        {
            await diagnostics.ErrorAsync("GameDownload", exception, CancellationToken.None).ConfigureAwait(false);
            checkpointStore.Clear();
            return Failed(localizer.F("fileOperationFailed", exception.Message), GameOperationErrorCode.System);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            await diagnostics.ErrorAsync("GameDownload", exception, CancellationToken.None).ConfigureAwait(false);
            checkpointStore.Clear();
            return Failed(localizer.F("networkErrorDetail", exception.Message), GameOperationErrorCode.Network);
        }
        catch (Exception exception)
        {
            await diagnostics.ErrorAsync(
                "GameDownload",
                exception,
                CancellationToken.None);
            checkpointStore.Clear();
            return Failed(localizer.F("unexpectedError", exception.Message), GameOperationErrorCode.System);
        }
    }

    /// <summary>Probes that the game directory accepts file creation (write permission).</summary>
    private static bool TryProbeWriteAccess(string gamePath)
    {
        try
        {
            using var probe = new FileStream(
                Path.Combine(gamePath, ".launcher-write-probe.tmp"),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Pauses the session until <see cref="Resume"/> releases the pause gate.</summary>
    public void Pause()
    {
        lock (pauseLock)
        {
            pauseTcs ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        LocalDiagnostics.LogSync(LogEntrySeverity.Debug, "GameDownload", "Download paused");
    }

    /// <summary>Releases a paused session so subsequent download work can continue.</summary>
    public void Resume()
    {
        ResetPauseState();
        LocalDiagnostics.LogSync(LogEntrySeverity.Debug, "GameDownload", "Download resumed");
    }

    private Task GetPauseTaskSnapshot()
    {
        lock (pauseLock)
        {
            return pauseTcs?.Task ?? Task.CompletedTask;
        }
    }

    private void ResetPauseState()
    {
        lock (pauseLock)
        {
            pauseTcs?.TrySetResult();
            pauseTcs = null;
        }
    }

    /// <summary>Cancels the session and releases any paused work.</summary>
    public void Stop()
    {
        CancellationTokenSource.Cancel();
        ResetPauseState();
    }

    /// <summary>Releases the session-owned cancellation source and pause gate.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ResetPauseState();
        CancellationTokenSource.Dispose();
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
}
