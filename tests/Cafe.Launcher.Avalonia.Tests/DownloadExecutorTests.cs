using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class DownloadExecutorTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public DownloadExecutorTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public async Task InstallDownloadedFilesAsync_WhenTargetFileIsReadOnly_ReplacesInstalledFile()
    {
        var targetPath = Path.Combine(tempDir, "data.bin");
        var tempPath = DownloadExecutor.GetTempName(targetPath);
        var payload = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(tempPath, payload);
        await File.WriteAllTextAsync(targetPath, "stale");
        File.SetAttributes(targetPath, FileAttributes.ReadOnly);

        var hash = await new Crc64Service().ComputeFileAsync(tempPath);
        var manifestFile = new ManifestFile
        {
            Path = "data.bin",
            Size = payload.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Hash = hash
        };
        var failed = await CreateExecutor().InstallDownloadedFilesAsync(
            tempDir,
            [manifestFile],
            [manifestFile],
            new Dictionary<string, string>(),
            new Dictionary<string, PlannedFileHash>(),
            _ => { },
            CancellationToken.None);

        Assert.Empty(failed);
        Assert.Equal(payload, await File.ReadAllBytesAsync(targetPath));
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public async Task InstallDownloadedFilesAsync_WhenCrcMismatch_DeletesTempFileAndReportsFailure()
    {
        var targetPath = Path.Combine(tempDir, "mismatch.bin");
        var tempPath = DownloadExecutor.GetTempName(targetPath);
        await File.WriteAllBytesAsync(tempPath, [9, 9, 9]);
        var manifestFile = new ManifestFile
        {
            Path = "mismatch.bin",
            Size = "3",
            Hash = "deadbeef"
        };

        var failed = await CreateExecutor().InstallDownloadedFilesAsync(
            tempDir,
            [manifestFile],
            [manifestFile],
            new Dictionary<string, string>(),
            new Dictionary<string, PlannedFileHash>(),
            _ => { },
            CancellationToken.None);

        _ = Assert.Single(failed);
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public async Task InstallDownloadedFilesAsync_WhenHashPreVerified_SkipsRecheckAndInstalls()
    {
        // 契约：verifiedHashes 已有与 manifest 匹配的条目时跳过重读校验
        // （下载阶段已验证）；写入方必须保证条目真实性——即便落盘内容
        // 与哈希不一致也不再拦截。
        var targetPath = Path.Combine(tempDir, "verified.bin");
        var tempPath = DownloadExecutor.GetTempName(targetPath);
        await File.WriteAllBytesAsync(tempPath, [1, 2, 3]);
        var manifestFile = new ManifestFile
        {
            Path = "verified.bin",
            Size = "3",
            Hash = "trusted-hash"
        };

        var failed = await CreateExecutor().InstallDownloadedFilesAsync(
            tempDir,
            [manifestFile],
            [manifestFile],
            new Dictionary<string, string> { ["verified.bin"] = "trusted-hash" },
            new Dictionary<string, PlannedFileHash>(),
            _ => { },
            CancellationToken.None);

        Assert.Empty(failed);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(targetPath));
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public async Task InstallDownloadedFilesAsync_WhenPlanningHashedUntouchedFile_SkipsRecheck()
    {
        // 契约（AUD-PERF-011）：规划阶段（修复差异）已整读过的、本会话未写入的文件，
        // 安装阶段凭「哈希 + 大小/最后写入时间见证」跳过第二次整读。落入文件的
        // 内容与 manifest 哈希故意不一致：只有跳过重读才会被接受。
        var targetPath = Path.Combine(tempDir, "planned.bin");
        await File.WriteAllBytesAsync(targetPath, [9, 9, 9]);
        var planned = new Dictionary<string, PlannedFileHash>(StringComparer.Ordinal)
        {
            ["planned.bin"] = PlannedFileHash.Capture(targetPath, "planned-hash")
        };
        var manifestFile = new ManifestFile { Path = "planned.bin", Size = "3", Hash = "planned-hash" };

        var failed = await CreateExecutor().InstallDownloadedFilesAsync(
            tempDir,
            [manifestFile],
            [],
            new Dictionary<string, string>(),
            planned,
            _ => { },
            CancellationToken.None);

        Assert.Empty(failed);
    }

    [Fact]
    public async Task InstallDownloadedFilesAsync_WhenPlannedFileChangedAfterPlanning_RechecksAndFails()
    {
        // 与上一条完全相同的布局，只把最后写入时间推离见证值（内容与长度不动，
        // 因此只有时间戳能区分）。见证失效即必须重读——内容自愈语义不被削弱。
        var targetPath = Path.Combine(tempDir, "planned-stale.bin");
        await File.WriteAllBytesAsync(targetPath, [9, 9, 9]);
        var planned = new Dictionary<string, PlannedFileHash>(StringComparer.Ordinal)
        {
            ["planned-stale.bin"] = PlannedFileHash.Capture(targetPath, "planned-hash")
        };
        File.SetLastWriteTimeUtc(targetPath, planned["planned-stale.bin"].LastWriteUtc.AddMinutes(-1));
        var manifestFile = new ManifestFile { Path = "planned-stale.bin", Size = "3", Hash = "planned-hash" };

        var failed = await CreateExecutor().InstallDownloadedFilesAsync(
            tempDir,
            [manifestFile],
            [],
            new Dictionary<string, string>(),
            planned,
            _ => { },
            CancellationToken.None);

        _ = Assert.Single(failed);
    }

    [Fact]
    public async Task InstallDownloadedFilesAsync_WhenManyFilesVerifyInParallel_ReassemblesFailuresInManifestOrder()
    {
        // 契约（AUD-TEST-005）：有界并行校验在多文件清单下保持与串行版相同的语义——
        // 失败列表按清单顺序重组；失配的已下载 .tmp 删除、失配的未改动已安装文件在
        // 终路径删除（与官方启动器一致的损坏自愈）；通过文件照常搬移；进度每文件恰一次。
        const int fileCount = 12; // 高于并行度上限 8，覆盖信号量等待与按索引重组
        const int downloadedCount = 8;
        var crc64 = new Crc64Service();
        var manifestFiles = new List<ManifestFile>();
        var downloadedFiles = new List<ManifestFile>();
        var finalPaths = new string[fileCount];
        var tempPaths = new string[fileCount];
        for (var index = 0; index < fileCount; index++)
        {
            var name = $"file{index:D2}.bin";
            var correctBytes = new byte[] { (byte)index, 0xAA, 0x55 };
            finalPaths[index] = Path.Combine(tempDir, name);
            tempPaths[index] = DownloadExecutor.GetTempName(finalPaths[index]);
            await File.WriteAllBytesAsync(finalPaths[index], correctBytes);
            var manifestFile = new ManifestFile
            {
                Path = name,
                Size = "3",
                Hash = await crc64.ComputeFileAsync(finalPaths[index])
            };
            manifestFiles.Add(manifestFile);
            if (index < downloadedCount)
            {
                // 已下载文件按生产布局只存在 .tmp；index 3 故意写错以触发失配
                File.Delete(finalPaths[index]);
                downloadedFiles.Add(manifestFile);
                await File.WriteAllBytesAsync(
                    tempPaths[index],
                    index == 3 ? new byte[] { 0xFF, 0xFF, 0xFF } : correctBytes);
            }
            else if (index == 9)
            {
                // 未改动已安装文件在终路径损坏：与官方一致的失配即删语义
                await File.WriteAllBytesAsync(finalPaths[index], new byte[] { 0xEE, 0xEE, 0xEE });
            }
        }

        var progressCount = 0;
        var failed = await CreateExecutor().InstallDownloadedFilesAsync(
            tempDir,
            manifestFiles,
            downloadedFiles,
            new Dictionary<string, string>(),
            new Dictionary<string, PlannedFileHash>(),
            _ => progressCount++,
            CancellationToken.None);

        Assert.Equal(2, failed.Count);
        Assert.Equal("file03.bin", failed[0].Path);
        Assert.Equal("file09.bin", failed[1].Path);
        Assert.Equal(manifestFiles[3].Hash, failed[0].Hash);
        Assert.Equal(manifestFiles[9].Hash, failed[1].Hash);
        Assert.False(File.Exists(tempPaths[3]));
        Assert.False(File.Exists(finalPaths[9]));
        for (var index = 0; index < fileCount; index++)
        {
            if (index is 3 or 9)
            {
                continue;
            }

            Assert.True(File.Exists(finalPaths[index]));
            if (index < downloadedCount)
            {
                Assert.False(File.Exists(tempPaths[index]));
            }
        }

        Assert.Equal(fileCount, progressCount);
    }

    [Fact]
    public async Task InstallDownloadedFilesAsync_WhenDownloadedTempIsMissing_MarksOnlyThatFileFailed()
    {
        // 缺失检查在并行路径上同样生效：缺失的已下载文件判失败且无需删除，
        // 其余已下载与未改动文件照常通过。
        var crc64 = new Crc64Service();
        var presentPath = Path.Combine(tempDir, "present.bin");
        var presentBytes = new byte[] { 1, 2, 3 };
        await File.WriteAllBytesAsync(presentPath, presentBytes);
        var present = new ManifestFile
        {
            Path = "present.bin",
            Size = "3",
            Hash = await crc64.ComputeFileAsync(presentPath)
        };
        File.Move(presentPath, DownloadExecutor.GetTempName(presentPath));
        var untouchedPath = Path.Combine(tempDir, "untouched.bin");
        await File.WriteAllBytesAsync(untouchedPath, new byte[] { 4, 5, 6 });
        var untouched = new ManifestFile
        {
            Path = "untouched.bin",
            Size = "3",
            Hash = await crc64.ComputeFileAsync(untouchedPath)
        };
        var missing = new ManifestFile { Path = "missing.bin", Size = "3", Hash = "whatever" };

        var failed = await CreateExecutor().InstallDownloadedFilesAsync(
            tempDir,
            [present, missing, untouched],
            [present, missing],
            new Dictionary<string, string>(),
            new Dictionary<string, PlannedFileHash>(),
            _ => { },
            CancellationToken.None);

        _ = Assert.Single(failed);
        Assert.Equal("missing.bin", failed[0].Path);
        Assert.True(File.Exists(untouchedPath));
    }

    [Fact]
    public async Task InstallDownloadedFilesAsync_WhenManyFilesSharePercentBuckets_DeduplicatesProgress()
    {
        // 契约（AUD-PERF-007）：校验阶段逐文件回调经百分比门控去重——400 个文件
        // 挤在 101 个百分比桶里，消费方只应收到桶变化的那几次，而非每文件一次。
        //
        // 回调来自 ≤8 个并行 worker，所以收集必须自己加锁：List<T> 不是线程安全的，并发 Add
        // 会重复或丢条目。断言也只取与到达顺序无关的性质——单调判据决定「投递什么」，但「谁先
        // Add」由线程竞速决定，且落后于更高桶的 0 会被单调判据丢掉（completed 1、2 都算 0，
        // 3 就算 1；前者若排在后者之后到达即被压掉），因此不能假设首项是 0
        // （2026-09-15 深夜 CI 复查：断言 delivered[0] == 0 在 CI 上偶发红，实到 1）。
        const int fileCount = 400;
        var crc64 = new Crc64Service();
        var manifestFiles = new List<ManifestFile>();
        for (var index = 0; index < fileCount; index++)
        {
            var name = $"bulk{index:D4}.bin";
            var path = Path.Combine(tempDir, name);
            await File.WriteAllBytesAsync(path, new[] { (byte)index });
            manifestFiles.Add(new ManifestFile
            {
                Path = name,
                Size = "1",
                Hash = await crc64.ComputeFileAsync(path)
            });
        }

        var delivered = new List<int>();
        var deliveredLock = new object();
        var failed = await CreateExecutor().InstallDownloadedFilesAsync(
            tempDir,
            manifestFiles,
            [],
            new Dictionary<string, string>(),
            new Dictionary<string, PlannedFileHash>(),
            percent =>
            {
                lock (deliveredLock)
                {
                    delivered.Add(percent);
                }
            },
            CancellationToken.None);

        Assert.Empty(failed);
        Assert.True(
            delivered.Count < fileCount,
            $"progress delivered {delivered.Count} times; percent gate did not collapse repeats.");
        // 单调判据下每个桶至多投递一次——这条与到达顺序无关，是门控本身的不变量。
        Assert.Equal(delivered.Count, delivered.Distinct().Count());
        Assert.All(delivered, percent => Assert.InRange(percent, 0, 100));
        // 100 是最大桶，不会被单调判据压掉（completed 399、400 都算 100，谁先赢下它都投得出去）。
        Assert.Contains(100, delivered);
    }

    [Fact]
    public void RemoveFiles_WhenFileIsReadOnly_DeletesFile()
    {
        var filePath = Path.Combine(tempDir, "removed.bin");
        File.WriteAllText(filePath, "data");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        DownloadExecutor.RemoveFiles(
            tempDir,
            [new ManifestFile { Path = "removed.bin" }],
            progress: null);

        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task DownloadFilesAsync_RespectsParallelConcurrencyLimit()
    {
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        var files = Enumerable.Range(0, 15)
            .Select(index => new ManifestFile { Path = $"file{index}.bin", Size = "10", Hash = "hash" })
            .ToArray();
        // 用共享替身 + 闭包统计并发峰值，等价于原先的 TrackingFileDownloadService。
        var currentConcurrency = 0;
        var maximumConcurrency = 0;
        var transferService = new StubFileDownloadService(async (request, operationControl, cancellationToken) =>
        {
            var value = Interlocked.Increment(ref currentConcurrency);
            while (true)
            {
                var current = Volatile.Read(ref maximumConcurrency);
                if (current >= value
                    || Interlocked.CompareExchange(ref maximumConcurrency, value, current) == current)
                {
                    break;
                }
            }

            try
            {
                // A shared batch transport is supplied through the control object.
                Assert.NotNull(operationControl.Transport);
                await operationControl.ReportProgressAsync(10, cancellationToken);
                await Task.Delay(50, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref currentConcurrency);
            }
        });
        var progressCount = 0;
        var executor = new DownloadExecutor(
            transferService,
            new Crc64Service(),
            new StubDownloadTransportSource(),
            new LocalDiagnostics(),
            () => Task.CompletedTask,
            () => false);

        await executor.DownloadFilesAsync(
            gamePath,
            new CdnConfigResponse
            {
                PrimaryCdn = "https://primary.example.invalid",
                BackUpCdn = "https://backup.example.invalid"
            },
            "source",
            files,
            ProxyModes.Direct,
            speedLimitBytesPerSec: 0,
            GameOperationKind.Download,
            _ => progressCount++,
            CancellationToken.None);

        Assert.True(maximumConcurrency > 1);
        Assert.True(maximumConcurrency <= 10);
        Assert.True(progressCount > 0);
    }

    [Fact]
    public async Task DownloadFilesAsync_WhenProgressIsReportedAndReset_CountsBytesInMemory()
    {
        // 守卫（进度内存计数）：正常块按上报字节累加，不再逐块磁盘 stat；
        // 重置路径从磁盘重采样权威长度。前置临时文件 8 字节（续传语义），
        // 块 4 → 内存 12；重置 → 磁盘权威 8；块 8 → 16 = 全量完成。
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        var targetPath = GamePathValidator.GetSafeFilePath(gamePath, "progress.bin");
        await File.WriteAllBytesAsync(DownloadExecutor.GetTempName(targetPath), new byte[8]);
        var files = new[] { new ManifestFile { Path = "progress.bin", Size = "16", Hash = "hash" } };
        var transferService = new StubFileDownloadService(async (request, operationControl, cancellationToken) =>
        {
            await operationControl.ReportProgressAsync(4, cancellationToken);
            await operationControl.ReportProgressResetAsync(cancellationToken);
            await operationControl.ReportProgressAsync(8, cancellationToken);
        });
        // 进度播种与重采样都经 fileDownloadService.GetExistingDownloadedSize：
        // 假体按真实 .tmp 长度语义返回盘上字节（缺失或超长记 0）。
        transferService.ExistingSize = (path, expectedSize) =>
        {
            var length = File.Exists(path) ? new FileInfo(path).Length : 0;
            return length <= expectedSize ? length : 0;
        };
        var progressSnapshots = new List<GameOperationProgress>();
        var executor = new DownloadExecutor(
            transferService,
            new Crc64Service(),
            new StubDownloadTransportSource(),
            new LocalDiagnostics(),
            () => Task.CompletedTask,
            () => false);

        await executor.DownloadFilesAsync(
            gamePath,
            new CdnConfigResponse
            {
                PrimaryCdn = "https://primary.example.invalid",
                BackUpCdn = "https://backup.example.invalid"
            },
            "source",
            files,
            ProxyModes.Direct,
            speedLimitBytesPerSec: 0,
            GameOperationKind.Download,
            progressSnapshots.Add,
            CancellationToken.None);

        Assert.Equal(16, progressSnapshots[^1].DownloadedSize);
        Assert.Equal(16, progressSnapshots[^1].TotalSize);
        // 重置回退必须可见：回退快照携带磁盘权威值 8（丢弃了内存计数的 12）。
        Assert.Contains(progressSnapshots, snapshot => snapshot.DownloadedSize == 8);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("sub/..")]
    public async Task DownloadFilesAsync_WhenEntryCanonicalizesToGameRoot_ThrowsAndWritesNothingOutside(string relativePath)
    {
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        var files = new[] { new ManifestFile { Path = relativePath, Size = "10", Hash = "hash" } };

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateExecutor().DownloadFilesAsync(
            gamePath,
            new CdnConfigResponse
            {
                PrimaryCdn = "https://primary.example.invalid",
                BackUpCdn = "https://backup.example.invalid"
            },
            "source",
            files,
            ProxyModes.Direct,
            speedLimitBytesPerSec: 0,
            GameOperationKind.Download,
            _ => { },
            CancellationToken.None));

        // 修复前 GetTempName(root) 会得到 <gamePath>.tmp，写在游戏目录之外。
        Assert.False(File.Exists(gamePath + ".tmp"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(gamePath)!));
    }

    private DownloadExecutor CreateExecutor() =>
        new(
            new StubFileDownloadService(),
            new Crc64Service(),
            new StubDownloadTransportSource(),
            new LocalDiagnostics(),
            () => Task.CompletedTask,
            () => false);

    /// <summary>
    /// 最简传输源替身：每批交出一个共享 <see cref="StubDownloadTransport"/>，
    /// 并记录 CreateAsync 的调用次数与代理模式。
    /// </summary>
    private sealed class StubDownloadTransportSource : IDownloadTransportSource
    {
        public int CreateCount { get; private set; }

        public List<string> RequestedProxyModes { get; } = [];

        public Task<IDownloadTransport> CreateAsync(string proxyMode, CancellationToken cancellationToken)
        {
            CreateCount++;
            RequestedProxyModes.Add(proxyMode);
            return Task.FromResult<IDownloadTransport>(new StubDownloadTransport());
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
