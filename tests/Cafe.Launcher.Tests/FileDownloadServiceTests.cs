using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.Tests;

/// <summary>
/// <see cref="FileDownloadService"/> .tmp 状态机与批级传输接缝的聚焦测试：非 2xx 的
/// 域名轮换重试、读取中途取消、短于声明长度的截断响应体、超长/部分临时文件的续传
/// 与清理语义、Content-Range 校验、服务端忽略 Range 时的重建与进度重置，以及本地
/// 磁盘 IO 失败不重试、保留 .tmp，还有 <see cref="IFileDownloadService.GetExistingDownloadedSize"/>
/// 的长度判定。所有请求由共享替身 <see cref="StubDownloadTransport"/> 应答，测试不触网，
/// 域名一律 .invalid。
/// </summary>
public sealed class FileDownloadServiceTests : IDisposable
{
    private const string PrimaryHost = "primary.example.invalid";
    private const string BackupHost = "backup.example.invalid";

    private readonly TestDirectory tempDir = TestDirectory.Create();

    public void Dispose()
    {
        tempDir.Dispose();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "404")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    public async Task DownloadAsync_WhenServerRespondsNonSuccess_ThrowsHttpRequestExceptionAfterAllRetries(
        HttpStatusCode statusCode,
        string expectedStatusText)
    {
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
    public async Task DownloadAsync_WhenConnectionTimesOut_RetriesAndResumesPartialFile()
    {
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var bytes = Encoding.UTF8.GetBytes("complete-content");
        await File.WriteAllBytesAsync(targetPath, bytes[..4]);
        var expectedHash = await ComputeExpectedHashAsync(bytes);
        var attempts = 0;
        using var transport = new StubDownloadTransport((_, _) =>
        {
            if (++attempts == 1)
            {
                throw new TaskCanceledException("Connect timeout", new TimeoutException());
            }

            var content = new ByteArrayContent(bytes[4..]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(4, bytes.Length - 1, bytes.Length);
            return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
        });

        var outcome = await CreateService().DownloadAsync(
            CreateRequest(targetPath, bytes.Length, expectedHash), CreateControl(transport), CancellationToken.None);

        Assert.Equal(DownloadOutcomeKind.Transferred, outcome.Kind);
        Assert.Equal(new long?[] { 4, 4 }, transport.RangeStarts);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(targetPath));
    }

    [Fact]
    public async Task DownloadAsync_WhenEveryRequestTimesOut_ExhaustsDomainsAndKeepsPartialFile()
    {
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        await File.WriteAllBytesAsync(targetPath, new byte[4]);
        using var transport = new StubDownloadTransport((_, _) =>
            throw new TaskCanceledException("Header timeout", new TimeoutException()));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => CreateService().DownloadAsync(
            CreateRequest(targetPath, 16, "0"), CreateControl(transport), CancellationToken.None));

        Assert.IsType<TaskCanceledException>(exception.InnerException);
        Assert.Equal(ExpectedHostSequence(), transport.RequestedUris.Select(uri => uri.Host).ToArray());
        Assert.Equal(4, new FileInfo(targetPath).Length);
    }

    [Fact]
    public async Task DownloadAsync_WhenCallerCancelsBeforeHeaders_DoesNotRetryOrDeletePartialFile()
    {
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        await File.WriteAllBytesAsync(targetPath, new byte[4]);
        using var cancellation = new CancellationTokenSource();
        using var transport = new StubDownloadTransport((_, _) =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService().DownloadAsync(
            CreateRequest(targetPath, 16, "0"), CreateControl(transport), cancellation.Token));

        Assert.Single(transport.RequestedUris);
        Assert.Equal(4, new FileInfo(targetPath).Length);
    }

