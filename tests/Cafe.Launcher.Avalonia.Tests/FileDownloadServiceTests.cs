using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// <see cref="FileDownloadService"/> 传输层语义的聚焦测试：非 2xx 的域名轮换重试、
/// 读取中途取消、短于声明长度的截断响应体，以及目标临时文件已存在时的续传语义。
/// 所有请求均由假 <see cref="HttpMessageHandler"/> 应答，测试不触网，域名一律 .invalid。
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
        var handler = new FixedStatusHandler(statusCode);
        using var client = new HttpClient(handler);
        var downloader = CreateService();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => downloader.DownloadAsync(
            targetPath,
            CreateCdnConfig(),
            "source",
            expectedBytes.Length,
            "0",
            "file.bin",
            client,
            () => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            false,
            CancellationToken.None));

        // 异常信息必须保留 HTTP 状态码以便诊断。
        Assert.Contains(expectedStatusText, exception.Message, StringComparison.Ordinal);
        Assert.Equal(FileDownloadService.RetryDomainOrder.Length, handler.RequestCount);
        Assert.Equal(
            FileDownloadService.RetryDomainOrder
                .Select(retryType => retryType == 0 ? BackupHost : PrimaryHost)
                .ToArray(),
            handler.RequestHosts);
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
        var handler = new GatedStreamHandler(expectedBytes, deliveredBytes);
        using var client = new HttpClient(handler);
        var downloader = CreateService();
        using var cancellationSource = new CancellationTokenSource();
        var firstChunkReported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var downloadTask = downloader.DownloadAsync(
            targetPath,
            CreateCdnConfig(),
            "source",
            expectedBytes.Length,
            "0",
            "file.bin",
            client,
            () => Task.CompletedTask,
            (_, _) =>
            {
                firstChunkReported.TrySetResult();
                return Task.CompletedTask;
            },
            false,
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
    public async Task DownloadAsync_WhenBodyIsShorterThanDeclaredContentLength_FailsCrcCheckAndRemovesPartialFile()
    {
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        var hashPath = Path.Combine(tempDir, "hash-source.bin");
        await File.WriteAllBytesAsync(hashPath, expectedBytes);
        var expectedHash = await new Crc64Service().ComputeFileAsync(hashPath);
        var handler = new TruncatedBodyHandler(expectedBytes, deliveredBytes: 4);
        using var client = new HttpClient(handler);
        var downloader = CreateService();

        // 实现不直接比对 Content-Length 与落盘字节数，短响应体最终由 CRC64 校验兜底：
        // 十次尝试全部截断后抛出 InvalidDataException，且部分文件不留盘。
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(
            targetPath,
            CreateCdnConfig(),
            "source",
            expectedBytes.Length,
            expectedHash,
            "file.bin",
            client,
            () => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            false,
            CancellationToken.None));

        Assert.Contains("CRC64 mismatch after all retries", exception.Message, StringComparison.Ordinal);
        Assert.Contains("file.bin", exception.Message, StringComparison.Ordinal);
        Assert.Equal(FileDownloadService.RetryDomainOrder.Length, handler.RequestCount);
        Assert.False(File.Exists(targetPath));
    }

    [Fact]
    public async Task DownloadAsync_WhenTemporaryFileExistsAndServerHonorsRange_AppendsFromExistingLength()
    {
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        // 目标临时文件已有前 4 个字节：服务必须从既有长度续传而不是覆盖重来。
        await File.WriteAllBytesAsync(targetPath, expectedBytes[..4]);
        var hashPath = Path.Combine(tempDir, "hash-source.bin");
        await File.WriteAllBytesAsync(hashPath, expectedBytes);
        var expectedHash = await new Crc64Service().ComputeFileAsync(hashPath);
        var handler = new ResumingRangeHandler(expectedBytes);
        using var client = new HttpClient(handler);
        var downloader = CreateService();

        await downloader.DownloadAsync(
            targetPath,
            CreateCdnConfig(),
            "source",
            expectedBytes.Length,
            expectedHash,
            "file.bin",
            client,
            () => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            false,
            CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(4, handler.RequestedRangeFrom);
        // 既有字节保留 + 追加剩余字节，最终内容与期望完全一致。
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(targetPath));
    }

    private static FileDownloadService CreateService() => new(
        new Crc64Service(),
        new LocalDiagnostics(),
        RemoteHttpUrlValidator.CreateForTesting());

    private static CdnConfigResponse CreateCdnConfig() => new()
    {
        PrimaryCdn = $"https://{PrimaryHost}",
        BackUpCdn = $"https://{BackupHost}"
    };

    /// <summary>始终返回同一非 2xx 状态码，并记录请求次数与主机序列。</summary>
    private sealed class FixedStatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public List<string> RequestHosts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestHosts.Add(request.RequestUri?.Host ?? "");
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }

    /// <summary>以 200 OK 返回一个先交付部分字节、随后挂起直到取消的流。</summary>
    private sealed class GatedStreamHandler(byte[] content, int deliveredBytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new GatedReadStream(content, deliveredBytes))
            });
        }
    }

    /// <summary>
    /// 先交付 <paramref name="deliveredBytes"/> 字节，然后无限期挂起并依赖取消令牌
    /// 抛出 <see cref="OperationCanceledException"/>，模拟「下载到一半被用户取消」。
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

            // 字节预算用尽后挂起，直到测试取消令牌触发并在此抛出 OCE。
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

    /// <summary>声明完整 Content-Length 但只提供前 N 字节，模拟被截断的响应体。</summary>
    private sealed class TruncatedBodyHandler(byte[] content, int deliveredBytes) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var truncated = new StreamContent(new FixedLengthReadStream(content, deliveredBytes))
            {
                Headers = { ContentLength = content.Length }
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = truncated
            });
        }
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

    /// <summary>对 Range 请求回以合法 206 分片响应，模拟支持断点续传的 CDN。</summary>
    private sealed class ResumingRangeHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public long? RequestedRangeFrom { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestedRangeFrom = request.Headers.Range?.Ranges.Single().From;
            var partialContent = new ByteArrayContent(content[4..]);
            partialContent.Headers.ContentRange =
                new ContentRangeHeaderValue(4, content.Length - 1, content.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = partialContent
            });
        }
    }
}
