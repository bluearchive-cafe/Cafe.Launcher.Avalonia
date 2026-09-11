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
                // A shared lease client is supplied through the control object.
                Assert.NotNull(operationControl.HttpClient);
                await operationControl.ReportProgressAsync(10, cancellationToken);
                await Task.Delay(50, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref currentConcurrency);
            }
        });
        using var leaseSource = new FixedHttpClientLeaseSource(new HttpClientHandler(), null, null);
        var progressCount = 0;
        var executor = new DownloadExecutor(
            transferService,
            new Crc64Service(),
            leaseSource,
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
        using var leaseSource = new FixedHttpClientLeaseSource(new HttpClientHandler(), null, null);
        var progressSnapshots = new List<GameOperationProgress>();
        var executor = new DownloadExecutor(
            transferService,
            new Crc64Service(),
            leaseSource,
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
            new FixedHttpClientLeaseSource(new HttpClientHandler(), null, null),
            new LocalDiagnostics(),
            () => Task.CompletedTask,
            () => false);

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
