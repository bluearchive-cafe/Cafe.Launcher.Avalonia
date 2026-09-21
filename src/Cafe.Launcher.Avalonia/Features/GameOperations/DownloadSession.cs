using System;
using System.Collections.Generic;
using System.Globalization;
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
using Cafe.Launcher.Avalonia.Services.GameRuntime;

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
    private readonly IGameProcessTracker gameProcessTracker;
    private readonly LauncherStatusSnapshot snapshot;
    private readonly DownloadOperationProfile profile;
    private readonly Action<GameOperationProgress> progress;
    private readonly object pauseLock = new();
    private TaskCompletionSource? pauseTcs;
    private bool disposed;

    /// <summary>Gets the cancellation source owned by this single download session.</summary>
    public CancellationTokenSource CancellationTokenSource { get; }
    private int stopReason;

    /// <summary>任何终局出口都丢弃检查点；唯一例外是生命周期退出保留供续传。</summary>
    private bool ShouldDiscardCheckpointAtExit =>
        (DownloadStopReason)Volatile.Read(ref stopReason) != DownloadStopReason.ApplicationExit;

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
        DownloadSessionContext context,
        LauncherStatusSnapshot snapshot,
        DownloadOperationProfile profile,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        apiClient = context.ApiClient;
        localInstallationStateStore = context.LocalInstallationStateStore;
        installationPath = context.InstallationPath;
        settingsService = context.SettingsService;
        diskSpaceService = context.DiskSpaceService;
        diagnostics = context.Diagnostics;
        localizer = context.Localizer;
        checkpointStore = context.CheckpointStore;
        gameProcessTracker = context.GameProcessTracker;
        this.snapshot = snapshot;
        this.profile = profile;
        this.progress = progress;
        CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        diffCalculator = new ManifestDiffCalculator(
            context.RemoteManifestService,
            context.LocalInstallationStateStore,
            context.Crc64Service);
        downloadExecutor = new DownloadExecutor(
            context.FileDownloadService,
            context.Crc64Service,
            context.TransportSource,
            context.Diagnostics,
            GetPauseTaskSnapshot,
            () => IsPaused);
    }

    /// <summary>Runs the configured install, update, or repair workflow to a terminal result.</summary>
    /// <remarks>
    /// 检查点只在操作在飞时存在：任何终局出口（成功、各类失败、用户停止）都在
    /// 出口处单点清除，唯一例外是生命周期退出（ApplicationExit）保留供下次启动
    /// 续传。去留判定依据停止原因而非异常类型——在飞操作可能以任意异常浮出，
    /// 若在逐个 catch 里清查，二者一旦分叉就会静默丢掉可续传状态。
    /// </remarks>
    public async Task<GameOperationResult> RunAsync()
    {
        var activeToken = CancellationTokenSource.Token;
        var operationKind = profile.Kind;
        string? gamePath = null;

        try
        {
            var settings = await settingsService.ReadAsync(activeToken).ConfigureAwait(false);
            var gameConfig = snapshot.Remote.GameConfig
                ?? await apiClient.GetGameConfigAsync(activeToken).ConfigureAwait(false);
            var preparation = await PrepareDownloadPlanAsync(
                settings,
                gameConfig,
                operationKind,
                value => gamePath = value,
                activeToken).ConfigureAwait(false);
            if (preparation.Failure is not null)
            {
                return preparation.Failure;
            }

            if (preparation.CompletedResult is not null)
            {
                return preparation.CompletedResult;
            }

            return await RunDownloadVerifyLoopAsync(
                preparation,
                settings.ProxyMode,
                operationKind,
                activeToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (activeToken.IsCancellationRequested)
        {
            progress(GameOperationProgressFactory.CreateProgress(operationKind, GameOperationStage.Stopped, 0));
            return GameOperationOutcomes.Failed(localizer.T(LocalizationKeys.OperationStopped), GameOperationErrorCode.Stopped);
        }
        catch (IOException exception) when (exception.HResult == unchecked((int)0x80070070))
        {
            await diagnostics.ErrorAsync("GameDownload", exception, CancellationToken.None).ConfigureAwait(false);
            return GameOperationOutcomes.Failed(localizer.T(LocalizationKeys.DiskSpaceInsufficient), GameOperationErrorCode.InsufficientDiskSpace);
        }
        catch (UnauthorizedAccessException exception)
        {
            await diagnostics.ErrorAsync("GameDownload", exception, CancellationToken.None).ConfigureAwait(false);
            return GameOperationOutcomes.Failed(localizer.F(LocalizationKeys.FileAccessDenied, gamePath), GameOperationErrorCode.System);
        }
        catch (IOException exception)
        {
            await diagnostics.ErrorAsync("GameDownload", exception, CancellationToken.None).ConfigureAwait(false);
            return GameOperationOutcomes.Failed(localizer.F(LocalizationKeys.FileOperationFailed, exception.Message), GameOperationErrorCode.System);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            await diagnostics.ErrorAsync("GameDownload", exception, CancellationToken.None).ConfigureAwait(false);
            return GameOperationOutcomes.Failed(localizer.F(LocalizationKeys.NetworkErrorDetail, exception.Message), GameOperationErrorCode.Network);
        }
        catch (Exception exception)
        {
            await diagnostics.ErrorAsync(
                "GameDownload",
                exception,
                CancellationToken.None);
            return GameOperationOutcomes.Failed(localizer.F(LocalizationKeys.UnexpectedError, exception.Message), GameOperationErrorCode.System);
        }
        finally
        {
            if (ShouldDiscardCheckpointAtExit)
            {
                checkpointStore.Clear();
            }
        }
    }


    /// <summary>下载计划准备阶段的产物；Failure/CompletedResult 任一非 null 即终止。</summary>
    private sealed record DownloadPlanPreparation(
        string? GamePath,
        DownloadPlan? Plan,
        CdnConfigResponse? CdnConfig,
        int SpeedLimitBytesPerSec,
        IReadOnlyList<string> KnownProcessNames,
        GameOperationResult? Failure,
        GameOperationResult? CompletedResult)
    {
        public static DownloadPlanPreparation Stop(GameOperationResult result) =>
            new(null, null, null, 0, [], result, null);
    }

    /// <summary>
    /// 下载前准备：解析安装路径、读取本地安装状态、写入 checkpoint、解析 CDN、
    /// 构建 diff 计划，并依次通过「磁盘空间」「目录可写」两道闸口。
    /// </summary>
    private async Task<DownloadPlanPreparation> PrepareDownloadPlanAsync(
        LauncherSettings settings,
        GameConfigResponse gameConfig,
        GameOperationKind operationKind,
        Action<string> reportGamePath,
        CancellationToken activeToken)
    {
        if (string.IsNullOrWhiteSpace(gameConfig.GameLatestVersion)
            || string.IsNullOrWhiteSpace(gameConfig.GameLatestFilePath)
            || string.IsNullOrWhiteSpace(gameConfig.GameStartExeName))
        {
            return DownloadPlanPreparation.Stop(GameOperationOutcomes.Failed(
                localizer.T(LocalizationKeys.DownloadRemoteConfigIncomplete),
                GameOperationErrorCode.RemoteConfiguration));
        }

        var speedLimitBytesPerSec = DownloadSpeedLimits.ToBytesPerSecond(settings.DownloadSpeedLimit);
        if (string.IsNullOrWhiteSpace(settings.GamePath))
        {
            return DownloadPlanPreparation.Stop(GameOperationOutcomes.Failed(
                localizer.T(LocalizationKeys.GameInstallPathNotConfigured),
                GameOperationErrorCode.PathMissing));
        }

        var gamePath = installationPath.NormalizeGamePath(settings.GamePath);
        reportGamePath(gamePath);
        GamePathValidator.EnsureGameDirectoryName(gamePath);
        Directory.CreateDirectory(gamePath);

        var localGame = await localInstallationStateStore.ReadAsync(gamePath, activeToken).ConfigureAwait(false);
        var knownProcessNames = RunningGameGate.ResolveKnownProcessNames(localGame.GameConfig, gameConfig);
        var gameRunning = await FindRunningGameFailureAsync(knownProcessNames, activeToken).ConfigureAwait(false);
        if (gameRunning is not null)
        {
            return DownloadPlanPreparation.Stop(gameRunning);
        }

        await checkpointStore.SaveAsync(new DownloadTaskState
        {
            Version = gameConfig.GameLatestVersion,
            Basis = gameConfig.GameLatestFilePath,
            GamePath = gamePath,
            IsRepair = profile.IsRepair,
            PatchUrlGroup = settings.PatchUrlGroup,
            StartedAt = DateTimeOffset.Now.ToString("O")
        }, activeToken);

        progress(GameOperationProgressFactory.CreateProgress(
            operationKind,
            profile.CheckStage,
            0));

        var cdnConfig = snapshot.Remote.CdnConfig
            ?? await apiClient.GetCdnConfigAsync(
                settings.PatchUrlGroup,
                activeToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(cdnConfig.PrimaryCdn) || string.IsNullOrWhiteSpace(cdnConfig.BackUpCdn))
        {
            return DownloadPlanPreparation.Stop(GameOperationOutcomes.Failed(
                localizer.T(LocalizationKeys.CdnConfigIncomplete),
                GameOperationErrorCode.CdnConfiguration));
        }

        var downloadPlan = await profile.BuildPlanAsync(
            diffCalculator,
            gamePath,
            localGame,
            gameConfig,
            settings.PatchUrlGroup,
            progress,
            activeToken).ConfigureAwait(false);

        if (downloadPlan.NeedDownload.Count == 0 && downloadPlan.NeedDelete.Count == 0)
        {
            await diagnostics.DebugAsync(
                "GameDownload",
                "Manifest diff: 0 files changed (already current)", CancellationToken.None).ConfigureAwait(false);
            var alreadyCurrentResult = new GameOperationResult
            {
                Success = true,
                Message = localizer.T(profile.NoChangesKey)
            };

            // 现有安装状态与将要提交的内容完全一致时，提交是纯粹的重写；
            // 跳过它让 Program Files 等只读位置下的“仅检查更新”安静通过。
            if (LocalInstallationStateMatchesCommit(localGame, gameConfig, downloadPlan.ManifestFiles))
            {
                return new DownloadPlanPreparation(
                    gamePath,
                    downloadPlan,
                    cdnConfig,
                    speedLimitBytesPerSec,
                    knownProcessNames,
                    Failure: null,
                    CompletedResult: alreadyCurrentResult);
            }

            // 确需提交时探测目录可写性，与下载路径的闸口保持一致，
            // 给出本地化的权限指引而非裸 I/O 异常。
            if (!DirectoryWriteProbe.CanWrite(gamePath))
            {
                return await StopForWriteDeniedAsync(gamePath, affectedCount: null, activeToken).ConfigureAwait(false);
            }

            // 这条提交同样往安装目录里写两个状态文件（game-launcher-config.json 与
            // manifest.json），所以「写入之前复查」这道闸门也适用于它（ADR-032）：计划阶段
            // 到这里的间隔不一定短——差异计算要逐一比对哈希——期间从桌面快捷方式把游戏起来
            // 就没人拦了（2026-09-15 复核轮）。
            var runningBeforeCommit = await FindRunningGameFailureAsync(
                knownProcessNames, activeToken).ConfigureAwait(false);
            if (runningBeforeCommit is not null)
            {
                return DownloadPlanPreparation.Stop(runningBeforeCommit);
            }

            await CommitInstallationStateAsync(
                gamePath,
                gameConfig,
                downloadPlan.ManifestFiles,
                activeToken).ConfigureAwait(false);
            return new DownloadPlanPreparation(
                gamePath,
                downloadPlan,
                cdnConfig,
                speedLimitBytesPerSec,
                knownProcessNames,
                Failure: null,
                CompletedResult: alreadyCurrentResult);
        }

        await diagnostics.DebugAsync(
            "GameDownload",
            $"Manifest diff: {downloadPlan.NeedDownload.Count} to download, {downloadPlan.NeedDelete.Count} to delete", CancellationToken.None).ConfigureAwait(false);

        var affectedCount = downloadPlan.NeedDownload.Count + downloadPlan.NeedDelete.Count;
        var plannedDownloadBytes = downloadPlan.NeedDownload.Sum(item => item.SizeBytes);
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
            return DownloadPlanPreparation.Stop(GameOperationOutcomes.Failed(
                localizer.F(
                    LocalizationKeys.DiskSpaceInsufficientDetail,
                    FileSizeFormatter.Format(diskCheck.RequiredBytes),
                    diskCheck.AvailableBytes.HasValue ? FileSizeFormatter.Format(diskCheck.AvailableBytes.Value) : "--"),
                GameOperationErrorCode.InsufficientDiskSpace,
                affectedCount));
        }

        // 下载前探测目录可写性：权限不足时立即失败并给出明确指引，
        // 避免大流量下载完成后才在落盘阶段报 UnauthorizedAccessException。
        if (!DirectoryWriteProbe.CanWrite(gamePath))
        {
            return await StopForWriteDeniedAsync(gamePath, affectedCount, activeToken).ConfigureAwait(false);
        }

        return new DownloadPlanPreparation(
            gamePath,
            downloadPlan,
            cdnConfig,
            speedLimitBytesPerSec,
            knownProcessNames,
            Failure: null,
            CompletedResult: null);
    }

    /// <summary>
    /// 「游戏是不是在跑」这道闸门（ADR-032）在本会话的入口：计划阶段与写入边界复查共用它，
    /// 正文在 <see cref="RunningGameGate"/>，与卸载侧的判据、报法不会分叉。返回 null 表示放行。
    /// </summary>
    private Task<GameOperationResult?> FindRunningGameFailureAsync(
        IReadOnlyList<string> knownProcessNames,
        CancellationToken activeToken) =>
        RunningGameGate.FindFailureAsync(
            gameProcessTracker,
            localizer,
            LocalizationKeys.GameExecutableRunning,
            knownProcessNames,
            activeToken);

    /// <summary>
    /// 写探测失败时的统一收尾：记日志并以本地化 FileAccessDenied 停止。
    /// 零差异提交路径与下载路径共用，避免两处闸口漂移。
    /// </summary>
    private async Task<DownloadPlanPreparation> StopForWriteDeniedAsync(
        string gamePath,
        int? affectedCount,
        CancellationToken activeToken)
    {
        await diagnostics.MessageAsync(
            "GameDownload",
            $"Write probe failed: {gamePath}",
            activeToken).ConfigureAwait(false);
        return DownloadPlanPreparation.Stop(affectedCount.HasValue
            ? GameOperationOutcomes.Failed(
                localizer.F(LocalizationKeys.FileAccessDenied, gamePath),
                GameOperationErrorCode.System,
                affectedCount.Value)
            : GameOperationOutcomes.Failed(
                localizer.F(LocalizationKeys.FileAccessDenied, gamePath),
                GameOperationErrorCode.System));
    }

    /// <summary>执行「下载 → 安装 → 校验」重试循环，直至全部通过或验证预算耗尽。</summary>
    private async Task<GameOperationResult> RunDownloadVerifyLoopAsync(
        DownloadPlanPreparation preparation,
        string proxyMode,
        GameOperationKind operationKind,
        CancellationToken activeToken)
    {
        var gamePath = preparation.GamePath!;
        var downloadPlan = preparation.Plan!;
        var cdnConfig = preparation.CdnConfig!;
        var currentDownloadList = downloadPlan.NeedDownload;
        var affectedCount = currentDownloadList.Count + downloadPlan.NeedDelete.Count;
        // 跨验证轮次累积的已验证哈希：安装阶段据此跳过对已验证文件的
        // 重复整读哈希（失败重试时此前每轮都会重哈希全部文件）。
        var verifiedHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var retry = 0; retry <= MaxInstallVerificationRetry; retry++)
        {
            await diagnostics.DebugAsync(
                "GameDownload",
                $"Install verification retry {retry + 1}/{MaxInstallVerificationRetry}, {currentDownloadList.Count} files", CancellationToken.None).ConfigureAwait(false);
            activeToken.ThrowIfCancellationRequested();
            var roundVerified = await downloadExecutor.DownloadFilesAsync(
                gamePath,
                cdnConfig,
                downloadPlan.Source,
                currentDownloadList,
                proxyMode,
                preparation.SpeedLimitBytesPerSec,
                operationKind,
                progress,
                activeToken).ConfigureAwait(false);
            foreach (var entry in roundVerified)
            {
                verifiedHashes[entry.Key] = entry.Value;
            }

            // 写入边界复查（ADR-032）：计划阶段答的是「点下按钮那一刻」，而下载可能持续数分钟，
            // 这期间游戏被从外部起来时预检的答复就已经过期。下载阶段只写 .tmp，从这里开始才动
            // 安装目录里的真实文件（先删、后搬移），所以在第一处写入之前复查一次同一道闸门。
            // 命中即返回失败而不是 Stop：.tmp 留在盘上，用户关掉游戏后重试会按已有字节继续
            // （检查点按既有终局语义丢弃——只有应用退出那一档才保留供跨会话续传）。
            var gameRunning = await FindRunningGameFailureAsync(
                preparation.KnownProcessNames,
                activeToken).ConfigureAwait(false);
            if (gameRunning is not null)
            {
                return gameRunning;
            }

            ManifestFileRemover.DeleteAll(gamePath, downloadPlan.NeedDelete, cancellationToken: activeToken);

            progress(GameOperationProgressFactory.CreateProgress(operationKind, GameOperationStage.FileCheck, 0));
            var failedFiles = await downloadExecutor.InstallDownloadedFilesAsync(
                gamePath,
                downloadPlan.ManifestFiles,
                currentDownloadList,
                verifiedHashes,
                downloadPlan.PlannedHashes,
                value => progress(GameOperationProgressFactory.CreateProgress(operationKind, GameOperationStage.FileCheck, value)),
                activeToken).ConfigureAwait(false);

            if (failedFiles.Count == 0)
            {
                await CommitInstallationStateAsync(
                    gamePath,
                    snapshot.Remote.GameConfig
                        ?? throw new InvalidOperationException("Game config was resolved during planning."),
                    downloadPlan.ManifestFiles,
                    activeToken).ConfigureAwait(false);
                progress(GameOperationProgressFactory.CreateProgress(
                    operationKind,
                    profile.CompletedStage,
                    100));
                await diagnostics.MessageAsync(
                    profile.LogCategory,
                    $"path: {gamePath}{Environment.NewLine}version: {snapshot.Remote.GameConfig?.GameLatestVersion}",
                    activeToken);
                return new GameOperationResult
                {
                    Success = true,
                    Message = localizer.T(profile.CompletedKey),
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
        return GameOperationOutcomes.Failed(
            localizer.F(LocalizationKeys.VerificationFailed, currentDownloadList.Count),
            GameOperationErrorCode.Network,
            affectedCount,
            currentDownloadList.Count);
    }

    /// <summary>Pauses the session until <see cref="Resume"/> releases the pause gate.</summary>
    public void Pause()
    {
        lock (pauseLock)
        {
            pauseTcs ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // 显式弃等而非 LogSync：本方法在 UI 点击路径上，LogSync 的 sync-over-async
        // 会在 Serilog async sink 背压时卡住 UI 线程；DebugAsync 内部吞掉全部异常，
        // 弃等的 Task 不会产生未观察异常。
        _ = diagnostics.DebugAsync("GameDownload", "Download paused");
    }

    /// <summary>Releases a paused session so subsequent download work can continue.</summary>
    public void Resume()
    {
        ResetPauseState();
        // 同 Pause()：UI 点击路径上显式弃等，不退回阻塞的 LogSync。
        _ = diagnostics.DebugAsync("GameDownload", "Download resumed");
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
    /// <summary>
    /// 停止会话。停止原因在取消触发前一次性写入并原子生效：用户停止在取消
    /// 处理时丢弃持久化检查点，生命周期退出保留它供下次启动续传——外层
    /// 不再需要在 Stop 之前预置任何标志。
    /// </summary>
    public void Stop(DownloadStopReason reason)
    {
        Volatile.Write(ref stopReason, (int)reason);
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

    /// <summary>
    /// 判断现有本地安装状态是否已与将要提交的内容完全一致（版本、manifest basis、
    /// 启动配置与全部文件清单）。一致时提交是纯粹的重写，可安全跳过。
    /// </summary>
    internal static bool LocalInstallationStateMatchesCommit(
        LocalInstallationState localGame,
        GameConfigResponse gameConfig,
        IReadOnlyList<ManifestFile> files)
    {
        if (localGame.Kind != LocalInstallationStateKind.Valid
            || localGame.Manifest is null
            || localGame.GameConfig is null)
        {
            return false;
        }

        var manifest = localGame.Manifest;
        var config = localGame.GameConfig;
        var latestVersion = gameConfig.GameLatestVersion ?? "";
        if (!string.Equals(manifest.Name, GamePaths.GameTag, StringComparison.Ordinal)
            || !string.Equals(manifest.Version, latestVersion, StringComparison.Ordinal)
            || !string.Equals(manifest.Basis, gameConfig.GameLatestFilePath ?? "", StringComparison.Ordinal)
            || !string.Equals(config.Tag, GamePaths.GameTag, StringComparison.Ordinal)
            || !string.Equals(config.Version, latestVersion, StringComparison.Ordinal)
            || !string.Equals(config.Name, gameConfig.GameStartExeName ?? "", StringComparison.Ordinal))
        {
            return false;
        }

        if (!config.Params.SequenceEqual(gameConfig.GameStartParams ?? [], StringComparer.Ordinal))
        {
            return false;
        }

        if (manifest.Files.Count != files.Count)
        {
            return false;
        }

        var existingByPath = new Dictionary<string, ManifestFile>(manifest.Files.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var existing in manifest.Files)
        {
            if (!existingByPath.TryAdd(existing.Path, existing))
            {
                return false;
            }
        }

        foreach (var file in files)
        {
            if (!existingByPath.TryGetValue(file.Path, out var existing)
                || !string.Equals(existing.Hash, file.Hash, StringComparison.Ordinal)
                || !string.Equals(
                    existing.Size,
                    file.SizeBytes.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
