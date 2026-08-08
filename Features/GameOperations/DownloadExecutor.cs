using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly HttpClientFactory httpClientFactory;
    private readonly LocalDiagnostics diagnostics;
    private readonly Func<Task> getPauseTask;
    private readonly Func<bool> isPaused;

    internal DownloadExecutor(
        IFileDownloadService fileDownloadService,
        Crc64Service crc64Service,
        HttpClientFactory httpClientFactory,
        LocalDiagnostics diagnostics,
        Func<Task> getPauseTask,
        Func<bool> isPaused)
    {
        this.fileDownloadService = fileDownloadService;
        this.crc64Service = crc64Service;
        this.httpClientFactory = httpClientFactory;
        this.diagnostics = diagnostics;
        this.getPauseTask = getPauseTask;
        this.isPaused = isPaused;
    }

    /// <summary>
    /// Downloads the given files concurrently (up to <see cref="MaxParallelDownloads"/>
    /// at once), applying the configured speed limit and cooperative pause/cancel.
    /// </summary>
    internal async Task DownloadFilesAsync(
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
            return;
        }

        using var lease = await httpClientFactory.CreateLeaseAsync(
            proxyMode, timeout: TimeSpan.FromMinutes(10), cancellationToken: cancellationToken).ConfigureAwait(false);
        var client = lease.Client;
        using var semaphore = new SemaphoreSlim(MaxParallelDownloads, MaxParallelDownloads);
        var downloadedSize = 0L;
        var totalSize = fileList.Sum(item => item.SizeBytes);
        await diagnostics.DebugAsync(
            "GameDownload",
            $"Downloading {fileList.Count} files, total {FileSizeFormatter.Format(totalSize)}", CancellationToken.None).ConfigureAwait(false);
        var startedAt = DateTimeOffset.Now;
        var throttleState = speedLimitBytesPerSec > 0
            ? new ThrottleState { BytesPerSec = speedLimitBytesPerSec }
            : null;
        var lastProgressTime = 0L;                   // Stopwatch timestamp-based throttling
        var progressIntervalTicks = Stopwatch.Frequency / 10;  // ~100ms

        var tasks = fileList.Select(async file =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var targetPath = GetTempName(GamePathValidator.GetSafePath(gamePath, file.Path));
                await fileDownloadService.DownloadAsync(
                    new FileDownloadRequest(
                        targetPath,
                        cdnConfig,
                        source,
                        file.SizeBytes,
                        file.Hash,
                        file.Path),
                    new FileDownloadOperationControl(
                        client,
                        getPauseTask,
                        async (bytes, ct) =>
                        {
                            // Throttle progress reporting to ~100ms intervals to avoid
                            // overwhelming the UI thread with high-frequency callbacks.
                            var now = Stopwatch.GetTimestamp();
                            var prev = Interlocked.Read(ref lastProgressTime);
                            if (now - prev < progressIntervalTicks)
                            {
                                return;
                            }
                            Interlocked.Exchange(ref lastProgressTime, now);

                            if (throttleState is not null)
                            {
                                var throttledTotal = Interlocked.Add(ref throttleState.TotalBytes, bytes);
                                var targetMs = throttledTotal * 1000L / throttleState.BytesPerSec;
                                var elapsedMs = throttleState.Watch.ElapsedMilliseconds;
                                if (elapsedMs < targetMs)
                                    await Task.Delay((int)Math.Clamp(targetMs - elapsedMs, 0, int.MaxValue), ct).ConfigureAwait(false);
                            }

                            var totalSizeVal = totalSize;
                            var totalBytes = Interlocked.Add(ref downloadedSize, bytes);
                            var elapsed = Math.Max(1, (DateTimeOffset.Now - startedAt).TotalSeconds);
                            var speed = (long)(totalBytes / elapsed);
                            var estimated = speed > 0 ? (totalSizeVal - totalBytes) / speed : 0;

                            var paused = isPaused();
                            progress(new GameOperationProgress
                            {
                                OperationKind = operationKind,
                                Stage = paused ? GameOperationStage.Paused : GameOperationStage.Downloading,
                                Progress = totalSizeVal > 0 ? (int)Math.Round(totalBytes * 100d / totalSizeVal) : 0,
                                BytesPerSecond = paused ? 0 : speed,
                                EstimatedRemaining = paused
                                    ? null
                                    : TimeSpan.FromSeconds(Math.Max(0, estimated)),
                                DownloadedSize = totalBytes,
                                TotalSize = totalSizeVal,
                                IsRunning = true,
                                CanStop = true,
                                CanPause = true,
                                IsPaused = paused
                            });
                        },
                        proxyMode != ProxyModes.Direct),
                    cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies downloaded files against the manifest (CRC64), deletes invalid
    /// .tmp files, installs the passed files, and returns the failed files so
    /// the caller can retry them.
    /// </summary>
    internal async Task<IReadOnlyList<ManifestFile>> InstallDownloadedFilesAsync(
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
                failedFiles.Add(new ManifestFile { Path = file.Path, Size = file.Size, Hash = file.Hash });
            }
            else
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
                    File.Delete(checkPath);
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
                if (File.Exists(targetPath))
                    File.Delete(targetPath);

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
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            progress?.Invoke((int)Math.Round((i + 1) * 100d / files.Count));
        }
    }

    private sealed class ThrottleState
    {
        public int BytesPerSec;
        public long TotalBytes;
        public Stopwatch Watch = Stopwatch.StartNew();
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
