using System.Net;
using System.Net.Http;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class BackgroundViewModelTests : IDisposable
{
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");

    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public BackgroundViewModelTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenRemoteImageIsValid_LoadsAndCachesImage()
    {
        var hash = await ComputeHashAsync(PngBytes);
        using var cache = CreateCache(new ImageHandler(PngBytes));
        var themeRefreshCount = 0;
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => themeRefreshCount++,
            (path, _) => new TestImage(),
            () => new TestImage());
        var settings = new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Remote,
            ThemeColorMode = ThemeColorModes.Wallpaper
        };

        await viewModel.UpdateBackgroundImageAsync(
            settings,
            CreateRemoteSnapshot(hash),
            CancellationToken.None);

        Assert.IsType<TestImage>(viewModel.BackgroundImageSource);
        Assert.Equal(1, themeRefreshCount);
        Assert.True(File.Exists(Path.Combine(tempDir, $"{hash}.cache")));
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenRemoteImageFails_FallsBackToBundledImage()
    {
        using var cache = CreateCache(new StatusHandler(HttpStatusCode.BadGateway));
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () => new TestImage());
        var initialImage = viewModel.BackgroundImageSource;
        var settings = new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Remote
        };

        await viewModel.UpdateBackgroundImageAsync(
            settings,
            CreateRemoteSnapshot("1"),
            CancellationToken.None);

        Assert.NotSame(initialImage, viewModel.BackgroundImageSource);
        Assert.IsType<TestImage>(viewModel.BackgroundImageSource);
    }

    [Fact]
    public async Task LoadCustomBackgroundAsync_WhenFileIsInvalid_ReturnsNull()
    {
        var path = Path.Combine(tempDir, "invalid.png");
        await File.WriteAllTextAsync(path, "not-an-image");
        using var cache = CreateCache(new ImageHandler(PngBytes));
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { });

        var result = await viewModel.LoadCustomBackgroundAsync(path);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoadCustomBackgroundAsync_WhenPathDoesNotExist_ReturnsNull()
    {
        using var cache = CreateCache(new ImageHandler(PngBytes));
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { });

        var result = await viewModel.LoadCustomBackgroundAsync(
            Path.Combine(tempDir, "missing.png"));

        Assert.Null(result);
    }

    [Fact]
    public async Task LoadCustomBackgroundAsync_WhenFolderHasNoSupportedImages_ReturnsNull()
    {
        var folder = Path.Combine(tempDir, "empty");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "readme.txt"), "text");
        using var cache = CreateCache(new ImageHandler(PngBytes));
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { });

        var result = await viewModel.LoadCustomBackgroundAsync(folder);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenMotionEnabled_PreviousWallpaperFadesAsOverlay()
    {
        var hash = await ComputeHashAsync(PngBytes);
        using var cache = CreateCache(new ImageHandler(PngBytes));
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () => new TestImage());

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Remote },
            CreateRemoteSnapshot(hash),
            CancellationToken.None);
        var firstSwap = (TestImage)viewModel.BackgroundImageSource!;
        var fadingOut = new List<TestImage>();

        viewModel.PreviousWallpaperFadingOut += (image, _) => fadingOut.Add((TestImage)image);

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Remote },
            CreateRemoteSnapshot(hash),
            CancellationToken.None);

        Assert.NotSame(firstSwap, viewModel.BackgroundImageSource);
        Assert.Equal([firstSwap], fadingOut);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhileOverlayStillHoldsPreviousImage_DefersDisposalUntilOverlayReleases()
    {
        using var cache = CreateCache(new ImageHandler(PngBytes));
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () => new TestImage());

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Remote },
            CreateRemoteSnapshot("1"),
            CancellationToken.None);

        var fadingOut = new List<TestImage>();
        viewModel.PreviousWallpaperFadingOut += (image, _) => fadingOut.Add((TestImage)image);

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Remote },
            CreateRemoteSnapshot("1"),
            CancellationToken.None);
        var fading = Assert.Single(fadingOut);

        // 模拟合成器停滞：视图层在宽限期过后仍未摘除覆盖层引用。此时释放位图会让
        // 下一渲染帧在 Image.Render 读取已释放实现，抛 ObjectDisposedException 崩溃。
        await Task.Delay(TimeSpan.FromMilliseconds(700));

        Assert.False(
            fading.IsDisposed,
            "ViewModel 在覆盖层仍持有旧图时释放了位图；渲染帧会读到已释放位图。");

        viewModel.OnWallpaperOverlayReleased(fading);
        Assert.True(fading.IsDisposed);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenMotionReduced_SkipsCrossFadeOverlay()
    {
        var bundled = new TestImage();
        using var cache = CreateCache(new ImageHandler(PngBytes));
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () => bundled);
        var fadingOut = new List<IImage>();
        viewModel.PreviousWallpaperFadingOut += (image, _) => fadingOut.Add(image);
        viewModel.ApplyMotionPreference(reduceMotion: true);

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Remote },
            CreateRemoteSnapshot("1"),
            CancellationToken.None);

        Assert.Empty(fadingOut);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenRemoteDownloadIsCanceled_PropagatesCancellation()
    {
        using var cache = CreateCache(new CancellationHandler());
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () => new TestImage());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => viewModel.UpdateBackgroundImageAsync(
                new LauncherSettings { BackgroundSource = BackgroundSources.Remote },
                CreateRemoteSnapshot("1"),
                cts.Token));
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenRemoteFieldsAreMissing_UsesBundledImage()
    {
        using var cache = CreateCache(new ImageHandler(PngBytes));
        var bundled = new TestImage();
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () => bundled);

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Remote },
            new LauncherStatusSnapshot(),
            CancellationToken.None);

        Assert.Same(bundled, viewModel.BackgroundImageSource);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenCustomImageIsValid_UsesCustomImage()
    {
        var path = Path.Combine(tempDir, "custom.png");
        await File.WriteAllBytesAsync(path, PngBytes);
        using var cache = CreateCache(new ImageHandler(PngBytes));
        var custom = new TestImage();
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => custom,
            () => new TestImage());

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings
            {
                BackgroundSource = BackgroundSources.Custom,
                CustomBackgroundPath = path
            },
            null,
            CancellationToken.None);

        Assert.Same(custom, viewModel.BackgroundImageSource);
    }

    [Fact]
    public void ApplyBackgroundPresentation_WhenUniform_UsesConfiguredFillColor()
    {
        using var cache = CreateCache(new ImageHandler(PngBytes));
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { });

        viewModel.ApplyBackgroundPresentation(new LauncherSettings
        {
            BackgroundFit = BackgroundFits.Uniform,
            BackgroundFillColor = "#FF123456"
        });

        Assert.Equal(Stretch.Uniform, viewModel.BackgroundStretch);
        var brush = Assert.IsType<SolidColorBrush>(viewModel.BackgroundFillBrush);
        Assert.Equal(Color.Parse("#FF123456"), brush.Color);
    }

    [Fact]
    public void Dispose_WhenCalledTwice_DoesNotThrow()
    {
        using var cache = CreateCache(new ImageHandler(PngBytes));
        var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () => new TestImage());

        viewModel.Dispose();
        viewModel.Dispose();
    }

    [Fact]
    public async Task LoadCustomBackgroundAsync_WhenCanceled_PropagatesCancellation()
    {
        using var cache = CreateCache(new ImageHandler(PngBytes));
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => viewModel.LoadCustomBackgroundAsync(tempDir, cts.Token));
    }

    [Theory]
    [InlineData("image.png", true)]
    [InlineData("image.JPG", true)]
    [InlineData("image.jpeg", true)]
    [InlineData("image.bmp", true)]
    [InlineData("image.webp", true)]
    [InlineData("image.gif", false)]
    public void IsSupportedBackgroundImage_RecognizesExactExtensions(
        string path,
        bool expected)
    {
        Assert.Equal(expected, BackgroundViewModel.IsSupportedBackgroundImage(path));
    }

    [Theory]
    [InlineData(BackgroundFits.Fill, Stretch.Fill)]
    [InlineData(BackgroundFits.Uniform, Stretch.Uniform)]
    [InlineData(BackgroundFits.UniformToFill, Stretch.UniformToFill)]
    [InlineData("invalid", Stretch.UniformToFill)]
    public void ToStretch_MapsFitCodes(string fit, Stretch expected)
    {
        Assert.Equal(expected, BackgroundViewModel.ToStretch(fit));
    }

    private LauncherStatusSnapshot CreateRemoteSnapshot(string hash) =>
        new()
        {
            Settings = new LauncherSettings { ProxyMode = ProxyModes.Direct },
            Remote = new LauncherRemoteState
            {
                BaseConfig = new BaseConfigResponse
                {
                    LauncherBackgroundImg = "https://images.example.invalid/background.png",
                    LauncherBackgroundImgCrc64 = hash
                }
            }
        };

    private ImageCacheService CreateCache(HttpMessageHandler handler) =>
        new(
            new FixedHttpClientLeaseSource(
                handler,
                baseAddress: null,
                timeout: Timeout.InfiniteTimeSpan),
            new Crc64Service(),
            RemoteHttpUrlValidator.CreateForTesting(),
            tempDir);

    private async Task<string> ComputeHashAsync(byte[] bytes)
    {
        var path = Path.Combine(tempDir, "hash-source.png");
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

    private sealed class ImageHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken);
    }

    private sealed class TestImage : IImage, IDisposable
    {
        public Size Size => new(1, 1);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
        }

        public void Dispose()
        {
            IsDisposed = true;
        }

        public bool IsDisposed { get; private set; }
    }
}
