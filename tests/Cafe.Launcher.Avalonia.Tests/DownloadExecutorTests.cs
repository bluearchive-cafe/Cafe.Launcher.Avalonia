using Cafe.Launcher.Avalonia.Features.GameOperations;
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
            _ => { },
            CancellationToken.None);

        Assert.Empty(failed);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(targetPath));
        Assert.False(File.Exists(tempPath));
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