    [Fact]
    public async Task DownloadAsync_WhenCancelledMidStream_PropagatesCancellationAndKeepsPartialFile()
    {
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        const int deliveredBytes = 4;
        using var transport = new StubDownloadTransport((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(ScriptedReadStream.StallingAfter(expectedBytes, deliveredBytes))
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
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        using var transport = new StubDownloadTransport((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(ScriptedReadStream.StallingAfter(expectedBytes, deliveredBytes: 0))
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
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        var expectedHash = await ComputeExpectedHashAsync(expectedBytes);
        var resetCount = 0;
        using var transport = new StubDownloadTransport((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            // 声明完整 Content-Length 但只提供前 4 字节，模拟被截断的响应体。
            Content = new StreamContent(ScriptedReadStream.EndingAfter(expectedBytes, deliveredBytes: 4))
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
        var verificationProgress = new List<long>();

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
                }) with { ReportVerificationProgress = (bytes, _) => verificationProgress.Add(bytes) },
            CancellationToken.None);

        Assert.Equal(DownloadOutcomeKind.Transferred, outcome.Kind);
        Assert.Equal(expectedHash, outcome.Crc64);
        Assert.Single(transport.RequestedUris);
        // 进度与暂停等待都经由 control：每块读取前先等待暂停，再按写入字节上报。
        Assert.True(pauseCount >= 1);
        Assert.Equal(expectedBytes.Length, progressTotal);
        Assert.Equal(0, verificationProgress[0]);
        Assert.Contains((long)expectedBytes.Length, verificationProgress);
    }

    [Fact]
    public async Task DownloadAsync_WhenTempFileAlreadyComplete_ReturnsAlreadyCompleteOutcomeWithoutTransfer()
    {
        // 断点续传「已下满」早退路径不做哈希：Crc64 为空，安装阶段必须自行校验。
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

    [Fact]
    public async Task DownloadAsync_WhenServerIgnoresRangeAndReturnsFullBody_RebuildsTempFileAndResetsProgressBeforeWriting()
    {
        // AUD-DL-002：续传请求收到 200（服务端忽略 Range）时必须重建 .tmp 并在写入前重置
        // 进度；否则批级计数会把整份正文叠加在按旧长度播种的计数上，提前报满并侵占其他文件。
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        const int existingBytes = 4;
        await File.WriteAllBytesAsync(targetPath, expectedBytes[..existingBytes]);
        var expectedHash = await ComputeExpectedHashAsync(expectedBytes);
        using var transport = new StubDownloadTransport((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expectedBytes)
        });
        var events = new List<string>();
        var lengthAtReset = -1L;
        var reportedBytes = 0L;

        var outcome = await CreateService().DownloadAsync(
            CreateRequest(targetPath, expectedBytes.Length, expectedHash),
            CreateControl(
                transport,
                progress: (bytes, _) =>
                {
                    reportedBytes += bytes;
                    events.Add("progress");
                    return Task.CompletedTask;
                },
                reset: _ =>
                {
                    events.Add("reset");
                    lengthAtReset = File.Exists(targetPath) ? new FileInfo(targetPath).Length : -1;
                    return Task.CompletedTask;
                }),
            CancellationToken.None);

        Assert.Equal(DownloadOutcomeKind.Transferred, outcome.Kind);
        Assert.Equal(expectedHash, outcome.Crc64);
        Assert.Single(transport.RequestedUris);
        // 仍先按既有长度发起续传请求；是服务端的 200 触发了重建。
        Assert.Equal(new long?[] { existingBytes }, transport.RangeStarts);
        // 重置先于任何新字节上报，且此刻 .tmp 已被截断为空文件。
        Assert.Equal("reset", events[0]);
        Assert.Equal(0, lengthAtReset);
        Assert.Equal((long)expectedBytes.Length, reportedBytes);
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(targetPath));
    }

    [Fact]
    public async Task DownloadAsync_WhenTempFileIsLockedDuringWrite_PropagatesIOExceptionWithoutRetryOrCleanup()
    {
        // AUD-DL-001：写盘阶段的本地 IO 失败（杀软或另一写入方独占文件）不是网络失败：
        // 只尝试一次、原样抛出 IOException，且不删除可续传的 .tmp、不重置进度。
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        const int existingBytes = 4;
        await File.WriteAllBytesAsync(targetPath, expectedBytes[..existingBytes]);
        using var transport = new StubDownloadTransport((_, _) =>
        {
            var partialContent = new ByteArrayContent(expectedBytes[existingBytes..]);
            partialContent.Headers.ContentRange =
                new ContentRangeHeaderValue(existingBytes, expectedBytes.Length - 1, expectedBytes.Length);
            return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = partialContent };
        });
        var resetCount = 0;

        using (new FileStream(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => CreateService().DownloadAsync(
                CreateRequest(targetPath, expectedBytes.Length, "0"),
                CreateControl(transport, reset: _ =>
                {
                    resetCount++;
                    return Task.CompletedTask;
                }),
                CancellationToken.None));
        }

        Assert.Single(transport.RequestedUris);
        Assert.Equal(new long?[] { existingBytes }, transport.RangeStarts);
        Assert.Equal(0, resetCount);
        Assert.Equal(expectedBytes[..existingBytes], await File.ReadAllBytesAsync(targetPath));
    }

    [Fact]
    public async Task DownloadAsync_WhenWriteIsDenied_PropagatesUnauthorizedAccessExceptionWithoutRetryOrCleanup()
    {
        // AUD-DL-001：落点不可写（以同名目录代表无法作为文件打开的路径）原样上抛
        // UnauthorizedAccessException，会话层据此给出权限指引，而不是报成网络错误。
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        Directory.CreateDirectory(targetPath);
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        using var transport = new StubDownloadTransport((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expectedBytes)
        });
        var resetCount = 0;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateService().DownloadAsync(
            CreateRequest(targetPath, expectedBytes.Length, "0"),
            CreateControl(transport, reset: _ =>
            {
                resetCount++;
                return Task.CompletedTask;
            }),
            CancellationToken.None));

