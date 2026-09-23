using System.Security.Cryptography;
using System.Text;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Update;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LauncherUpdateDownloaderTests
{
    private static readonly byte[] PackageBytes = Encoding.UTF8.GetBytes("cafe launcher package payload");

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenDigestMatches_WritesDestinationAndRemovesPart()
    {
        using var directory = TestDirectory.Create();
        var destination = Path.Combine(directory, "pkg.zip");
        var downloader = new LauncherUpdateDownloader(new StubRemoteHttpTransport(_ => PackageBytes));

        var result = await downloader.DownloadAndVerifyAsync(
            PackageFile(), Sha256(PackageBytes), destination, progress: null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(destination, result.FilePath);
        Assert.Equal(
            PackageBytes,
            await System.IO.File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
        Assert.False(System.IO.File.Exists(destination + ".part"));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenDigestMismatches_ReturnsChecksumMismatchAndRemovesPart()
    {
        using var directory = TestDirectory.Create();
        var destination = Path.Combine(directory, "pkg.zip");
        var downloader = new LauncherUpdateDownloader(new StubRemoteHttpTransport(_ => PackageBytes));

        var result = await downloader.DownloadAndVerifyAsync(
            PackageFile(), new string('0', 64), destination, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(LauncherUpdateDownloadStatus.ChecksumMismatch, result.Status);
        Assert.False(System.IO.File.Exists(destination));
        Assert.False(System.IO.File.Exists(destination + ".part"));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenTransportThrowsHttp_ReturnsNetworkFailure()
    {
        using var directory = TestDirectory.Create();
        var destination = Path.Combine(directory, "pkg.zip");
        var downloader = new LauncherUpdateDownloader(
            new StubRemoteHttpTransport(_ => new HttpRequestException("boom")));

        var result = await downloader.DownloadAndVerifyAsync(
            PackageFile(), new string('0', 64), destination, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(LauncherUpdateDownloadStatus.NetworkFailure, result.Status);
        Assert.False(System.IO.File.Exists(destination));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenTransportRejectsUrl_ReturnsNetworkFailure()
    {
        using var directory = TestDirectory.Create();
        var destination = Path.Combine(directory, "pkg.zip");
        var downloader = new LauncherUpdateDownloader(
            new StubRemoteHttpTransport(_ => new InvalidOperationException("rejected")));

        var result = await downloader.DownloadAndVerifyAsync(
            PackageFile(), new string('0', 64), destination, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(LauncherUpdateDownloadStatus.NetworkFailure, result.Status);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenCancelled_ReturnsCancelled()
    {
        using var directory = TestDirectory.Create();
        var destination = Path.Combine(directory, "pkg.zip");
        var downloader = new LauncherUpdateDownloader(new StubRemoteHttpTransport(_ => PackageBytes));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await downloader.DownloadAndVerifyAsync(
            PackageFile(), Sha256(PackageBytes), destination, progress: null, cancellation.Token);

        Assert.Equal(LauncherUpdateDownloadStatus.Cancelled, result.Status);
        Assert.False(System.IO.File.Exists(destination));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenDestinationExists_OverwritesIt()
    {
        using var directory = TestDirectory.Create();
        var destination = Path.Combine(directory, "pkg.zip");
        await System.IO.File.WriteAllTextAsync(destination, "old", TestContext.Current.CancellationToken);
        var downloader = new LauncherUpdateDownloader(new StubRemoteHttpTransport(_ => PackageBytes));

        var result = await downloader.DownloadAndVerifyAsync(
            PackageFile(), Sha256(PackageBytes), destination, progress: null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            PackageBytes,
            await System.IO.File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_ReportsProgressAtLeastOnce()
    {
        using var directory = TestDirectory.Create();
        var destination = Path.Combine(directory, "pkg.zip");
        var downloader = new LauncherUpdateDownloader(new StubRemoteHttpTransport(_ => PackageBytes));
        var progress = new RecordingProgress();

        await downloader.DownloadAndVerifyAsync(
            PackageFile(), Sha256(PackageBytes), destination, progress, TestContext.Current.CancellationToken);

        Assert.NotEmpty(progress.Reports);
        Assert.Equal(PackageBytes.Length, progress.Reports[^1].DownloadedBytes);
    }

    [Fact]
    public async Task ReadTextAsync_WhenBody_ReturnsText()
    {
        var downloader = new LauncherUpdateDownloader(new StubRemoteHttpTransport(_ => "hash  name"));

        var text = await downloader.ReadTextAsync(PackageFile(), TestContext.Current.CancellationToken);

        Assert.Equal("hash  name", text);
    }

    [Fact]
    public async Task ReadTextAsync_WhenStreamRejectsSynchronousReads_ReturnsText()
    {
        var bytes = Encoding.UTF8.GetBytes("hash  name");
        var body = new RemoteBody(new AsyncOnlyReadStream(bytes), bytes.LongLength);
        var downloader = new LauncherUpdateDownloader(new StubRemoteHttpTransport(_ => body));

        var text = await downloader.ReadTextAsync(PackageFile(), TestContext.Current.CancellationToken);

        Assert.Equal("hash  name", text);
    }

    [Fact]
    public async Task ReadTextAsync_WhenTransportThrowsHttp_ReturnsNull()
    {
        var downloader = new LauncherUpdateDownloader(
            new StubRemoteHttpTransport(_ => new HttpRequestException("boom")));

        var text = await downloader.ReadTextAsync(PackageFile(), TestContext.Current.CancellationToken);

        Assert.Null(text);
    }

    [Fact]
    public async Task ReadTextAsync_WhenBodyExceedsCap_ReturnsNull()
    {
        var oversized = new string('a', LauncherUpdateDownloader.MaxTextAssetBytes + 1);
        var downloader = new LauncherUpdateDownloader(new StubRemoteHttpTransport(_ => oversized));

        var text = await downloader.ReadTextAsync(PackageFile(), TestContext.Current.CancellationToken);

        Assert.Null(text);
    }

    [Fact]
    public async Task ReadTextAsync_WhenBodyMatchesCap_ReturnsText()
    {
        var atLimit = new string('a', LauncherUpdateDownloader.MaxTextAssetBytes);
        var downloader = new LauncherUpdateDownloader(new StubRemoteHttpTransport(_ => atLimit));

        var text = await downloader.ReadTextAsync(PackageFile(), TestContext.Current.CancellationToken);

        Assert.Equal(atLimit, text);
    }

    private static ReleaseFile PackageFile() => new()
    {
        Name = "Cafe.Launcher.Avalonia_v1.2.3_win-x64.zip",
        Url = "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v1.2.3/Cafe.Launcher.Avalonia_v1.2.3_win-x64.zip",
        Size = PackageBytes.Length
    };

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class RecordingProgress : IProgress<LauncherUpdateProgress>
    {
        public List<LauncherUpdateProgress> Reports { get; } = [];

        public void Report(LauncherUpdateProgress value) => Reports.Add(value);
    }

    private sealed class AsyncOnlyReadStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("Synchronous reads are not supported.");

        public override int Read(Span<byte> buffer) =>
            throw new NotSupportedException("Synchronous reads are not supported.");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(inner.Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
