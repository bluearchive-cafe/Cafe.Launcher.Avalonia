using System.Net;
using System.Net.Http;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ImageCacheServiceTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public ImageCacheServiceTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public async Task GetCachedPathAsync_WhenCacheHashMatches_ReturnsCachedPath()
    {
        var bytes = "cached-image"u8.ToArray();
        var hash = await ComputeHashAsync(bytes);
        var cachePath = Path.Combine(tempDir, $"{hash}.cache");
        await File.WriteAllBytesAsync(cachePath, bytes);
        using var source = CreateSource(new ByteArrayContent(bytes));
        using var service = CreateService(source);

        var result = await service.GetCachedPathAsync(hash);

        Assert.Equal(cachePath, result);
    }

    [Fact]
    public async Task GetCachedPathAsync_WhenCacheHashDoesNotMatch_DeletesCachedFile()
    {
        var expectedHash = await ComputeHashAsync("expected"u8.ToArray());
        var cachePath = Path.Combine(tempDir, $"{expectedHash}.cache");
        await File.WriteAllBytesAsync(cachePath, "actual"u8.ToArray());
        using var source = CreateSource(new ByteArrayContent([]));
        using var service = CreateService(source);

        var result = await service.GetCachedPathAsync(expectedHash);

        Assert.Null(result);
        Assert.False(File.Exists(cachePath));
    }

    [Fact]
    public async Task CacheImageAsync_WhenConcurrentCallsUseSameHash_DownloadsOnce()
    {
        var bytes = "downloaded-image"u8.ToArray();
        var hash = await ComputeHashAsync(bytes);
        var handler = new CountingHandler(bytes);
        using IHttpClientLeaseSource source = new FixedHttpClientLeaseSource(
            handler,
            baseAddress: null,
            timeout: Timeout.InfiniteTimeSpan);
        using var service = CreateService(source);

        var results = await Task.WhenAll(
            service.CacheImageAsync("https://images.example.invalid/a.png", hash),
            service.CacheImageAsync("https://images.example.invalid/a.png", hash));

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(results[0], results[1]);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(results[0]));
    }

    [Fact]
    public async Task GetCachedOrDownloadImageBytesAsync_WhenUrlIsRequestedTwice_ReusesDiskCache()
    {
        var bytes = "remote-banner"u8.ToArray();
        var handler = new CountingHandler(bytes);
        using IHttpClientLeaseSource source = new FixedHttpClientLeaseSource(
            handler,
            baseAddress: null,
            timeout: Timeout.InfiniteTimeSpan);
        using var service = CreateService(source);

        var first = await service.GetCachedOrDownloadImageBytesAsync(
            "https://images.example.invalid/banner.png",
            ProxyModes.Direct);
        var second = await service.GetCachedOrDownloadImageBytesAsync(
            "https://images.example.invalid/banner.png",
            ProxyModes.Direct);

        Assert.Equal(bytes, first);
        Assert.Equal(bytes, second);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("../hash")]
    [InlineData("folder/hash")]
    [InlineData("folder\\hash")]
    public async Task CacheImageAsync_WhenHashContainsPathSyntax_Throws(string hash)
    {
        using var source = CreateSource(new ByteArrayContent([]));
        using var service = CreateService(source);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CacheImageAsync("https://images.example.invalid/a.png", hash));
    }

    [Fact]
    public async Task CacheImageAsync_WhenDownloadedHashDoesNotMatch_RemovesTemporaryFile()
    {
        var expectedHash = await ComputeHashAsync("expected"u8.ToArray());
        using var source = CreateSource(new ByteArrayContent("actual"u8.ToArray()));
        using var service = CreateService(source);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.CacheImageAsync(
                "https://images.example.invalid/a.png",
                expectedHash));

        Assert.Empty(Directory.EnumerateFiles(tempDir, "*.tmp"));
        Assert.False(File.Exists(Path.Combine(tempDir, $"{expectedHash}.cache")));
    }

    [Fact]
    public async Task GetImageBytesAsync_WhenContentLengthExceedsLimit_Throws()
    {
        var content = new ByteArrayContent(new byte[25 * 1024 * 1024 + 1]);
        using var source = CreateSource(content);
        using var service = CreateService(source);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.GetImageBytesAsync("https://images.example.invalid/a.png"));
    }

    [Fact]
    public async Task GetImageBytesAsync_WhenStreamExceedsLimit_Throws()
    {
        using var source = CreateSource(new UnknownLengthContent(25 * 1024 * 1024 + 1));
        using var service = CreateService(source);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.GetImageBytesAsync("https://images.example.invalid/a.png"));
    }

    [Fact]
    public async Task GetImageBytesAsync_WhenResponseIsFailure_Throws()
    {
        using IHttpClientLeaseSource source = new FixedHttpClientLeaseSource(
            new StatusHandler(HttpStatusCode.BadGateway),
            baseAddress: null,
            timeout: Timeout.InfiniteTimeSpan);
        using var service = CreateService(source);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GetImageBytesAsync("https://images.example.invalid/a.png"));
    }

    [Fact]
    public async Task GetImageBytesAsync_WhenCanceled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var source = CreateSource(new ByteArrayContent([]));
        using var service = CreateService(source);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetImageBytesAsync(
                "https://images.example.invalid/a.png",
                cts.Token));
    }

    private ImageCacheService CreateService(IHttpClientLeaseSource source) =>
        new(
            source,
            new Crc64Service(),
            RemoteHttpUrlValidator.CreateForTesting(),
            tempDir);

    private static FixedHttpClientLeaseSource CreateSource(HttpContent content) =>
        new(
            new StaticResponseHandler(content),
            baseAddress: null,
            timeout: Timeout.InfiniteTimeSpan);

    private async Task<string> ComputeHashAsync(byte[] bytes)
    {
        var path = Path.Combine(tempDir, $"{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, bytes);
        return await new Crc64Service().ComputeFileAsync(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed class StaticResponseHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            });
    }

    private sealed class CountingHandler(byte[] bytes) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        }
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class UnknownLengthContent(int length) : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            throw new NotSupportedException();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new RepeatingByteStream(length));
    }

    private sealed class RepeatingByteStream(long length) : Stream
    {
        private long position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = length - position;
            if (remaining <= 0)
            {
                return 0;
            }

            var read = (int)Math.Min(count, remaining);
            Array.Clear(buffer, offset, read);
            position += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = length - position;
            if (remaining <= 0)
            {
                return ValueTask.FromResult(0);
            }

            var read = (int)Math.Min(buffer.Length, remaining);
            buffer.Span[..read].Clear();
            position += read;
            return ValueTask.FromResult(read);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
