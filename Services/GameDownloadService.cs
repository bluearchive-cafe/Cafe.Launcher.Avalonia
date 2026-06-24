using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class GameDownloadService : IDisposable
{
    /// <summary>Parameter object grouping all constructor dependencies.</summary>
    public sealed record Dependencies(
        LauncherApiClient ApiClient,
        RemoteManifestService RemoteManifestService,
        IFileDownloadService FileDownloadService,
        LocalInstallationStateStore LocalInstallationStateStore,
        LauncherSettingsService SettingsService,
        ProxySettingsService ProxySettingsService,
        Crc64Service Crc64Service,
        DiskSpaceService DiskSpaceService,
        LocalDiagnostics Diagnostics,
        LocalizationService Localizer,
        GameInstallationPath InstallationPath);

    // Retry domain order: 0 = backup CDN, 1 = primary CDN (matching the original Electron launcher).
    // The first 4 attempts use the primary CDN, then 3 on backup, then 3 on primary.
    private const int MaxParallelDownloads = 10;
    private const int MaxInstallVerificationRetry = 3;

    private readonly LauncherApiClient apiClient;
    private readonly RemoteManifestService remoteManifestService;
    private readonly IFileDownloadService fileDownloadService;
    private readonly LocalInstallationStateStore localInstallationStateStore;
    private readonly GameInstallationPath installationPath;
    private readonly LauncherSettingsService settingsService;
    private readonly ProxySettingsService proxySettingsService;
    private readonly Crc64Service crc64Service;
    private readonly DiskSpaceService diskSpaceService;
    private readonly LocalDiagnostics diagnostics;
    private readonly LocalizationService localizer;
    private readonly string downloadStateFilePath;
    private static readonly JsonSerializerOptions DownloadStateJsonOptions = JsonDefaults.Indented;
    private readonly object activeDownloadLock = new();
    private readonly object pauseLock = new();
    private ActiveDownloadOperation? activeDownload;
    private Task pauseTask = Task.CompletedTask;
    private TaskCompletionSource? pauseTcs;
    private bool disposed;

    public GameDownloadService(Dependencies deps)
    {
        apiClient = deps.ApiClient;
        remoteManifestService = deps.RemoteManifestService;
        fileDownloadService = deps.FileDownloadService;
        localInstallationStateStore = deps.LocalInstallationStateStore;
        installationPath = deps.InstallationPath;
        settingsService = deps.SettingsService;
        proxySettingsService = deps.ProxySettingsService;
        crc64Service = deps.Crc64Service;
        diskSpaceService = deps.DiskSpaceService;
        diagnostics = deps.Diagnostics;
        localizer = deps.Localizer;
        downloadStateFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LauncherConstants.ProductName,
            GamePaths.DownloadStateFileName);
    }

    internal GameDownloadService(Dependencies deps, string downloadStateFilePath)
        : this(deps)
    {
        this.downloadStateFilePath = downloadStateFilePath;
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
            return Failed(localizer.T("operationUnavailableForCurrentState"), "invalid-state");
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
            return Failed(localizer.T("operationUnavailableForCurrentState"), "invalid-state");
        }

        return await RunAsync(snapshot, repair: true, progress, cancellationToken).ConfigureAwait(false);
    }

    public void Stop(bool clearPersistedState = true)
    {
        ActiveDownloadOperation? operation;
        lock (activeDownloadLock)
        {
            operation = activeDownload;
            if (operation is not null)
            {
                operation.ClearPersistedStateOnCancel = clearPersistedState;
            }
        }

        operation?.CancellationTokenSource.Cancel();
        // Release any paused awaits so they can observe the cancellation
        TaskCompletionSource? tcs;
        lock (pauseLock)
        {
            tcs = pauseTcs;
        }
        tcs?.TrySetResult();
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

        var state = await LoadDownloadStateAsync(cancellationToken).ConfigureAwait(false);
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
            ClearDownloadState();
            return null;
        }

        return await RunAsync(snapshot, state.IsRepair, progress, cancellationToken).ConfigureAwait(false);
    }

    public void Pause()
    {
        lock (pauseLock)
        {
            if (pauseTcs is null)
            {
                pauseTcs = new TaskCompletionSource();
                pauseTask = pauseTcs.Task;
            }
        }
    }

    public void Resume()
    {
        lock (pauseLock)
        {
            pauseTcs?.TrySetResult();
            pauseTcs = null;
            pauseTask = Task.CompletedTask;
        }
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
            return pauseTask;
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
        var operationKind = repair ? GameOperationKinds.Repair : GameOperationKinds.Download;

        try
        {
            ReplaceActiveDownload(operation);
            operationRegistered = true;
            lock (pauseLock)
            {
                pauseTcs?.TrySetResult();
                pauseTcs = null;
                pauseTask = Task.CompletedTask;
            }

            var settings = await settingsService.ReadAsync(activeToken).ConfigureAwait(false);
            var gameConfig = snapshot.Remote.GameConfig
                ?? await apiClient.GetGameConfigAsync(settings.ProxyMode, activeToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(gameConfig.GameLatestVersion)
                || string.IsNullOrWhiteSpace(gameConfig.GameLatestFilePath)
                || string.IsNullOrWhiteSpace(gameConfig.GameStartExeName))
            {
                return Failed(localizer.T("downloadRemoteConfigIncomplete"), "remote-config");
            }

            var speedLimitBytesPerSec = DownloadSpeedLimits.ToBytesPerSecond(settings.DownloadSpeedLimit);
            if (string.IsNullOrWhiteSpace(settings.GamePath))
                return Failed(localizer.T("installPathNotConfigured"), "no-path");
            var gamePath = installationPath.NormalizeGamePath(settings.GamePath);
            EnsureGamePath(gamePath);
            Directory.CreateDirectory(gamePath);

            var localGame = await localInstallationStateStore.ReadAsync(gamePath, activeToken).ConfigureAwait(false);
            if (localGame.GameConfig?.Name is { Length: > 0 }
                && await ProcessService.IsExeRunningAsync($"{localGame.GameConfig.Name}.exe", activeToken))
            {
                return Failed(localizer.T("gameExecutableRunning"), "game-running");
            }

            // Persist download state for potential resume after restart
            await SaveDownloadStateAsync(new Models.DownloadTaskState
            {
                Version = gameConfig.GameLatestVersion,
                Basis = gameConfig.GameLatestFilePath,
                GamePath = gamePath,
                IsRepair = repair,
                PatchUrlGroup = settings.PatchUrlGroup,
                StartedAt = DateTimeOffset.Now.ToString("O")
            }, activeToken);

            progress(CreateProgress(operationKind, repair ? "repair-check" : "update-check", 0));

            var cdnConfig = snapshot.Remote.CdnConfig
                ?? await apiClient.GetCdnConfigAsync(
                    settings.PatchUrlGroup,
                    settings.ProxyMode,
                    activeToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(cdnConfig.PrimaryCdn) || string.IsNullOrWhiteSpace(cdnConfig.BackUpCdn))
            {
                return Failed(localizer.T("cdnConfigIncomplete"), "cdn-config");
            }

            var downloadPlan = repair
                ? await BuildRepairPlanAsync(
                    gamePath,
                    gameConfig,
                    settings.PatchUrlGroup,
                    settings.ProxyMode,
                    progress,
                    activeToken)
                : await BuildInstallOrUpdatePlanAsync(
                    gamePath,
                    localGame,
                    gameConfig,
                    settings.PatchUrlGroup,
                    settings.ProxyMode,
                    progress,
                    activeToken).ConfigureAwait(false);

            if (downloadPlan.NeedDownload.Count == 0 && downloadPlan.NeedDelete.Count == 0)
            {
                await CommitInstallationStateAsync(
                    gamePath,
                    gameConfig,
                    downloadPlan.ManifestFiles,
                    activeToken).ConfigureAwait(false);
                ClearDownloadState();
                return new GameOperationResult
                {
                    Success = true,
                    Message = repair
                        ? localizer.T("repairNoChanges")
                        : localizer.T("gameAlreadyCurrent")
                };
            }

            var currentDownloadList = downloadPlan.NeedDownload;
            var affectedCount = currentDownloadList.Count + downloadPlan.NeedDelete.Count;
            var requiredBytes = currentDownloadList.Sum(item => FileSizeFormatter.ParseSize(item.Size));
            if (!diskSpaceService.HasEnoughSpace(gamePath, requiredBytes))
            {
                await diagnostics.MessageAsync(
                    "Game download blocked by disk space.",
                    $"path: {gamePath}{Environment.NewLine}required: {FileSizeFormatter.Format(requiredBytes)}",
                    activeToken);
                return Failed(localizer.T("diskSpaceInsufficient"), "game-download-error-no-space", affectedCount);
            }

            for (var retry = 0; retry <= MaxInstallVerificationRetry; retry++)
            {
                activeToken.ThrowIfCancellationRequested();
                await DownloadFilesAsync(
                    gamePath,
                    cdnConfig,
                    downloadPlan.Source,
                    currentDownloadList,
                    settings.ProxyMode,
                    speedLimitBytesPerSec,
                    operationKind,
                    progress,
                    activeToken).ConfigureAwait(false);

                RemoveFiles(gamePath, downloadPlan.NeedDelete, null);

                progress(CreateProgress(operationKind, "check-file", 0));
                var failedFiles = await InstallDownloadedFilesAsync(
                    gamePath,
                    downloadPlan.ManifestFiles,
                    currentDownloadList,
                    value => progress(CreateProgress(operationKind, "check-file", value)),
                    activeToken).ConfigureAwait(false);

                if (failedFiles.Count == 0)
                {
                    await CommitInstallationStateAsync(
                        gamePath,
                        gameConfig,
                        downloadPlan.ManifestFiles,
                        activeToken).ConfigureAwait(false);
                    ClearDownloadState();
                    progress(CreateProgress(operationKind, repair ? "repair-done" : "download-done", 100));
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

                currentDownloadList = failedFiles.Select(file => new ManifestFile
                {
                    Path = GetOriginName(file.Path),
                    Size = file.Size,
                    Hash = file.Hash
                }).ToList();
            }

            return Failed(localizer.T("downloadVerificationFailed"), "game-download-error-network-down", affectedCount);
        }
        catch (OperationCanceledException) when (activeToken.IsCancellationRequested)
        {
            if (operation.ShouldClearPersistedStateOnCancel)
            {
                ClearDownloadState();
            }

            progress(CreateProgress(operationKind, "stopped", 0));
            return Failed(localizer.T("operationStopped"), "stopped");
        }
        catch (IOException exception) when (exception.HResult == unchecked((int)0x80070070))
        {
            await diagnostics.ErrorAsync("Game download disk space error.", exception, CancellationToken.None).ConfigureAwait(false);
            return Failed(localizer.T("diskSpaceInsufficient"), "game-download-error-no-space");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await diagnostics.ErrorAsync("Game file operation failed.", exception, CancellationToken.None).ConfigureAwait(false);
            return Failed(localizer.F("fileOperationFailed", exception.Message), "error-system");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            await diagnostics.ErrorAsync("Game download network failed.", exception, CancellationToken.None).ConfigureAwait(false);
            return Failed(localizer.F("networkErrorDetail", exception.Message), "game-download-error-network-down");
        }
        catch (Exception exception)
        {
            // Catch-all for any unexpected exception — log and surface to user
            await diagnostics.ErrorAsync(
                $"Game download unexpected error (operation: {operationKind})",
                exception,
                CancellationToken.None);
            return Failed(localizer.F("unexpectedError", exception.Message), "error-system");
        }
        finally
        {
            if (operationRegistered)
            {
                ClearActiveDownload(operation);
            }
            else
            {
                operationCts.Dispose();
            }

            lock (pauseLock)
            {
                pauseTcs?.TrySetResult();
                pauseTcs = null;
                pauseTask = Task.CompletedTask;
            }
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
        }

        previous?.CancellationTokenSource.Cancel();
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
        }

        operation?.CancellationTokenSource.Cancel();
        lock (pauseLock)
        {
            pauseTcs?.TrySetResult();
            pauseTcs = null;
            pauseTask = Task.CompletedTask;
        }
    }

    private async Task<DownloadTaskState?> LoadDownloadStateAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(downloadStateFilePath))
            return null;
        try
        {
            var json = await File.ReadAllTextAsync(downloadStateFilePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<DownloadTaskState>(json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveDownloadStateAsync(DownloadTaskState state, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(state, DownloadStateJsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(downloadStateFilePath) ?? ".");
        await File.WriteAllTextAsync(downloadStateFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    private void ClearDownloadState()
    {
        if (File.Exists(downloadStateFilePath))
            File.Delete(downloadStateFilePath);
    }

    private async Task<DownloadPlan> BuildInstallOrUpdatePlanAsync(
        string gamePath,
        LocalInstallationState localGame,
        GameConfigResponse gameConfig,
        string patchUrlGroup,
        string proxyMode,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        // Current files: best-effort remote fetch matching the local version, fall back to local manifest.
        var currentFiles = localGame.Manifest?.Files ?? [];
        if (localGame.Manifest is not null
            && !string.IsNullOrWhiteSpace(localGame.Manifest.Version)
            && !string.IsNullOrWhiteSpace(localGame.Manifest.Basis))
        {
            var currentManifest = await remoteManifestService.GetOptionalManifestAsync(
                localGame.Manifest.Version,
                localGame.Manifest.Basis,
                patchUrlGroup,
                proxyMode,
                cancellationToken).ConfigureAwait(false);
            if (currentManifest is not null)
            {
                currentFiles = currentManifest.File;
            }
        }

        // Latest manifest: required for diff computation.
        var version = gameConfig.GameLatestVersion ?? "";
        var basis = gameConfig.GameLatestFilePath ?? "";
        var latestManifest = await remoteManifestService.GetRequiredManifestAsync(
            version,
            basis,
            patchUrlGroup,
            proxyMode,
            cancellationToken).ConfigureAwait(false);
        var statDiff = CheckStat(currentFiles, gamePath, value => progress(CreateProgress(GameOperationKinds.Download, "update-check", value)));
        var expected = GameManifestDiff(currentFiles, latestManifest.File);
        var actual = GameResultMerge(expected, new DownloadPlan { NeedDownload = statDiff });

        actual.Source = latestManifest.Source ?? "";
        actual.ManifestFiles = latestManifest.File;
        return actual;
    }

    private async Task<DownloadPlan> BuildRepairPlanAsync(
        string gamePath,
        GameConfigResponse gameConfig,
        string patchUrlGroup,
        string proxyMode,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        var localGame = await localInstallationStateStore.ReadAsync(gamePath, cancellationToken).ConfigureAwait(false);
        var version = gameConfig.GameLatestVersion ?? "";
        var basis = gameConfig.GameLatestFilePath ?? "";
        var latestManifest = await remoteManifestService.GetRequiredManifestAsync(
            version,
            basis,
            patchUrlGroup,
            proxyMode,
            cancellationToken).ConfigureAwait(false);

        var hashDiff = await CheckHashAsync(
            latestManifest.File,
            gamePath,
            value => progress(CreateProgress(GameOperationKinds.Repair, "repair-check", value)),
            cancellationToken).ConfigureAwait(false);
        var needDelete = localGame.Kind == LocalInstallationStateKind.Valid
            ? GameManifestDiff(localGame.Manifest?.Files ?? [], latestManifest.File).NeedDelete
            : [];
        var actual = new DownloadPlan
        {
            NeedDownload = hashDiff,
            NeedDelete = needDelete
        };

        actual.Source = latestManifest.Source ?? "";
        actual.ManifestFiles = latestManifest.File;

        // Report repair-confirm with diff summary (matches original's repair-confirm progress = -1)
        progress(new GameOperationProgress
        {
            OperationKind = GameOperationKinds.Repair,
            Stage = "repair-confirm",
            Progress = -1,
            AffectedFileCount = actual.NeedDownload.Count + actual.NeedDelete.Count,
            DownloadedSize = actual.NeedDownload.Sum(f => FileSizeFormatter.ParseSize(f.Size)),
            IsRunning = true,
            CanStop = false
        });

        return actual;
    }

    private async Task DownloadFilesAsync(
        string gamePath,
        CdnConfigResponse cdnConfig,
        string source,
        IReadOnlyList<ManifestFile> fileList,
        string proxyMode,
        int speedLimitBytesPerSec,
        string operationKind,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        if (fileList.Count == 0)
        {
            return;
        }

        var proxy = await proxySettingsService.CreateProxyAsync(proxyMode, cancellationToken).ConfigureAwait(false);
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = proxyMode == ProxyModes.System,
            Proxy = proxy,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        };
        using var client = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        using var semaphore = new SemaphoreSlim(MaxParallelDownloads, MaxParallelDownloads);
        var downloadedSize = 0L;
        var totalSize = fileList.Sum(item => FileSizeFormatter.ParseSize(item.Size));
        var startedAt = DateTimeOffset.Now;
        var throttleState = speedLimitBytesPerSec > 0
            ? new ThrottleState { BytesPerSec = speedLimitBytesPerSec }
            : null;

        var tasks = fileList.Select(async file =>
        {
            var acquired = false;
            try
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired = true;
                var targetPath = GetTempName(GamePathValidator.GetSafePath(gamePath, file.Path));
                await fileDownloadService.DownloadAsync(
                    targetPath,
                    cdnConfig,
                    source,
                    FileSizeFormatter.ParseSize(file.Size),
                    file.Hash,
                    file.Path,
                    client,
                    GetPauseTaskSnapshot,
                    async (bytes, ct) =>
                    {
                        if (throttleState is not null)
                        {
                            var throttledTotal = Interlocked.Add(ref throttleState.TotalBytes, bytes);
                            var targetMs = throttledTotal * 1000L / throttleState.BytesPerSec;
                            var elapsedMs = throttleState.Watch.ElapsedMilliseconds;
                            if (elapsedMs < targetMs)
                                await Task.Delay((int)(targetMs - elapsedMs), ct).ConfigureAwait(false);
                        }

                        var totalSizeVal = totalSize;
                        var totalBytes = Interlocked.Add(ref downloadedSize, bytes);
                        var elapsed = Math.Max(1, (DateTimeOffset.Now - startedAt).TotalSeconds);
                        var speed = (long)(totalBytes / elapsed);
                        var estimated = speed > 0 ? (totalSizeVal - totalBytes) / speed : 0;
                        progress(new GameOperationProgress
                        {
                            OperationKind = operationKind,
                            Stage = IsPaused ? "paused" : "download",
                            Progress = totalSizeVal > 0 ? (int)Math.Round(totalBytes * 100d / totalSizeVal) : 0,
                            Speed = IsPaused ? "" : $"{FileSizeFormatter.Format(speed)}/S",
                            Estimated = IsPaused
                                ? ""
                                : TimeSpan.FromSeconds(Math.Max(0, estimated))
                                    .ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
                            DownloadedSize = totalBytes,
                            TotalSize = totalSizeVal,
                            IsRunning = true,
                            CanStop = true,
                            CanPause = true,
                            IsPaused = IsPaused
                        });
                    },
                    proxyMode == ProxyModes.System,
                    cancellationToken);
            }
            finally
            {
                if (acquired)
                {
                    try { semaphore.Release(); } catch (ObjectDisposedException) { }
                }
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
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
                long.Parse(file.Size, NumberStyles.None, CultureInfo.InvariantCulture),
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

    private async Task<IReadOnlyList<ManifestFile>> InstallDownloadedFilesAsync(
        string gamePath,
        IReadOnlyList<ManifestFile> manifestFiles,
        IReadOnlyList<ManifestFile> downloadedFiles,
        Action<int> progress,
        CancellationToken cancellationToken)
    {
        var downloadedPathSet = downloadedFiles.Select(item => item.Path).ToHashSet(StringComparer.Ordinal);
        var failedFiles = new List<ManifestFile>();
        var index = 0;

        foreach (var file in manifestFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var checkPath = downloadedPathSet.Contains(file.Path)
                ? GetTempName(GamePathValidator.GetSafePath(gamePath, file.Path))
                : GamePathValidator.GetSafePath(gamePath, file.Path);

            if (!File.Exists(checkPath))
            {
                failedFiles.Add(new ManifestFile { Path = GetTempName(file.Path), Size = file.Size, Hash = file.Hash });
            }
            else
            {
                var crc64 = await crc64Service.ComputeFileAsync(checkPath, null, cancellationToken).ConfigureAwait(false);
                if (crc64 != file.Hash)
                {
                    await diagnostics.MessageAsync(
                        "CRC64 mismatch during install verification",
                        $"file: {file.Path}{Environment.NewLine}" +
                        $"expected: {file.Hash}{Environment.NewLine}" +
                        $"actual:   {crc64}{Environment.NewLine}" +
                        $"size: {new FileInfo(checkPath).Length}",
                        CancellationToken.None);

                    failedFiles.Add(new ManifestFile { Path = GetTempName(file.Path), Size = file.Size, Hash = file.Hash });
                    File.Delete(checkPath);
                }
            }

            progress((int)Math.Round(++index * 100d / manifestFiles.Count));
        }

        // Install passed files BEFORE returning failures — prevents retry cascade
        var failedPathSet = failedFiles.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var file in downloadedFiles)
        {
            if (failedPathSet.Contains(GetTempName(file.Path)))
                continue;

            var tempPath = GetTempName(GamePathValidator.GetSafePath(gamePath, file.Path));
            var targetPath = GetOriginName(tempPath);
            if (File.Exists(tempPath))
            {
                var dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                if (File.Exists(targetPath))
                    File.Delete(targetPath);

                File.Move(tempPath, targetPath);
            }
        }

        if (failedFiles.Count > 0)
            return failedFiles;

        return failedFiles;
    }

    private static DownloadPlan GameManifestDiff(IReadOnlyList<ManifestFile> oldList, IReadOnlyList<ManifestFile> newList)
    {
        var needDownload = newList.ToDictionary(file => file.Path, file => file, StringComparer.Ordinal);
        var needDelete = new Dictionary<string, ManifestFile>(StringComparer.Ordinal);

        foreach (var oldFile in oldList)
        {
            if (!needDownload.TryGetValue(oldFile.Path, out var newFile))
            {
                needDelete[oldFile.Path] = oldFile;
            }
            else if (newFile.Hash == oldFile.Hash)
            {
                needDownload.Remove(oldFile.Path);
            }
        }

        return new DownloadPlan
        {
            NeedDownload = needDownload.Values.ToList(),
            NeedDelete = needDelete.Values.ToList()
        };
    }

    private static DownloadPlan GameResultMerge(params DownloadPlan[] plans)
    {
        var result = new DownloadPlan();
        var processed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in plans.SelectMany(plan => plan.NeedDelete))
        {
            if (processed.Add(file.Path))
            {
                result.NeedDelete.Add(file);
            }
        }

        foreach (var file in plans.SelectMany(plan => plan.NeedDownload))
        {
            if (processed.Add(file.Path))
            {
                result.NeedDownload.Add(file);
            }
        }

        return result;
    }

    private static List<ManifestFile> CheckStat(
        IReadOnlyList<ManifestFile> files,
        string gamePath,
        Action<int>? progress)
    {
        var diff = new List<ManifestFile>();
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var filePath = GamePathValidator.GetSafePath(gamePath, file.Path);
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length != FileSizeFormatter.ParseSize(file.Size))
            {
                diff.Add(file);
            }

            progress?.Invoke((int)Math.Round((i + 1) * 100d / files.Count));
        }

        return diff;
    }

    private async Task<List<ManifestFile>> CheckHashAsync(
        IReadOnlyList<ManifestFile> files,
        string gamePath,
        Action<int>? progress,
        CancellationToken cancellationToken)
    {
        var diff = new List<ManifestFile>();
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var filePath = GamePathValidator.GetSafePath(gamePath, file.Path);
            if (!File.Exists(filePath))
            {
                diff.Add(file);
                continue;
            }

            var crc64 = await crc64Service.ComputeFileAsync(filePath, null, cancellationToken).ConfigureAwait(false);
            if (crc64 != file.Hash)
            {
                diff.Add(file);
            }

            progress?.Invoke((int)Math.Round((i + 1) * 100d / files.Count));
        }

        return diff;
    }

    private static void RemoveFiles(string gamePath, IReadOnlyList<ManifestFile> files, Action<int>? progress)
    {
        for (var i = 0; i < files.Count; i++)
        {
            var filePath = GamePathValidator.GetSafePath(gamePath, files[i].Path);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            progress?.Invoke((int)Math.Round((i + 1) * 100d / files.Count));
        }
    }

    internal static string? ResolveRetryDomain(CdnConfigResponse cdnConfig, int retryType)
    {
        return retryType == 0 ? cdnConfig.BackUpCdn : cdnConfig.PrimaryCdn;
    }

    private static void EnsureGamePath(string gamePath)
    {
        var fullPath = Path.GetFullPath(gamePath);
        if (!string.Equals(Path.GetFileName(fullPath), GamePaths.GameFolderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Game directory name must be {GamePaths.GameFolderName}.");
        }
    }

    private static string GetTempName(string name)
    {
        return $"{name}.tmp";
    }

    private static string GetOriginName(string name)
    {
        return name.EndsWith(".tmp", StringComparison.Ordinal) ? name[..^4] : name;
    }

    private static GameOperationProgress CreateProgress(string kind, string stage, int value)
    {
        return new GameOperationProgress
        {
            OperationKind = kind,
            Stage = stage,
            Progress = value,
            IsRunning = true,
            CanStop = kind is GameOperationKinds.Download or GameOperationKinds.Repair,
            CanPause = false
        };
    }

    private static GameOperationResult Failed(string message, string errorType, int affectedFileCount = 0)
    {
        return new GameOperationResult
        {
            Success = false,
            Message = message,
            ErrorType = errorType,
            AffectedFileCount = affectedFileCount
        };
    }

    private sealed class ThrottleState
    {
        public int BytesPerSec;
        public long TotalBytes;
        public System.Diagnostics.Stopwatch Watch = System.Diagnostics.Stopwatch.StartNew();
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

    private sealed class DownloadPlan
    {
        public string Source { get; set; } = "";

        public List<ManifestFile> NeedDownload { get; set; } = [];

        public List<ManifestFile> NeedDelete { get; set; } = [];

        public List<ManifestFile> ManifestFiles { get; set; } = [];
    }
}