        Assert.Single(transport.RequestedUris);
        Assert.Equal(0, resetCount);
        Assert.True(Directory.Exists(targetPath));
    }

    [Fact]
    public async Task DownloadAsync_WhenLocalVerificationHitsDiskFull_PropagatesIOExceptionForSessionMapping()
    {
        // AUD-DL-001：磁盘满的 IOException（HResult 0x80070070）不再被包成网络错误，
        // 会话层的「磁盘空间不足」映射因此可达；.tmp 保留。
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        const int diskFullHResult = unchecked((int)0x80070070);
        var diskFull = new IOException("There is not enough space on the disk.", diskFullHResult);
        using var transport = new StubDownloadTransport((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expectedBytes)
        });
        var resetCount = 0;
        var downloader = CreateService(crc64Service: new ThrowingCrc64Service(diskFull));

        var exception = await Assert.ThrowsAsync<IOException>(() => downloader.DownloadAsync(
            CreateRequest(targetPath, expectedBytes.Length, "0"),
            CreateControl(transport, reset: _ =>
            {
                resetCount++;
                return Task.CompletedTask;
            }),
            CancellationToken.None));

        Assert.Same(diskFull, exception);
        Assert.Equal(diskFullHResult, exception.HResult);
        Assert.Single(transport.RequestedUris);
        Assert.Equal(0, resetCount);
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(targetPath));
    }

    [Fact]
    public async Task DownloadAsync_WhenNetworkBodyReadBreaksMidStream_RetriesAndResumes()
    {
        // 对端在正文中途断开（IOException）属于网络失败，仍按既有域名次序重试并续传；
        // AUD-DL-001 的本地 IO 判据不能把这条路径一并排除。
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        var expectedHash = await ComputeExpectedHashAsync(expectedBytes);
        const int deliveredBytes = 4;
        var attempt = 0;
        using var transport = new StubDownloadTransport((_, _) =>
        {
            if (++attempt == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(ScriptedReadStream.ThrowingAfter(
                        expectedBytes,
                        deliveredBytes,
                        new IOException("The response ended prematurely.")))
                };
            }

            var partialContent = new ByteArrayContent(expectedBytes[deliveredBytes..]);
            partialContent.Headers.ContentRange =
                new ContentRangeHeaderValue(deliveredBytes, expectedBytes.Length - 1, expectedBytes.Length);
            return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = partialContent };
        });

        var outcome = await CreateService().DownloadAsync(
            CreateRequest(targetPath, expectedBytes.Length, expectedHash),
            CreateControl(transport),
            CancellationToken.None);

        Assert.Equal(DownloadOutcomeKind.Transferred, outcome.Kind);
        Assert.Equal(2, transport.RequestedUris.Count);
        Assert.Equal(new long?[] { null, deliveredBytes }, transport.RangeStarts);
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(targetPath));
    }

    [Theory]
    [InlineData(4, 16)]
    [InlineData(16, 16)]
    public void GetExistingDownloadedSize_WhenTempFileExistsWithinExpectedSize_ReturnsFileLength(
        int existingBytes,
        long expectedSize)
    {
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        File.WriteAllBytes(targetPath, new byte[existingBytes]);
        var downloader = CreateService();

        Assert.Equal((long)existingBytes, downloader.GetExistingDownloadedSize(targetPath, expectedSize));
    }

    [Fact]
    public void GetExistingDownloadedSize_WhenTempFileIsMissing_ReturnsZero()
    {
        var downloader = CreateService();

        Assert.Equal(0, downloader.GetExistingDownloadedSize(Path.Combine(tempDir, "missing.bin.tmp"), 16));
    }

    [Fact]
    public void GetExistingDownloadedSize_WhenTempFileExceedsExpectedSize_ReturnsZero()
    {
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        File.WriteAllBytes(targetPath, new byte[20]);
        var downloader = CreateService();

        Assert.Equal(0, downloader.GetExistingDownloadedSize(targetPath, 16));
    }

    [Fact]
    public void GetExistingDownloadedSize_WhenExpectedSizeIsNotPositive_ReturnsZero()
    {
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        File.WriteAllBytes(targetPath, new byte[4]);
        var downloader = CreateService();

        Assert.Equal(0, downloader.GetExistingDownloadedSize(targetPath, 0));
    }

    private FileDownloadService CreateService(
        TimeSpan? idleReadTimeout = null,
        ICrc64Service? crc64Service = null) => new(
        crc64Service ?? new Crc64Service(),
        new LocalDiagnostics(),
        idleReadTimeout);

    /// <summary>在本地校验阶段抛出指定失败的 CRC64 替身，用于注入磁盘/权限类 IO 异常。</summary>
    private sealed class ThrowingCrc64Service(Exception failure) : ICrc64Service
    {
        public Task<string> ComputeFileAsync(
            string filePath,
            Action<int>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<string>(failure);

        public Task<string> ComputeFileAsync(
            string filePath,
            Action<int>? progress,
            Action<long, long>? byteProgress,
            CancellationToken cancellationToken) =>
            Task.FromException<string>(failure);
    }

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
}
