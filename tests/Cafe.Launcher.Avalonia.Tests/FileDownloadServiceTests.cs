using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// <see cref="FileDownloadService"/> .tmp 状态机与批级传输接缝的聚焦测试：非 2xx 的
/// 域名轮换重试、读取中途取消、短于声明长度的截断响应体、超长/部分临时文件的续传
/// 与清理语义、Content-Range 校验，以及 <see cref="IFileDownloadService.GetExistingDownloadedSize"/>
/// 的长度判定。所有请求由共享替身 <see cref="StubDownloadTransport"/> 应答，测试不触网，
/// 域名一律 .invalid。
/// </summary>
public sealed class FileDownloadServiceTests : IDisposable
{
    private const string PrimaryHost = "primary.example.invalid";
    private const string BackupHost = "backup.example.invalid";

    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            const int maxRetries = 5;
            for (var attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                    break;
                }
                catch (IOException)
                {
                    if (attempt == maxRetries - 1)
                    {
                        throw;
                    }

                    Thread.Sleep(TimeSpan.FromMilliseconds(200 * (attempt + 1)));
                }
                catch (UnauthorizedAccessException)
                {
                    if (attempt == maxRetries - 1)
                    {
                        throw;
                    }

                    Thread.Sleep(TimeSpan.FromMilliseconds(200 * (attempt + 1)));
                }
            }
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "404")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    public async Task DownloadAsync_WhenServerRespondsNonSuccess_ThrowsHttpRequestExceptionAfterAllRetries(
        HttpStatusCode statusCode,
        string expectedStatusText)
    {
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        // 预置一份部分写入的临时文件，验证 HTTP 失败不会丢弃可用于续传的已下载数据。
        await File.WriteAllBytesAsync(targetPath, expectedBytes[..4]);
        using var transport = new StubDownloadTransport((_, _) => new HttpResponseMessage(statusCode));
        var downloader = CreateService();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => downloader.DownloadAsync(
            CreateRequest(targetPath, expectedBytes.Length, "0"),
            CreateControl(transport),
            CancellationToken.None));

        // 异常信息必须保留 HTTP 状态码以便诊断。
        Assert.Contains(expectedStatusText, exception.Message, StringComparison.Ordinal);
        Assert.Equal(FileDownloadService.RetryDomainOrder.Length, transport.RequestedUris.Count);
        Assert.Equal(
            ExpectedHostSequence(),
            transport.RequestedUris.Select(uri => uri.Host).ToArray());
        // 传输层失败属于可续传错误：实现刻意不清理已存在的部分临时文件。
        Assert.True(File.Exists(targetPath));
        Assert.Equal(expectedBytes[..4], await File.ReadAllBytesAsync(targetPath));
    }

    [Fact]
    public async Task DownloadAsync_WhenCancelledMidStream_PropagatesCancellationAndKeepsPartialFile()
    {
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        const int deliveredBytes = 4;
        using var transport = new StubDownloadTransport((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new GatedReadStream(expectedBytes, deliveredBytes))
        });
        var downloader = CreateService();
        using var cancellationSource = new CancellationTokenSource();
        var firstChunkReported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var downloadTask = downloader.DownloadAsync(
            CreateRequest(targetPath, expectedBytes.Length, "0"),
            CreateControl(
                transport,
                progress: (_, _) =>
                {
                    firstChunkReported.TrySetResult();
                    return Task.CompletedTask;
                }),
            cancellationSource.Token);

        // 门控：等第一个分块写盘并上报进度、读取循环挂在下一个 ReadAsync 上后再取消。
        await firstChunkReported.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => downloadTask.WaitAsync(TimeSpan.FromSeconds(5)));

        // 取消路径不做清理：部分文件按续传语义原样保留。
        Assert.True(File.Exists(targetPath));
        Assert.Equal(expectedBytes[..deliveredBytes], await File.ReadAllBytesAsync(targetPath));
    }

    [Fact]
    public async Task DownloadAsync_WhenBodyStallsAfterHeaders_ThrowsHttpRequestExceptionAfterAllRetries()
    {
        // 守卫（AUD-NET-001）：ResponseHeadersRead 之下 HttpClient.Timeout 只约束到响应头，
        // 正文零字节停滞必须由空闲读预算转成可重试的 HttpRequestException，
        // 而不是让下载会话在无异常、无进度的情况下无限挂起。
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        using var transport = new StubDownloadTransport((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new GatedReadStream(expectedBytes, deliveredBytes: 0))
        });
        var downloader = CreateService(TimeSpan.FromMilliseconds(200));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => downloader.DownloadAsync(
            CreateRequest(targetPath, expectedBytes.Length, "0"),
            CreateControl(transport),
            CancellationToken.None));

        Assert.Contains("stalled", exception.Message, StringComparison.Ordinal);
        Assert.Equal(FileDownloadService.RetryDomainOrder.Length, transport.RequestedUris.Count);
        // 停滞属网络类失败：已下载字节按续传语义保留。
        Assert.True(File.Exists(targetPath));
    }

    [Fact]
    public async Task DownloadAsync_WhenBodyIsShorterThanDeclaredContentLength_FailsCrcCheckAndRemovesPartialFile()
    {
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        var expectedHash = await ComputeExpectedHashAsync(expectedBytes);
        var resetCount = 0;
        using var transport = new StubDownloadTransport((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            // 声明完整 Content-Length 但只提供前 4 字节，模拟被截断的响应体。
            Content = new StreamContent(new FixedLengthReadStream(expectedBytes, deliveredBytes: 4))
            {
                Headers = { ContentLength = expectedBytes.Length }
            }
        });
        var downloader = CreateService();

        // 实现不直接比对 Content-Length 与落盘字节数，短响应体最终由 CRC64 校验兜底：
        // 十次尝试全部截断后抛出 InvalidDataException，且部分文件不留盘。
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(
            CreateRequest(targetPath, expectedBytes.Length, expectedHash),
            CreateControl(transport, reset: _ =>
            {
                resetCount++;
                return Task.CompletedTask;
            }),
            CancellationToken.None));

        Assert.Contains("CRC64 mismatch after all retries", exception.Message, StringComparison.Ordinal);
        Assert.Contains("file.bin", exception.Message, StringComparison.Ordinal);
        Assert.Equal(FileDownloadService.RetryDomainOrder.Length, transport.RequestedUris.Count);
        // 每次校验失败都删除临时文件并请求进度重采样。
        Assert.Equal(FileDownloadService.RetryDomainOrder.Length, resetCount);
        Assert.False(File.Exists(targetPath));
    }

    [Fact]
    public async Task DownloadAsync_WhenTemporaryFileExistsAndServerHonorsRange_AppendsFromExistingLength()
    {
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        const int existingBytes = 4;
        // 目标临时文件已有前 4 个字节：服务必须从既有长度续传而不是覆盖重来。
        await File.WriteAllBytesAsync(targetPath, expectedBytes[..existingBytes]);
        var expectedHash = await ComputeExpectedHashAsync(expectedBytes);
        using var transport = new StubDownloadTransport((_, _) =>
        {
            var partialContent = new ByteArrayContent(expectedBytes[existingBytes..]);
            partialContent.Headers.ContentRange =
                new ContentRangeHeaderValue(existingBytes, expectedBytes.Length - 1, expectedBytes.Length);
            return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = partialContent };
        });
        var downloader = CreateService();

        var outcome = await downloader.DownloadAsync(
            CreateRequest(targetPath, expectedBytes.Length, expectedHash),
            CreateControl(transport),
            CancellationToken.None);

        Assert.Single(transport.RequestedUris);
        // 续传请求必须从既有长度发起（Range.From == 4）。
        Assert.Equal(new long?[] { existingBytes }, transport.RangeStarts);
        // 既有字节保留 + 追加剩余字节，最终内容与期望完全一致。
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(targetPath));
        Assert.Equal(DownloadOutcomeKind.Transferred, outcome.Kind);
        Assert.Equal(expectedHash, outcome.Crc64);
    }

    [Fact]
    public async Task DownloadAsync_WhenTransferVerified_ReturnsTransferredOutcomeWithComputedCrc64()
    {
        // 下载即校验：结果携带本次计算的 CRC64，安装阶段据此跳过重复整读哈希。
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        var expectedHash = await ComputeExpectedHashAsync(expectedBytes);
        using var transport = new StubDownloadTransport((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expectedBytes)
        });
        var downloader = CreateService();
        var pauseCount = 0;
        var progressTotal = 0L;

        var outcome = await downloader.DownloadAsync(
            CreateRequest(targetPath, expectedBytes.Length, expectedHash),
            CreateControl(
                transport,
                pauseAwaiter: () =>
                {
                    pauseCount++;
                    return Task.CompletedTask;
                },
                progress: (bytes, _) =>
                {
                    progressTotal += bytes;
                    return Task.CompletedTask;
                }),
            CancellationToken.None);

        Assert.Equal(DownloadOutcomeKind.Transferred, outcome.Kind);
        Assert.Equal(expectedHash, outcome.Crc64);
        Assert.Single(transport.RequestedUris);
        // 进度与暂停等待都经由 control：每块读取前先等待暂停，再按写入字节上报。
        Assert.True(pauseCount >= 1);
        Assert.Equal(expectedBytes.Length, progressTotal);
    }

    [Fact]
    public async Task DownloadAsync_WhenTempFileAlreadyComplete_ReturnsAlreadyCompleteOutcomeWithoutTransfer()
    {
        // 断点续传「已下满」早退路径不做哈希：Crc64 为空，安装阶段必须自行校验。
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        await File.WriteAllBytesAsync(targetPath, expectedBytes);
        using var transport = new StubDownloadTransport();
        var downloader = CreateService();

        var outcome = await downloader.DownloadAsync(
            CreateRequest(targetPath, expectedBytes.Length, "irrelevant-hash"),
            CreateControl(transport),
            CancellationToken.None);

        Assert.Equal(DownloadOutcomeKind.AlreadyComplete, outcome.Kind);
        Assert.Null(outcome.Crc64);
        Assert.Empty(transport.RequestedUris);
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(targetPath));
    }

    [Fact]
    public async Task DownloadAsync_WhenTempFileExceedsExpectedSize_DeletesFileResetsProgressAndRestartsFromZero()
    {
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        // 超长临时文件不可信（可能是上次中断留下的错误内容）：必须整体丢弃。
        await File.WriteAllBytesAsync(targetPath, expectedBytes.Concat(expectedBytes).ToArray());
        var expectedHash = await ComputeExpectedHashAsync(expectedBytes);
        var resetCount = 0;
        using var transport = new StubDownloadTransport((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expectedBytes)
        });
        var downloader = CreateService();

        var outcome = await downloader.DownloadAsync(
            CreateRequest(targetPath, expectedBytes.Length, expectedHash),
            CreateControl(transport, reset: _ =>
            {
                resetCount++;
                return Task.CompletedTask;
            }),
            CancellationToken.None);

        // 超长文件被删除后从零重新下载：请求不带 Range 头。
        Assert.Single(transport.RequestedUris);
        Assert.Null(transport.RangeStarts.Single());
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(targetPath));
        Assert.Equal(1, resetCount);
        Assert.Equal(DownloadOutcomeKind.Transferred, outcome.Kind);
    }

    [Fact]
    public async Task DownloadAsync_WhenResumedContentRangeIsInvalid_DiscardsTempFileAndRestartsFromFullDownload()
    {
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        const int existingBytes = 4;
        var expectedHash = await ComputeExpectedHashAsync(expectedBytes);
        File.WriteAllBytes(targetPath, expectedBytes[..existingBytes]);
        var resetCount = 0;
        var attempt = 0;
        // 首次续传拿到 From 不等于既有长度的非法 Content-Range：临时文件被视为
        // 不可信数据丢弃并重置进度；之后的请求不带 Range，由完整内容接管。
        using var transport = new StubDownloadTransport((_, _) =>
        {
            attempt++;
            if (attempt == 1)
            {
                var invalidRangeContent = new ByteArrayContent(expectedBytes[existingBytes..]);
                invalidRangeContent.Headers.ContentRange =
                    new ContentRangeHeaderValue(2, expectedBytes.Length - 1, expectedBytes.Length);
                return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = invalidRangeContent };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expectedBytes)
            };
        });
        var downloader = CreateService();

        var outcome = await downloader.DownloadAsync(
            CreateRequest(targetPath, expectedBytes.Length, expectedHash),
            CreateControl(transport, reset: _ =>
            {
                resetCount++;
                return Task.CompletedTask;
            }),
            CancellationToken.None);

        Assert.Equal(DownloadOutcomeKind.Transferred, outcome.Kind);
        Assert.Equal(expectedHash, outcome.Crc64);
        // 只有首次请求带 Range（起点 = 既有长度）；丢弃后由完整下载恢复。
        Assert.Equal([4, null], transport.RangeStarts);
        Assert.Equal(2, transport.RequestedUris.Count);
        Assert.Equal(1, resetCount);
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(targetPath));
    }

    [Theory]
    [InlineData(4, 16)]
    [InlineData(16, 16)]
    public void GetExistingDownloadedSize_WhenTempFileExistsWithinExpectedSize_ReturnsFileLength(
        int existingBytes,
        long expectedSize)
    {
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        File.WriteAllBytes(targetPath, new byte[existingBytes]);
        var downloader = CreateService();

        Assert.Equal((long)existingBytes, downloader.GetExistingDownloadedSize(targetPath, expectedSize));
    }

    [Fact]
    public void GetExistingDownloadedSize_WhenTempFileIsMissing_ReturnsZero()
    {
        Directory.CreateDirectory(tempDir);
        var downloader = CreateService();

        Assert.Equal(0, downloader.GetExistingDownloadedSize(Path.Combine(tempDir, "missing.bin.tmp"), 16));
    }

    [Fact]
    public void GetExistingDownloadedSize_WhenTempFileExceedsExpectedSize_ReturnsZero()
    {
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        File.WriteAllBytes(targetPath, new byte[20]);
        var downloader = CreateService();

        Assert.Equal(0, downloader.GetExistingDownloadedSize(targetPath, 16));
    }

    [Fact]
    public void GetExistingDownloadedSize_WhenExpectedSizeIsNotPositive_ReturnsZero()
    {
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        File.WriteAllBytes(targetPath, new byte[4]);
        var downloader = CreateService();

        Assert.Equal(0, downloader.GetExistingDownloadedSize(targetPath, 0));
    }

    private FileDownloadService CreateService(TimeSpan? idleReadTimeout = null) => new(
        new Crc64Service(),
        new LocalDiagnostics(),
        idleReadTimeout);

    private async Task<string> ComputeExpectedHashAsync(byte[] content)
    {
        var hashPath = Path.Combine(tempDir, "hash-source.bin");
        await File.WriteAllBytesAsync(hashPath, content);
        return await new Crc64Service().ComputeFileAsync(hashPath);
    }

    private static FileDownloadRequest CreateRequest(string targetTempPath, long expectedSize, string expectedHash) => new(
        targetTempPath,
        CreateCdnConfig(),
        "source",
        expectedSize,
        expectedHash,
        "file.bin");

    private static FileDownloadOperationControl CreateControl(
        StubDownloadTransport transport,
        Func<Task>? pauseAwaiter = null,
        Func<long, CancellationToken, Task>? progress = null,
        Func<CancellationToken, Task>? reset = null) => new(
        transport,
        pauseAwaiter ?? (static () => Task.CompletedTask),
        progress ?? (static (_, _) => Task.CompletedTask),
        reset ?? (static _ => Task.CompletedTask));

    private static CdnConfigResponse CreateCdnConfig() => new()
    {
        PrimaryCdn = $"https://{PrimaryHost}",
        BackUpCdn = $"https://{BackupHost}"
    };

    private static string[] ExpectedHostSequence() =>
        FileDownloadService.RetryDomainOrder
            .Select(retryType => retryType == 0 ? BackupHost : PrimaryHost)
            .ToArray();

    /// <summary>
    /// 先交付 <paramref name="deliveredBytes"/> 字节，然后无限期挂起：取消令牌触发时抛出
    /// <see cref="OperationCanceledException"/>（模拟下载到一半被用户取消），空闲读预算
    /// 触发时由 <c>ResponseBodyReader</c> 转成停滞异常（模拟正文零字节停滞）。
    /// </summary>
    private sealed class GatedReadStream(byte[] content, int deliveredBytes) : Stream
    {
        private int position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => content.Length;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (position < deliveredBytes)
            {
                var bytesToCopy = Math.Min(buffer.Length, deliveredBytes - position);
                content.AsMemory(position, bytesToCopy).CopyTo(buffer);
                position += bytesToCopy;
                return bytesToCopy;
            }

            // 字节预算用尽后挂起，直到取消令牌（用户取消或空闲读预算）触发。
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("此替身仅支持异步读取。");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>交付指定字节数后干净地到达 EOF，头部声明的总长大于实际字节。</summary>
    private sealed class FixedLengthReadStream(byte[] content, int deliveredBytes) : Stream
    {
        private int position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => content.Length;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (position >= deliveredBytes)
            {
                return ValueTask.FromResult(0);
            }

            var bytesToCopy = Math.Min(buffer.Length, deliveredBytes - position);
            content.AsMemory(position, bytesToCopy).CopyTo(buffer);
            position += bytesToCopy;
            return ValueTask.FromResult(bytesToCopy);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (position >= deliveredBytes)
            {
                return 0;
            }

            var bytesToCopy = Math.Min(count, deliveredBytes - position);
            Array.Copy(content, position, buffer, offset, bytesToCopy);
            position += bytesToCopy;
            return bytesToCopy;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
