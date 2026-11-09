using System.Security.Cryptography;
using System.Text;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Update;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.Tests;

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
    public async Task DownloadAndVerifyAsync_WhenMirrorUrlIsPresent_PrefersMirror()
    {
        using var directory = TestDirectory.Create();
        var destination = Path.Combine(directory, "pkg.zip");
        var transport = new StubRemoteHttpTransport(uri =>
            uri.Host == "download.cafe-launcher.saibamidori.com"
                ? PackageBytes
                : throw new HttpRequestException("GitHub should not be used."));
        var downloader = new LauncherUpdateDownloader(transport);

        var result = await downloader.DownloadAndVerifyAsync(
            PackageFile(withMirror: true), Sha256(PackageBytes), destination, progress: null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(transport.RequestedUris);
        Assert.Equal("download.cafe-launcher.saibamidori.com", transport.RequestedUris[0].Host);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenMirrorFails_FallsBackToGitHub()
    {
        using var directory = TestDirectory.Create();
        var destination = Path.Combine(directory, "pkg.zip");
        var transport = new StubRemoteHttpTransport(uri =>
            uri.Host == "download.cafe-launcher.saibamidori.com"
                ? new HttpRequestException("mirror unavailable")
                : PackageBytes);
        var downloader = new LauncherUpdateDownloader(transport);

        var result = await downloader.DownloadAndVerifyAsync(
            PackageFile(withMirror: true), Sha256(PackageBytes), destination, progress: null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, transport.RequestedUris.Count);
        Assert.Equal("github.com", transport.RequestedUris[1].Host);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenMirrorBytesMismatch_FallsBackToGitHub()
    {
        using var directory = TestDirectory.Create();
        var destination = Path.Combine(directory, "pkg.zip");
        var transport = new StubRemoteHttpTransport(uri =>
            uri.Host == "download.cafe-launcher.saibamidori.com"
                ? Encoding.UTF8.GetBytes("corrupted mirror payload")
                : PackageBytes);
        var downloader = new LauncherUpdateDownloader(transport);

        var result = await downloader.DownloadAndVerifyAsync(
            PackageFile(withMirror: true), Sha256(PackageBytes), destination, progress: null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, transport.RequestedUris.Count);
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
    public async Task DownloadAndVerifyAsync_WhenTransportRejectsUrlShape_ReturnsNetworkFailure()
    {
        using var directory = TestDirectory.Create();
        var destination = Path.Combine(directory, "pkg.zip");
        var downloader = new LauncherUpdateDownloader(
            new StubRemoteHttpTransport(_ => new InvalidOperationException("rejected")));

        var result = await downloader.DownloadAndVerifyAsync(
            PackageFile(), new string('0', 64), destination, progress: null, TestContext.Current.CancellationToken);

        // 未标记的拒绝（scheme/端口等形状问题）不属于「解析到本机/内网」，
        // 不能冒充可行动的 hosts 指引。
        Assert.Equal(LauncherUpdateDownloadStatus.NetworkFailure, result.Status);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenTargetResolvesToBlockedAddress_ReturnsUrlRejected()
    {
        using var directory = TestDirectory.Create();
        var destination = Path.Combine(directory, "pkg.zip");
        var downloader = new LauncherUpdateDownloader(
            new StubRemoteHttpTransport(_ => BlockedAddressRejection()));

        var result = await downloader.DownloadAndVerifyAsync(
            PackageFile(), new string('0', 64), destination, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(LauncherUpdateDownloadStatus.UrlRejected, result.Status);
        Assert.False(System.IO.File.Exists(destination));
        Assert.False(System.IO.File.Exists(destination + ".part"));
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
        Assert.True(progress.Reports[^1].BytesPerSecond > 0);
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
    public async Task ReadTextAsync_WhenTransportThrowsHttp_PropagatesFailure()
    {
        // 传输失败不得再退化成 null：调用方靠异常区分「抓不到清单」与「清单不覆盖安装包」，
        // 否则更新失败会被归因成发布侧缺数据，日志也留不下任何痕迹（#33）。
        var downloader = new LauncherUpdateDownloader(
            new StubRemoteHttpTransport(_ => new HttpRequestException("boom")));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => downloader.ReadTextAsync(PackageFile(), TestContext.Current.CancellationToken));

        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public async Task ReadTextAsync_WhenMirrorFails_FallsBackToGitHub()
    {
        var transport = new StubRemoteHttpTransport(uri =>
            uri.Host == "github.com"
                ? "hash  name"
                : new HttpRequestException("mirror unavailable"));
        var downloader = new LauncherUpdateDownloader(transport);

        var text = await downloader.ReadTextAsync(
            PackageFile(withMirror: true), TestContext.Current.CancellationToken);

        Assert.Equal("hash  name", text);
        Assert.Equal(2, transport.RequestedUris.Count);
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

    private static ReleaseFile PackageFile(bool withMirror = false) => new()
    {
        Name = "Cafe.Launcher_v1.2.3_win-x64.zip",
        Url = "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.2.3/Cafe.Launcher_v1.2.3_win-x64.zip",
        MirrorUrl = withMirror
            ? "https://download.cafe-launcher.saibamidori.com/v1/downloads/v1.2.3/Cafe.Launcher_v1.2.3_win-x64.zip"
            : null,
        Size = PackageBytes.Length
    };

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// 构造与 <see cref="RemoteHttpUrlValidator"/> 私网拒绝完全同形的异常：类型不变，
    /// 只多带标记键，下载器据此把「本机/内网地址」与其他传输失败分开。
    /// </summary>
    private static InvalidOperationException BlockedAddressRejection()
    {
        var exception = new InvalidOperationException(
            "Remote URL resolves to a blocked network address. Blocked: 127.0.0.1");
        exception.Data[RemoteHttpUrlValidator.BlockedAddressDataKey] = true;
        return exception;
    }

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
