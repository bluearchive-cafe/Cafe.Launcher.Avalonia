using System.Net.Http;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// <see cref="ResponseBodyReader"/> 的空闲读预算语义：停滞转可重试的
/// HttpRequestException、调用方取消照常传播、正常进展不受影响。
/// </summary>
public sealed class ResponseBodyReaderTests
{
    [Fact]
    public async Task ReadAsync_WhenStreamNeverProducesBytes_ThrowsHttpRequestExceptionAndKeepsCallerTokenLive()
    {
        using var callerCts = new CancellationTokenSource();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => ResponseBodyReader.ReadAsync(
            new NeverReadingStream(),
            new byte[16],
            callerCts.Token,
            TimeSpan.FromMilliseconds(50)));

        Assert.Contains("stalled", exception.Message, StringComparison.Ordinal);
        // 预算超时不得污染调用方的取消令牌：上层重试仍可复用该令牌继续请求。
        Assert.False(callerCts.IsCancellationRequested);
    }

    [Fact]
    public async Task ReadAsync_WhenCallerCancels_ThrowsOperationCanceledException()
    {
        using var callerCts = new CancellationTokenSource();
        var readTask = ResponseBodyReader.ReadAsync(
            new NeverReadingStream(),
            new byte[16],
            callerCts.Token,
            TimeSpan.FromSeconds(30));

        callerCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);
    }

    [Fact]
    public async Task ReadAsync_WhenStreamYieldsBytes_ReturnsReadCount()
    {
        var read = await ResponseBodyReader.ReadAsync(
            new OneShotStream(5),
            new byte[16],
            CancellationToken.None,
            TimeSpan.FromSeconds(1));

        Assert.Equal(5, read);
    }

    /// <summary>挂起直到令牌触发，模拟头部返回后正文零字节停滞的连接。</summary>
    private sealed class NeverReadingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
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

    private sealed class OneShotStream(int count) : Stream
    {
        private bool read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (read)
            {
                return ValueTask.FromResult(0);
            }

            read = true;
            var bytes = Math.Min(count, buffer.Length);
            buffer.Span[..bytes].Clear();
            return ValueTask.FromResult(bytes);
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
}
