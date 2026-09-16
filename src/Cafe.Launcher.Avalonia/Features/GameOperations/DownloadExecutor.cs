using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// Executes concurrent game file downloads, verifies them, and installs them
/// into the game directory.
/// </summary>
internal sealed class DownloadExecutor
{
    private const int MaxParallelDownloads = 10;

    /// <summary>
    /// 安装/更新校验阶段的有界并行度。校验是磁盘+CPU 混合负载，并行度超过机器核心数
    /// 后只增加 IO 争用；上限 8 避免高核心机器上把磁盘打满影响同机游戏/其他进程。
    /// </summary>
    private static readonly int VerificationParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8);

    private readonly IFileDownloadService fileDownloadService;
    private readonly Crc64Service crc64Service;
    private readonly IDownloadTransportSource transportSource;
    private readonly LocalDiagnostics diagnostics;
    private readonly Func<Task> getPauseTask;
    private readonly Func<bool> isPaused;

    internal DownloadExecutor(
        IFileDownloadService fileDownloadService,
        Crc64Service crc64Service,
        IDownloadTransportSource transportSource,
        LocalDiagnostics diagnostics,
        Func<Task> getPauseTask,
        Func<bool> isPaused)
    {
        this.fileDownloadService = fileDownloadService;
        this.crc64Service = crc64Service;
        this.transportSource = transportSource;
        this.diagnostics = diagnostics;
        this.getPauseTask = getPauseTask;
        this.isPaused = isPaused;
    }

    /// <summary>
    /// Downloads the given files concurrently (up to <see cref="MaxParallelDownloads"/>
    /// at once), applying the configured speed limit and cooperative pause/cancel.
    /// Returns the per-file CRC64 values verified during this call (files whose
    /// temp file was already complete on entry are absent — the caller must
    /// still verify those at install time).
    /// </summary>
    internal async Task<IReadOnlyDictionary<string, string>> DownloadFilesAsync(
        string gamePath,
        CdnConfigResponse cdnConfig,
        string source,
        IReadOnlyList<ManifestFile> fileList,
        string proxyMode,
        int speedLimitBytesPerSec,
        GameOperationKind operationKind,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        if (fileList.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        // 下载完成后即完成校验的文件记录在此，安装阶段据此跳过对同一字节的
        // 重复整读哈希（此前每个文件在下载后与安装前各被完整读盘哈希一次）。
        var verifiedHashes = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        using var transport = await transportSource
            .CreateAsync(proxyMode, cancellationToken)
            .ConfigureAwait(false);
        using var semaphore = new SemaphoreSlim(MaxParallelDownloads, MaxParallelDownloads);
        var totalSize = fileList.Sum(item => item.SizeBytes);
        var downloadFiles = fileList.Select(file =>
        {
            var targetPath = GetTempName(GamePathValidator.GetSafeFilePath(gamePath, file.Path));
            return new DownloadFileState(
                file,
                targetPath,
                fileDownloadService.GetExistingDownloadedSize(targetPath, file.SizeBytes));
        }).ToArray();
        var initialDownloadedSize = downloadFiles.Sum(item => item.ReportedSize);
        await diagnostics.DebugAsync(
            "GameDownload",
            $"Downloading {fileList.Count} files, total {FileSizeFormatter.Format(totalSize)}", CancellationToken.None).ConfigureAwait(false);
        var throttleState = speedLimitBytesPerSec > 0
            ? new DownloadTransferThrottle(speedLimitBytesPerSec)
            : null;
        var progressAccumulator = new DownloadProgressAccumulator(
            totalSize,
            initialDownloadedSize,
            TimeSpan.FromMilliseconds(100));
        var pauseMeasurementLock = new object();
        Task? measuredPauseTask = null;

        void ReportProgress(DownloadProgressSnapshot snapshot, bool paused)
        {
            var speed = snapshot.BytesPerSecond;
            TimeSpan? estimated = speed > 0
                ? TimeSpan.FromSeconds(Math.Max(0, (totalSize - snapshot.DownloadedSize) / speed))
                : null;
            progress(new GameOperationProgress
            {
                OperationKind = operationKind,
                Stage = paused ? GameOperationStage.Paused : GameOperationStage.Downloading,
                Progress = totalSize > 0
                    ? (int)Math.Round(snapshot.DownloadedSize * 100d / totalSize)
                    : 0,
                BytesPerSecond = paused ? 0 : speed,
                EstimatedRemaining = paused ? null : estimated,
                DownloadedSize = snapshot.DownloadedSize,
                TotalSize = totalSize,
                IsRunning = true,
                CanStop = true,
                CanPause = true,
                IsPaused = paused
            });
        }

        async Task WaitWhilePausedAsync()
        {
            var pauseTask = getPauseTask();
            if (pauseTask.IsCompleted)
            {
                return;
            }

            lock (pauseMeasurementLock)
            {
                if (!ReferenceEquals(measuredPauseTask, pauseTask))
                {
                    measuredPauseTask = pauseTask;
                    throttleState?.Pause();
                    progressAccumulator.Pause();
                }
            }

            try
            {
                await pauseTask.ConfigureAwait(false);
            }
            finally
            {
                lock (pauseMeasurementLock)
                {
                    if (ReferenceEquals(measuredPauseTask, pauseTask))
                    {
                        measuredPauseTask = null;
                        throttleState?.Resume();
                        progressAccumulator.Resume();
                    }
                }
            }
        }

        void RecordFileProgress(DownloadFileState downloadFile, long transferredBytes)
        {
            var paused = isPaused();
            long downloadedSize;
            long previousSize;
            if (transferredBytes > 0)
            {
                // 追加模式下按上报字节推进内存计数：此前每个 256KB 块都做一次
                // 磁盘 stat（File.Exists + Length），快盘 10 并发下是每秒数千次
                // 系统调用。同一文件的下载与回调在单个任务内串行，计数无竞争。
                previousSize = Interlocked.Add(ref downloadFile.ReportedSize, transferredBytes)
                    - transferredBytes;
                downloadedSize = previousSize + transferredBytes;
            }
            else
            {
                // 重置路径（CRC 失败、Content-Range 无效、超长临时文件被丢弃）：
                // 从磁盘重采样权威长度（.tmp 长度语义的唯一实现在下载服务中）。
                downloadedSize = fileDownloadService.GetExistingDownloadedSize(
                    downloadFile.TargetPath,
                    downloadFile.File.SizeBytes);
                previousSize = Interlocked.Exchange(
                    ref downloadFile.ReportedSize,
                    downloadedSize);
            }

            if (progressAccumulator.TryRecord(
                    transferredBytes,
                    downloadedSize - previousSize,
                    paused,
                    out var snapshot))
            {
                ReportProgress(snapshot, paused);
            }
        }

        ReportProgress(progressAccumulator.GetCurrentSnapshot(), paused: false);

        var tasks = downloadFiles.Select(async downloadFile =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var outcome = await fileDownloadService.DownloadAsync(
                    new FileDownloadRequest(
                        downloadFile.TargetPath,
                        cdnConfig,
                        source,
                        downloadFile.File.SizeBytes,
                        downloadFile.File.Hash,
                        downloadFile.File.Path),
                    new FileDownloadOperationControl(
                        transport,
                        WaitWhilePausedAsync,
                        async (bytes, ct) =>
                        {
                            if (throttleState is not null && bytes > 0)
                            {
                                var delay = throttleState.RecordBytes(bytes);
                                if (delay > TimeSpan.Zero)
                                {
                                    await Task.Delay(delay, ct).ConfigureAwait(false);
                                }
                            }

                            RecordFileProgress(downloadFile, bytes);
                        },
                        _ =>
                        {
                            RecordFileProgress(downloadFile, transferredBytes: 0);
                            return Task.CompletedTask;
                        }),
                    cancellationToken).ConfigureAwait(false);
                if (outcome.Kind == DownloadOutcomeKind.Transferred)
                {
                    verifiedHashes[downloadFile.File.Path] = outcome.Crc64!;
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return verifiedHashes;
    }

    /// <summary>
    /// Verifies downloaded files against the manifest (CRC64), deletes invalid
    /// .tmp files, installs the passed files, and returns the failed files so
    /// the caller can retry them. Files present in <paramref name="verifiedHashes"/>
    /// with a matching manifest hash skip the re-read (they were verified during
    /// download or in an earlier install round), and files present in
    /// <paramref name="plannedHashes"/> whose witness still matches skip it too (they
    /// were hashed by this session's planning pass). Untouched installed files are
    /// otherwise still hashed: this is the only content-corruption self-heal for files
    /// an update does not rewrite — the launch check only compares size/existence.
    /// </summary>
    internal async Task<IReadOnlyList<ManifestFile>> InstallDownloadedFilesAsync(
        string gamePath,
        IReadOnlyList<ManifestFile> manifestFiles,
        IReadOnlyList<ManifestFile> downloadedFiles,
        IReadOnlyDictionary<string, string> verifiedHashes,
        IReadOnlyDictionary<string, PlannedFileHash> plannedHashes,
        Action<int> progress,
        CancellationToken cancellationToken)
    {
        var downloadedPathSet = downloadedFiles.Select(item => item.Path).ToHashSet(StringComparer.Ordinal);
        // 有界并行校验：CRC64 全量重读是安装/更新阶段的主导等待，而各文件的校验彼此独立
        // （Crc64Service 经共享 ArrayPool 保证并发安全，下载阶段已在并发使用）。结果按清单
        // 下标收集，失败列表与进度仍保持与串行版本相同的清单顺序语义；自愈语义不变——
        // 未经 verifiedHashes/plannedHashes 见证豁免的文件仍全量重读。
        var failedFlags = new bool[manifestFiles.Count];
        var skippedCount = 0;
        var completedCount = 0;
        // AUD-PERF-007：校验阶段逐文件回调经百分比门控去重后抵达 UI 线程。
        var progressGate = new PercentProgressGate();

        using var semaphore = new SemaphoreSlim(VerificationParallelism, VerificationParallelism);
        var tasks = manifestFiles.Select(async (file, index) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var isDownloaded = downloadedPathSet.Contains(file.Path);
                var checkPath = isDownloaded
                    ? GetTempName(GamePathValidator.GetSafeFilePath(gamePath, file.Path))
                    : GamePathValidator.GetSafeFilePath(gamePath, file.Path);

                if (!File.Exists(checkPath))
                {
                    failedFlags[index] = true;
                }
                else if (!IsAlreadyVerified(file, checkPath, isDownloaded, verifiedHashes, plannedHashes))
                {
                    var crc64 = await crc64Service.ComputeFileAsync(checkPath, null, cancellationToken).ConfigureAwait(false);
                    if (crc64 != file.Hash)
                    {
                        await diagnostics.MessageAsync(
                            "GameDownload",
                            $"CRC64 mismatch: {file.Path}{Environment.NewLine}" +
                            $"expected: {file.Hash}{Environment.NewLine}" +
                            $"actual:   {crc64}{Environment.NewLine}" +
                            $"size: {new FileInfo(checkPath).Length}",
                            CancellationToken.None);

                        failedFlags[index] = true;
                        ManifestFileRemover.DeleteFileIfPresent(checkPath);
                    }
                }
                else
                {
                    Interlocked.Increment(ref skippedCount);
                }
            }
            finally
            {
                semaphore.Release();
            }

            // 校验是并行的（≤8 个 worker）：回调的到达顺序不保证单调，落后的那一次会把走过的
            // 桶再报一遍，所以这里用单调判据——进度只增不减，每个桶也只报一次
            // （2026-09-15 复核轮：非单调判据下 400 文件去重用例会偶发红）。
            var percent = (int)Math.Round(Interlocked.Increment(ref completedCount) * 100d / manifestFiles.Count);
            if (progressGate.ShouldDeliverMonotonic(percent))
            {
                progress(percent);
            }
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        var failedFiles = new List<ManifestFile>();
        for (var index = 0; index < manifestFiles.Count; index++)
        {
            if (failedFlags[index])
            {
                var file = manifestFiles[index];
                failedFiles.Add(new ManifestFile { Path = file.Path, Size = file.Size, Hash = file.Hash });
            }
        }

        await diagnostics.VerboseAsync(
            "GameDownload",
            $"CRC check complete: {manifestFiles.Count - failedFiles.Count} passed, {failedFiles.Count} failed, {skippedCount} skipped by verified/witness hash",
            CancellationToken.None).ConfigureAwait(false);

        // Install passed files BEFORE returning failures — prevents retry cascade
        var failedPathSet = failedFiles.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var file in downloadedFiles)
        {
            if (failedPathSet.Contains(file.Path))
                continue;

            var tempPath = GetTempName(GamePathValidator.GetSafeFilePath(gamePath, file.Path));
            var targetPath = GetOriginName(tempPath);
            if (File.Exists(tempPath))
            {
                var dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                // 目标文件常因只读属性（手工拷贝、更新包标记）导致 File.Delete/Move 抛
                // UnauthorizedAccessException，先清除属性再覆盖。
                ManifestFileRemover.DeleteFileIfPresent(targetPath);
                File.Move(tempPath, targetPath);
            }
        }

        return failedFiles;
    }

    /// <summary>
    /// Gets whether this session has already proven <paramref name="file"/>'s content, so the
    /// full read can be skipped. A downloaded file is proven by the hash taken right after its
    /// transfer; a file the session did not write is proven by the planning pass, but only while
    /// it still matches the witness captured then — anything that changed since is read again, so
    /// the content check keeps its teeth.
    /// </summary>
    private static bool IsAlreadyVerified(
        ManifestFile file,
        string checkPath,
        bool isDownloaded,
        IReadOnlyDictionary<string, string> verifiedHashes,
        IReadOnlyDictionary<string, PlannedFileHash> plannedHashes)
    {
        if (verifiedHashes.TryGetValue(file.Path, out var verifiedHash)
            && verifiedHash == file.Hash)
        {
            return true;
        }

        return !isDownloaded
            && plannedHashes.TryGetValue(file.Path, out var planned)
            && planned.Hash == file.Hash
            && planned.Matches(checkPath);
    }

    private sealed class DownloadFileState(
        ManifestFile file,
        string targetPath,
        long reportedSize)
    {
        public ManifestFile File { get; } = file;
        public string TargetPath { get; } = targetPath;
        public long ReportedSize = reportedSize;
    }

    private const string TempFileExtension = ".tmp";

    /// <summary>Gets the temporary download path for a file name.</summary>
    internal static string GetTempName(string name)
    {
        return $"{name}{TempFileExtension}";
    }

    /// <summary>Strips the temporary file extension to recover the original file name.</summary>
    private static string GetOriginName(string name)
    {
        return name.EndsWith(TempFileExtension, StringComparison.Ordinal) ? name[..^TempFileExtension.Length] : name;
    }
}
