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

    private readonly IFileDownloadService fileDownloadService;
    private readonly Crc64Service crc64Service;
    private readonly IHttpClientLeaseSource leaseSource;
    private readonly LocalDiagnostics diagnostics;
    private readonly Func<Task> getPauseTask;
    private readonly Func<bool> isPaused;

    internal DownloadExecutor(
        IFileDownloadService fileDownloadService,
        Crc64Service crc64Service,
        IHttpClientLeaseSource leaseSource,
        LocalDiagnostics diagnostics,
        Func<Task> getPauseTask,
        Func<bool> isPaused)
    {
        this.fileDownloadService = fileDownloadService;
        this.crc64Service = crc64Service;
        this.leaseSource = leaseSource;
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

        using var lease = await leaseSource
            .CreateLeaseAsync(proxyMode, cancellationToken)
            .ConfigureAwait(false);
        var client = lease.Client;
        using var semaphore = new SemaphoreSlim(MaxParallelDownloads, MaxParallelDownloads);
        var totalSize = fileList.Sum(item => item.SizeBytes);
        var downloadFiles = fileList.Select(file =>
        {
            var targetPath = GetTempName(GamePathValidator.GetSafePath(gamePath, file.Path));
            return new DownloadFileState(
                file,
                targetPath,
                GetExistingDownloadedSize(targetPath, file.SizeBytes));
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
            var downloadedSize = GetExistingDownloadedSize(
                downloadFile.TargetPath,
                downloadFile.File.SizeBytes);
            var previousSize = Interlocked.Exchange(
                ref downloadFile.ReportedSize,
                downloadedSize);
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
                var verifiedCrc = await fileDownloadService.DownloadAsync(
                    new FileDownloadRequest(
                        downloadFile.TargetPath,
                        cdnConfig,
                        source,
                        downloadFile.File.SizeBytes,
                        downloadFile.File.Hash,
                        downloadFile.File.Path),
                    new FileDownloadOperationControl(
                        client,
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
                        },
                        proxyMode != ProxyModes.Direct),
                    cancellationToken).ConfigureAwait(false);
                if (verifiedCrc is not null)
                {
                    verifiedHashes[downloadFile.File.Path] = verifiedCrc;
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
    /// download or in an earlier install round). Untouched installed files are
    /// still hashed: this is the only content-corruption self-heal for files an
    /// update does not rewrite — the launch check only compares size/existence.
    /// </summary>
    internal async Task<IReadOnlyList<ManifestFile>> InstallDownloadedFilesAsync(
        string gamePath,
        IReadOnlyList<ManifestFile> manifestFiles,
        IReadOnlyList<ManifestFile> downloadedFiles,
        IReadOnlyDictionary<string, string> verifiedHashes,
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
                failedFiles.Add(new ManifestFile { Path = file.Path, Size = file.Size, Hash = file.Hash });
            }
            else if (!(verifiedHashes.TryGetValue(file.Path, out var verifiedHash)
                && verifiedHash == file.Hash))
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

                    failedFiles.Add(new ManifestFile { Path = file.Path, Size = file.Size, Hash = file.Hash });
                    DeleteExistingFile(checkPath);
                }
            }

            progress((int)Math.Round(++index * 100d / manifestFiles.Count));
        }

        await diagnostics.VerboseAsync(
            "GameDownload",
            $"CRC check complete: {manifestFiles.Count - failedFiles.Count} passed, {failedFiles.Count} failed",
            CancellationToken.None).ConfigureAwait(false);

        // Install passed files BEFORE returning failures — prevents retry cascade
        var failedPathSet = failedFiles.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var file in downloadedFiles)
        {
            if (failedPathSet.Contains(file.Path))
                continue;

            var tempPath = GetTempName(GamePathValidator.GetSafePath(gamePath, file.Path));
            var targetPath = GetOriginName(tempPath);
            if (File.Exists(tempPath))
            {
                var dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                // 目标文件常因只读属性（手工拷贝、更新包标记）导致 File.Delete/Move 抛
                // UnauthorizedAccessException，先清除属性再覆盖。
                DeleteExistingFile(targetPath);
                File.Move(tempPath, targetPath);
            }
        }

        return failedFiles;
    }

    /// <summary>
    /// Deletes files that are no longer part of the manifest.
    /// </summary>
    internal static void RemoveFiles(string gamePath, IReadOnlyList<ManifestFile> files, Action<int>? progress)
    {
        for (var i = 0; i < files.Count; i++)
        {
            var filePath = GamePathValidator.GetSafePath(gamePath, files[i].Path);
            DeleteExistingFile(filePath);

            progress?.Invoke((int)Math.Round((i + 1) * 100d / files.Count));
        }
    }

    /// <summary>
    /// Deletes an existing file if present, clearing the read-only attribute first —
    /// <see cref="File.Delete(string)"/> throws <see cref="UnauthorizedAccessException"/>
    /// on read-only files, which previously aborted installs/updates outright.
    /// </summary>
    private static void DeleteExistingFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return;
        }

        if ((info.Attributes & FileAttributes.ReadOnly) != 0)
        {
            info.Attributes &= ~FileAttributes.ReadOnly;
        }

        info.Delete();
    }

    private static long GetExistingDownloadedSize(string path, long expectedSize)
    {
        if (expectedSize <= 0 || !File.Exists(path))
        {
            return 0;
        }

        var length = new FileInfo(path).Length;
        return length <= expectedSize ? length : 0;
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
