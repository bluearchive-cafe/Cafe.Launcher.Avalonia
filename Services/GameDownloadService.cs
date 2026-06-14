using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    // Retry domain order: 0 = primary CDN, 1 = backup CDN.
    // The first 4 attempts use the backup, then 3 on primary, then 3 on backup.
    // The original launcher prioritises the backup CDN for initial attempts.
    internal static readonly int[] RetryDomainOrder = [1, 1, 1, 1, 0, 0, 0, 1, 1, 1];
    private const int MaxParallelDownloads = 10;
    private const int MaxInstallVerificationRetry = 3;

    private readonly LauncherApiClient apiClient;
    private readonly LocalGameStateService localGameStateService;
    private readonly LauncherSettingsService settingsService;
    private readonly ProxySettingsService proxySettingsService;
    private readonly Crc64Service crc64Service;
    private readonly DiskSpaceService diskSpaceService;
    private readonly LocalDiagnostics diagnostics;
    private readonly DownloadStateService downloadStateService;
    private readonly object activeDownloadLock = new();
    private readonly object pauseLock = new();
    private CancellationTokenSource? activeDownloadCts;
    private Task pauseTask = Task.CompletedTask;
    private TaskCompletionSource? pauseTcs;
    private bool clearStateOnCancel;
    private bool disposed;

    public GameDownloadService(
        LauncherApiClient apiClient,
        LocalGameStateService localGameStateService,
        LauncherSettingsService settingsService,
        ProxySettingsService proxySettingsService,
        Crc64Service crc64Service,
        DiskSpaceService diskSpaceService,
        LocalDiagnostics diagnostics,
        DownloadStateService downloadStateService)
    {
        this.apiClient = apiClient;
        this.localGameStateService = localGameStateService;
        this.settingsService = settingsService;
        this.proxySettingsService = proxySettingsService;
        this.crc64Service = crc64Service;
        this.diskSpaceService = diskSpaceService;
        this.diagnostics = diagnostics;
        this.downloadStateService = downloadStateService;
    }

    public async Task<GameOperationResult> InstallOrUpdateAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync(snapshot, repair: false, progress, cancellationToken);
    }

    public async Task<GameOperationResult> RepairAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync(snapshot, repair: true, progress, cancellationToken);
    }

    public void Stop(bool clearPersistedState = true)
    {
        CancellationTokenSource? cts;
        lock (activeDownloadLock)
        {
            clearStateOnCancel = clearPersistedState;
            cts = activeDownloadCts;
        }

        cts?.Cancel();
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
        var state = await downloadStateService.LoadAsync(cancellationToken);
        if (state is null)
        {
            return null;
        }

        var gameConfig = snapshot.Remote.GameConfig;
        var settingsPath = localGameStateService.NormalizeGamePath(snapshot.Settings.GamePath);
        var statePath = localGameStateService.NormalizeGamePath(state.GamePath);
        if (gameConfig is null
            || !string.Equals(state.Version, gameConfig.GameLatestVersion, StringComparison.Ordinal)
            || !string.Equals(state.Basis, gameConfig.GameLatestFilePath, StringComparison.Ordinal)
            || !string.Equals(statePath, settingsPath, StringComparison.Ordinal)
            || !string.Equals(state.PatchUrlGroup, snapshot.Settings.PatchUrlGroup, StringComparison.Ordinal))
        {
            downloadStateService.Clear();
            return null;
        }

        return await RunAsync(snapshot, state.IsRepair, progress, cancellationToken);
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
                return activeDownloadCts is not null && !activeDownloadCts.IsCancellationRequested;
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
        var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var activeToken = operationCts.Token;
        var operationRegistered = false;
        var operationKind = repair ? GameOperationKinds.Repair : GameOperationKinds.Download;

        try
        {
            ReplaceActiveDownloadCts(operationCts);
            operationRegistered = true;
            pauseTcs?.TrySetResult();
            pauseTcs = null;
            pauseTask = Task.CompletedTask;

            var gameConfig = snapshot.Remote.GameConfig ?? await apiClient.GetGameConfigAsync(activeToken);
            if (string.IsNullOrWhiteSpace(gameConfig.GameLatestVersion)
                || string.IsNullOrWhiteSpace(gameConfig.GameLatestFilePath)
                || string.IsNullOrWhiteSpace(gameConfig.GameStartExeName))
            {
                return Failed("Remote game config is incomplete.", "remote-config");
            }

            var settings = await settingsService.ReadAsync(activeToken);
            apiClient.SetProxyMode(settings.ProxyMode);
            var speedLimitBytesPerSec = DownloadSpeedLimits.ToBytesPerSecond(settings.DownloadSpeedLimit);
            if (string.IsNullOrWhiteSpace(settings.GamePath))
                return Failed("Game install path is not configured. Open Settings to choose a path.", "no-path");
            var gamePath = localGameStateService.NormalizeGamePath(settings.GamePath);
            Directory.CreateDirectory(gamePath);
            EnsureGamePath(gamePath);

            var localGame = await localGameStateService.ReadAsync(gamePath, activeToken);
            if (localGame.GameConfig?.Name is { Length: > 0 }
                && await ProcessService.IsExeRunningAsync($"{localGame.GameConfig.Name}.exe", activeToken))
            {
                return Failed("Game executable is running. Close the game before changing files.", "game-running");
            }

            // Persist download state for potential resume after restart
            await downloadStateService.SaveAsync(new Models.DownloadTaskState
            {
                Version = gameConfig.GameLatestVersion,
                Basis = gameConfig.GameLatestFilePath,
                GamePath = gamePath,
                IsRepair = repair,
                PatchUrlGroup = settings.PatchUrlGroup,
                StartedAt = DateTimeOffset.Now.ToString("O")
            }, activeToken);

            progress(CreateProgress(operationKind, repair ? "repair-check" : "update-check", 0));

            var cdnConfig = snapshot.Remote.CdnConfig ?? await apiClient.GetCdnConfigAsync(settings.PatchUrlGroup, activeToken);
            if (string.IsNullOrWhiteSpace(cdnConfig.PrimaryCdn) || string.IsNullOrWhiteSpace(cdnConfig.BackUpCdn))
            {
                return Failed("CDN config is incomplete.", "cdn-config");
            }

            var downloadPlan = repair
                ? await BuildRepairPlanAsync(gamePath, gameConfig, settings.PatchUrlGroup, progress, activeToken)
                : await BuildInstallOrUpdatePlanAsync(gamePath, localGame, gameConfig, settings.PatchUrlGroup, progress, activeToken);

            if (downloadPlan.NeedDownload.Count == 0 && downloadPlan.NeedDelete.Count == 0)
            {
                downloadStateService.Clear();
                return new GameOperationResult
                {
                    Success = true,
                    Message = repair ? "Repair check passed. No file needs repair." : "Game files are already current."
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
                return Failed("Disk space insufficient.", "game-download-error-no-space", affectedCount);
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
                    activeToken);

                await WriteTempConfigAsync(gamePath, gameConfig, downloadPlan.ManifestFiles, activeToken);
                RemoveFiles(gamePath, downloadPlan.NeedDelete, null);

                progress(CreateProgress(operationKind, "check-file", 0));
                var failedFiles = await InstallDownloadedFilesAsync(
                    gamePath,
                    downloadPlan.ManifestFiles,
                    currentDownloadList,
                    value => progress(CreateProgress(operationKind, "check-file", value)),
                    activeToken);

                if (failedFiles.Count == 0)
                {
                    downloadStateService.Clear();
                    progress(CreateProgress(operationKind, repair ? "repair-done" : "download-done", 100));
                    await diagnostics.MessageAsync(
                        repair ? "Game repair completed." : "Game install or update completed.",
                        $"path: {gamePath}{Environment.NewLine}version: {gameConfig.GameLatestVersion}",
                        activeToken);
                    return new GameOperationResult
                    {
                        Success = true,
                        Message = repair ? "Repair completed." : "Install / Update completed.",
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

            return Failed("Download verification failed after retries.", "game-download-error-network-down", affectedCount);
        }
        catch (OperationCanceledException) when (activeToken.IsCancellationRequested)
        {
            if (clearStateOnCancel)
            {
                downloadStateService.Clear();
            }

            progress(CreateProgress(operationKind, "stopped", 0));
            return Failed("Operation stopped.", "stopped");
        }
        catch (IOException exception) when (exception.HResult == unchecked((int)0x80070070))
        {
            await diagnostics.ErrorAsync("Game download disk space error.", exception, CancellationToken.None);
            return Failed("Disk space insufficient.", "game-download-error-no-space");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await diagnostics.ErrorAsync("Game file operation failed.", exception, CancellationToken.None);
            return Failed($"File operation failed: {exception.Message}", "error-system");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            await diagnostics.ErrorAsync("Game download network failed.", exception, CancellationToken.None);
            return Failed($"Network error: {exception.Message}", "game-download-error-network-down");
        }
        catch (Exception exception)
        {
            // Catch-all for any unexpected exception — log and surface to user
            await diagnostics.ErrorAsync(
                $"Game download unexpected error (operation: {operationKind})",
                exception,
                CancellationToken.None);
            return Failed($"Unexpected error: {exception.Message}", "error-system");
        }
        finally
        {
            if (operationRegistered)
            {
                ClearActiveDownloadCts(operationCts);
            }
            else
            {
                operationCts.Dispose();
            }

            pauseTcs?.TrySetResult();
            lock (pauseLock)
            {
                pauseTcs = null;
                pauseTask = Task.CompletedTask;
            }
            clearStateOnCancel = false;
        }
    }

    private void ReplaceActiveDownloadCts(CancellationTokenSource operationCts)
    {
        CancellationTokenSource? previous;
        lock (activeDownloadLock)
        {
            ThrowIfDisposed();
            previous = activeDownloadCts;
            activeDownloadCts = operationCts;
        }

        previous?.Cancel();
        previous?.Dispose();
    }

    private void ClearActiveDownloadCts(CancellationTokenSource operationCts)
    {
        var shouldDispose = false;
        lock (activeDownloadLock)
        {
            if (ReferenceEquals(activeDownloadCts, operationCts))
            {
                activeDownloadCts = null;
                shouldDispose = true;
            }
        }

        if (shouldDispose)
        {
            operationCts.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(GameDownloadService));
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        lock (activeDownloadLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cts = activeDownloadCts;
            activeDownloadCts = null;
        }

        cts?.Cancel();
        cts?.Dispose();
        pauseTcs?.TrySetResult();
        lock (pauseLock)
        {
            pauseTcs = null;
            pauseTask = Task.CompletedTask;
        }
    }

    private async Task<DownloadPlan> BuildInstallOrUpdatePlanAsync(
        string gamePath,
        LocalGameState localGame,
        GameConfigResponse gameConfig,
        string patchUrlGroup,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        var currentFiles = await GetCurrentManifestFilesAsync(gamePath, localGame, patchUrlGroup, cancellationToken);
        var latestManifest = await GetLatestManifestAsync(gameConfig, patchUrlGroup, cancellationToken);
        var statDiff = CheckStat(currentFiles, gamePath, value => progress(CreateProgress(GameOperationKinds.Download, "update-check", value)));
        var expected = GameManifestDiff(currentFiles, latestManifest.Manifest.File);
        var actual = GameResultMerge(expected, new DownloadPlan { NeedDownload = statDiff });

        actual.Source = latestManifest.Manifest.Source ?? "";
        actual.ManifestFiles = latestManifest.Manifest.File;
        return actual;
    }

    private async Task<DownloadPlan> BuildRepairPlanAsync(
        string gamePath,
        GameConfigResponse gameConfig,
        string patchUrlGroup,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        var localGame = await localGameStateService.ReadAsync(gamePath, cancellationToken);

        // Fetch current and latest manifests in parallel (matches original's Promise.all)
        var currentTask = GetCurrentManifestFilesAsync(gamePath, localGame, patchUrlGroup, cancellationToken);
        var latestTask = GetLatestManifestAsync(gameConfig, patchUrlGroup, cancellationToken);
        await Task.WhenAll(currentTask, latestTask);
        var currentFiles = await currentTask;
        var latestManifest = await latestTask;

        var hashDiff = await CheckHashAsync(
            latestManifest.Manifest.File,
            gamePath,
            value => progress(CreateProgress(GameOperationKinds.Repair, "repair-check", value)),
            cancellationToken);
        var expected = GameManifestDiff(currentFiles, latestManifest.Manifest.File);
        var actual = GameResultMerge(new DownloadPlan { NeedDownload = [], NeedDelete = expected.NeedDelete }, new DownloadPlan { NeedDownload = hashDiff });

        actual.Source = latestManifest.Manifest.Source ?? "";
        actual.ManifestFiles = latestManifest.Manifest.File;

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

    private async Task<IReadOnlyList<ManifestFile>> GetCurrentManifestFilesAsync(
        string gamePath,
        LocalGameState localGame,
        string patchUrlGroup,
        CancellationToken cancellationToken)
    {
        if (localGame.Manifest is null)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(localGame.Manifest.Version)
            || string.IsNullOrWhiteSpace(localGame.Manifest.Basis))
        {
            return localGame.Manifest.Files;
        }

        try
        {
            var manifestUrl = await apiClient.GetManifestUrlAsync(localGame.Manifest.Version, localGame.Manifest.Basis, patchUrlGroup, cancellationToken);
            if (string.IsNullOrWhiteSpace(manifestUrl.Url))
            {
                return localGame.Manifest.Files;
            }

            var manifest = await apiClient.GetRemoteManifestAsync(manifestUrl.Url, cancellationToken);
            return manifest.File;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return localGame.Manifest.Files;
        }
    }

    private async Task<(RemoteManifest Manifest, string Version, string Basis)> GetLatestManifestAsync(
        GameConfigResponse gameConfig,
        string patchUrlGroup,
        CancellationToken cancellationToken)
    {
        var version = gameConfig.GameLatestVersion ?? "";
        var basis = gameConfig.GameLatestFilePath ?? "";
        var manifestUrl = await apiClient.GetManifestUrlAsync(version, basis, patchUrlGroup, cancellationToken);
        if (string.IsNullOrWhiteSpace(manifestUrl.Url))
        {
            throw new InvalidOperationException("Remote manifest URL is empty.");
        }

        return (await apiClient.GetRemoteManifestAsync(manifestUrl.Url, cancellationToken), version, basis);
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

        var proxy = await proxySettingsService.CreateProxyAsync(proxyMode, cancellationToken);
        using var handler = new SocketsHttpHandler
        {
            UseProxy = proxyMode == ProxyModes.System,
            Proxy = proxy,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        };
        using var client = new HttpClient(handler)
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
                await semaphore.WaitAsync(cancellationToken);
                acquired = true;
                await DownloadFileAsync(
                    gamePath,
                    cdnConfig,
                    source,
                    file,
                    client,
                    throttleState,
                    bytes =>
                    {
                        var total = Interlocked.Add(ref downloadedSize, bytes);
                        var elapsed = Math.Max(1, (DateTimeOffset.Now - startedAt).TotalSeconds);
                        var speed = (long)(total / elapsed);
                        var estimated = speed > 0 ? (totalSize - total) / speed : 0;
                        progress(new GameOperationProgress
                        {
                            OperationKind = operationKind,
                            Stage = IsPaused ? "paused" : "download",
                            Progress = totalSize > 0 ? (int)Math.Round(total * 100d / totalSize) : 0,
                            Speed = IsPaused ? "" : $"{FileSizeFormatter.Format(speed)}/S",
                            Estimated = IsPaused ? "" : TimeSpan.FromSeconds(Math.Max(0, estimated)).ToString(@"hh\:mm\:ss"),
                            DownloadedSize = total,
                            TotalSize = totalSize,
                            IsRunning = true,
                            CanStop = true,
                            CanPause = true,
                            IsPaused = IsPaused
                        });
                    },
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

        await Task.WhenAll(tasks);
    }

    private async Task DownloadFileAsync(
        string gamePath,
        CdnConfigResponse cdnConfig,
        string source,
        ManifestFile file,
        HttpClient client,
        ThrottleState? throttleState,
        Action<long> onBytes,
        CancellationToken cancellationToken)
    {
        var targetPath = GetTempName(GamePathValidator.GetSafePath(gamePath, file.Path));
        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        var retryList = RetryDomainOrder.ToList();
        Exception? lastError = null;
        while (retryList.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var retryType = retryList[0];
            retryList.RemoveAt(0);

            var downloadUrl = BuildDownloadUrl(
                retryType == 1 ? cdnConfig.PrimaryCdn : cdnConfig.BackUpCdn,
                source,
                file.Path);

            try
            {
                var fi = new FileInfo(targetPath);
                var existingLength = fi.Exists ? fi.Length : 0;
                if (existingLength >= FileSizeFormatter.ParseSize(file.Size) && FileSizeFormatter.ParseSize(file.Size) > 0)
                {
                    return;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                if (existingLength > 0)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);
                }

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(targetPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                var buffer = new byte[1024 * 256];
                while (true)
                {
                    // Async pause — yields the thread instead of blocking it
                    await GetPauseTaskSnapshot();
                    cancellationToken.ThrowIfCancellationRequested();

                    var read = await responseStream.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    onBytes(read);

                    // Global rate limiting across all concurrent streams
                    if (throttleState is not null)
                    {
                        var total = Interlocked.Add(ref throttleState.TotalBytes, read);
                        var targetMs = total * 1000L / throttleState.BytesPerSec;
                        var elapsedMs = throttleState.Watch.ElapsedMilliseconds;
                        if (elapsedMs < targetMs)
                            await Task.Delay((int)(targetMs - elapsedMs), cancellationToken);
                    }
                }

                await output.FlushAsync(cancellationToken);
                var crc64 = await crc64Service.ComputeFileAsync(targetPath, null, cancellationToken);
                if (crc64 == file.Hash) return;

                // CRC64 mismatch — different CDN won't help, content is the same
                await diagnostics.MessageAsync(
                    "CRC64 mismatch after download",
                    $"file: {file.Path}{Environment.NewLine}" +
                    $"expected: {file.Hash}{Environment.NewLine}" +
                    $"actual:   {crc64}{Environment.NewLine}" +
                    $"size: {new FileInfo(targetPath).Length} / expected: {file.Size}",
                    CancellationToken.None);

                File.Delete(targetPath);
                return; // Don't retry — mark as done (file deleted, will be caught by install verification)
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                // Network error — retry with next domain if available
                if (retryList.Count == 0) throw;
            }
        }

        throw new HttpRequestException($"Download failed: {file.Path}", lastError);
    }

    private async Task WriteTempConfigAsync(
        string gamePath,
        GameConfigResponse gameConfig,
        IReadOnlyList<ManifestFile> files,
        CancellationToken cancellationToken)
    {
        var manifestFiles = files.Select(file =>
        {
            var item = new ManifestFile
            {
                Path = file.Path,
                Size = file.Size,
                Hash = file.Hash
            };
            item.Vc = OfficialHashService.GetManifestFileHash(item);
            return item;
        }).ToList();

        var manifest = new LocalManifest
        {
            Name = LauncherConstants.GameTag,
            Version = gameConfig.GameLatestVersion,
            Basis = gameConfig.GameLatestFilePath,
            Files = manifestFiles
        };
        manifest.Vc = OfficialHashService.GetManifestInfoHash(
            manifest.Name ?? "",
            manifest.Version ?? "",
            manifest.Basis ?? "");

        var config = new GameLauncherConfig
        {
            Tag = LauncherConstants.GameTag,
            Name = gameConfig.GameStartExeName,
            Params = gameConfig.GameStartParams ?? [],
            Version = gameConfig.GameLatestVersion
        };
        config.Vc = OfficialHashService.GetGameConfigHash(config);

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(
            GetTempName(Path.Combine(gamePath, LauncherConstants.ManifestFileName)),
            JsonSerializer.Serialize(manifest, jsonOptions),
            cancellationToken);
        await File.WriteAllTextAsync(
            GetTempName(Path.Combine(gamePath, LauncherConstants.GameConfigFileName)),
            JsonSerializer.Serialize(config, jsonOptions),
            cancellationToken);
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
                var crc64 = await crc64Service.ComputeFileAsync(checkPath, null, cancellationToken);
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

        File.Move(
            GetTempName(Path.Combine(gamePath, LauncherConstants.ManifestFileName)),
            Path.Combine(gamePath, LauncherConstants.ManifestFileName),
            overwrite: true);
        File.Move(
            GetTempName(Path.Combine(gamePath, LauncherConstants.GameConfigFileName)),
            Path.Combine(gamePath, LauncherConstants.GameConfigFileName),
            overwrite: true);

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

            var crc64 = await crc64Service.ComputeFileAsync(filePath, null, cancellationToken);
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

    internal static string BuildDownloadUrl(string? domain, string source, string filePath)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new InvalidOperationException("CDN domain is empty.");
        }

        var uri = new Uri(domain);
        var pathItems = source
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Concat(filePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
            .ToList();
        if (pathItems.Count > 0)
        {
            pathItems[^1] = Uri.EscapeDataString(pathItems[^1]);
        }

        var builder = new UriBuilder(uri)
        {
            Path = string.Join("/", pathItems)
        };
        return builder.Uri.AbsoluteUri;
    }

    private static void EnsureGamePath(string gamePath)
    {
        var fullPath = Path.GetFullPath(gamePath);
        if (!string.Equals(Path.GetFileName(fullPath), LauncherConstants.GameFolderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Game directory name must be {LauncherConstants.GameFolderName}.");
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

    private sealed class DownloadPlan
    {
        public string Source { get; set; } = "";

        public List<ManifestFile> NeedDownload { get; set; } = [];

        public List<ManifestFile> NeedDelete { get; set; } = [];

        public List<ManifestFile> ManifestFiles { get; set; } = [];
    }
}
